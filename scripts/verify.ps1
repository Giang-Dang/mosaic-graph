#!/usr/bin/env pwsh
#Requires -Version 7.0

<#
.SYNOPSIS
    Verifies the Mosaic sample service end to end.

.DESCRIPTION
    This is the gate that has to pass before a chapter tag is cut, and it is the
    script a reader runs to check that the code in the book still does what the
    book says it does. It builds the solution, checks the committed schema
    snapshot against a freshly exported one, starts the service, runs the
    chapter's query and asserts the three numbers the chapter quotes.

    Nothing here is clever on purpose. A reader who has never written a line of
    PowerShell should be able to read it top to bottom and see what is checked.

    The sh version of this script, scripts/verify.sh, does the same work for
    readers on Linux and macOS. Changes to one belong in the other.

.EXAMPLE
    pwsh scripts/verify.ps1
#>

[CmdletBinding()]
param(
    # The port the service is started on. Matches docker-compose.yml and the
    # http launch profile.
    [int] $Port = 5100,

    # How long to wait for the service to answer /health before giving up.
    [int] $StartupTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

# Invoke-WebRequest draws a progress bar by default and erases it afterwards by
# overwriting the line with spaces, which smears trailing whitespace across the
# step output. Turning it off also makes the calls measurably faster.
$ProgressPreference = 'SilentlyContinue'

# ---------------------------------------------------------------------------
# Paths and expected values
# ---------------------------------------------------------------------------

$RepoRoot           = Split-Path -Parent $PSScriptRoot
$Solution           = Join-Path $RepoRoot 'Mosaic.slnx'
$ApiProject         = Join-Path $RepoRoot 'src' 'Mosaic.Api' 'Mosaic.Api.csproj'
$CommittedSchema    = Join-Path $RepoRoot 'schema' 'mosaic.graphql'
$SamplesDir         = Join-Path $RepoRoot 'samples' 'three-approaches'
$SampleSchemaDir    = Join-Path $RepoRoot 'schema' 'samples'
$PostmanCollection  = Join-Path $RepoRoot 'postman' 'mosaic.postman_collection.json'
$PostmanEnvironment = Join-Path $RepoRoot 'postman' 'mosaic.local.postman_environment.json'
$BaseUrl            = "http://localhost:$Port"

# The three sample projects, in the order the chapter introduces them: the name
# each schema is committed under in schema/samples, and the folder under
# samples/three-approaches that produces it. All three describe the same schema
# three different ways, so all three must export exactly the same SDL.
$SampleApproaches = [ordered] @{
    'implementation-first' = 'Mosaic.Sample.ImplementationFirst'
    'code-first'           = 'Mosaic.Sample.CodeFirst'
    'schema-first'         = 'Mosaic.Sample.SchemaFirst'
}

# The chapter's query and the numbers it produces. The lookup count is the point
# of the exercise: one lookup for the product list, one per product for its
# reviews, one per review for its author. 1 + 25 + 120 = 146. A later chapter
# fixes that; until then a change in this number means the shape of the naive
# version has changed and the prose is wrong.
$VerifyQuery          = '{ products { title reviews { rating author { displayName } } } }'
$ExpectedProductCount = 25
$ExpectedReviewCount  = 120
$ExpectedLookupCount  = 146

# The request pipeline HotChocolate assembles for this service, in order. Twelve
# of these come from the default pipeline; CostAnalyzerMiddleware is inserted
# after DocumentValidationMiddleware by the cost analyzer that AddGraphQLServer
# turns on unless default security is disabled. Chapter 3 prints this list, so a
# change here is a change to the chapter.
$ExpectedPipeline = @(
    'InstrumentationMiddleware'
    'ExceptionMiddleware'
    'TimeoutMiddleware'
    'DocumentCacheMiddleware'
    'DocumentParserMiddleware'
    'DocumentValidationMiddleware'
    'CostAnalyzerMiddleware'
    'OperationCacheMiddleware'
    'OperationResolverMiddleware'
    'SkipWarmupExecutionMiddleware'
    'OperationVariableCoercionMiddleware'
    'ConcurrencyGateMiddleware'
    'OperationExecutionMiddleware'
)

# Every resolver the engine runs for $VerifyQuery does exactly one
# domain-service lookup, so this matches $ExpectedLookupCount. Plain record
# properties - title, rating, displayName - are not resolvers and are not
# counted.
$ExpectedResolverCount = 146

# ---------------------------------------------------------------------------
# Step reporting
# ---------------------------------------------------------------------------

$script:Summary = [System.Collections.Generic.List[string]]::new()
$script:Failed = $false

function Write-Ok {
    param([string] $Step)

    $line = "[ok]   $Step"
    $script:Summary.Add($line)
    Write-Host $line -ForegroundColor Green
}

function Write-Skipped {
    param([string] $Step, [string] $Reason)

    $line = "[skip] $Step - $Reason"
    $script:Summary.Add($line)
    Write-Host $line -ForegroundColor Yellow
}

# Prints the failure, records it, and unwinds to the finally block that stops the
# service. Nothing after a failed step is worth running.
function Stop-Verify {
    param([string] $Step, [string] $Detail = '')

    $line = "[FAIL] $Step"
    $script:Summary.Add($line)
    $script:Failed = $true
    Write-Host $line -ForegroundColor Red
    if ($Detail) {
        Write-Host $Detail
    }
    throw $Step
}

function Join-Lines {
    param([string[]] $Lines)

    return ($Lines -join [Environment]::NewLine)
}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

# Schemas are compared by content, not byte for byte. The exporter writes CRLF on
# Windows and LF everywhere else, while .gitattributes stores the committed copy
# with LF, so a byte comparison would fail on Windows every time for a difference
# nobody cares about. Line endings and trailing blank lines are normalised away;
# everything else is compared exactly, including case.
function Get-NormalisedText {
    param([string] $Path)

    $text = [System.IO.File]::ReadAllText($Path)
    $text = $text -replace "`r`n", "`n"
    return $text.TrimEnd([char]10) + "`n"
}

function Test-SameText {
    param([string] $PathA, [string] $PathB)

    return (Get-NormalisedText $PathA) -ceq (Get-NormalisedText $PathB)
}

# Produces a readable diff of two schema files. git gives a proper unified diff
# and everyone who cloned this repo has it; Compare-Object is the fallback.
function Get-SchemaDiff {
    param(
        [string] $ExpectedPath,
        [string] $ActualPath,
        [string] $WorkDir,
        [string] $Label
    )

    $expectedName = "$Label.expected.graphql"
    $actualName = "$Label.actual.graphql"
    [System.IO.File]::WriteAllText((Join-Path $WorkDir $expectedName), (Get-NormalisedText $ExpectedPath))
    [System.IO.File]::WriteAllText((Join-Path $WorkDir $actualName), (Get-NormalisedText $ActualPath))

    if (Get-Command git -ErrorAction SilentlyContinue) {
        # -C keeps the file names in the diff header short. autocrlf is turned off
        # because the two files were normalised a moment ago and git rewriting
        # them again would only add a warning nobody needs to read.
        $diff = (& git -C $WorkDir -c core.autocrlf=false -c core.safecrlf=false `
            --no-pager diff --no-index --no-color -- $expectedName $actualName 2>$null | Out-String)
        if ($diff.Trim()) {
            return $diff
        }
    }

    $lines = Compare-Object `
        -ReferenceObject ((Get-NormalisedText $ExpectedPath) -split "`n") `
        -DifferenceObject ((Get-NormalisedText $ActualPath) -split "`n") |
        ForEach-Object {
            $marker = if ($_.SideIndicator -eq '<=') { '-' } else { '+' }
            "$marker $($_.InputObject)"
        }

    return (Join-Lines @($lines))
}

function Get-FileSha256 {
    param([string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# True when something accepts a TCP connection on the port. Used to refuse to
# start when the port is taken, and to prove we gave it back on the way out.
function Test-PortInUse {
    param([int] $PortNumber)

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connect = $client.ConnectAsync('127.0.0.1', $PortNumber)
        return ($connect.Wait(1000) -and $client.Connected)
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

# The service is still writing to its log file while we read it, so the share
# mode has to be explicit. Get-Content would fail on Windows here.
function Get-LogText {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return ''
    }

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite)
    try {
        $reader = [System.IO.StreamReader]::new($stream)
        return $reader.ReadToEnd()
    } finally {
        $stream.Dispose()
    }
}

function Get-LogTail {
    param([string] $Path, [int] $Lines = 40)

    $text = Get-LogText $Path
    if (-not $text) {
        return '(the service printed nothing)'
    }

    $all = @($text -split "`r?`n")
    $start = [Math]::Max(0, $all.Count - $Lines)
    return (Join-Lines $all[$start..($all.Count - 1)])
}

# ---------------------------------------------------------------------------
# Verification
# ---------------------------------------------------------------------------

$apiProcess = $null
$tempDir = $null
$previousAspNetCoreUrls = $env:ASPNETCORE_URLS
$aspNetCoreUrlsWasSet = $null -ne $previousAspNetCoreUrls
$previousAspNetCoreEnvironment = $env:ASPNETCORE_ENVIRONMENT
$aspNetCoreEnvironmentWasSet = $null -ne $previousAspNetCoreEnvironment
$exitCode = 0

Write-Host "mosaic verify - $RepoRoot"
Write-Host ''

try {
    # -- 1. the SDK ---------------------------------------------------------

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Stop-Verify 'dotnet sdk' 'dotnet is not on PATH. Install the .NET SDK version pinned in global.json.'
    }

    # stderr is left alone rather than merged in: older PowerShell 7 releases turn
    # a native command's redirected stderr into a terminating error, and the
    # message is more use on the console anyway.
    $sdkVersion = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        Stop-Verify 'dotnet sdk' "dotnet --version exited with $LASTEXITCODE. global.json most likely pins an SDK that is not installed; the output above says which."
    }
    Write-Ok "dotnet sdk $sdkVersion"

    # -- 2. restore and build ----------------------------------------------

    & dotnet restore $Solution --nologo
    if ($LASTEXITCODE -ne 0) {
        Stop-Verify 'restore' 'dotnet restore failed; its output above says why.'
    }
    Write-Ok 'restore'

    & dotnet build $Solution -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) {
        Stop-Verify 'build' 'dotnet build failed. TreatWarningsAsErrors is on, so a single warning is enough to get here.'
    }
    Write-Ok 'build (Release)'

    # Everything generated from here on lands in one temporary directory, which
    # the finally block deletes. That includes the <name>-settings.json the
    # schema exporter writes next to every SDL file it produces.
    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ('mosaic-verify-' + [Guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

    # -- 3. schema drift ----------------------------------------------------

    $exportedSchema = Join-Path $tempDir 'mosaic.graphql'
    & dotnet run --project $ApiProject -c Release --no-build --no-launch-profile -- schema export --output $exportedSchema
    if ($LASTEXITCODE -ne 0) {
        Stop-Verify 'schema export' 'dotnet run -- schema export failed; its output above says why.'
    }
    if (-not (Test-Path -LiteralPath $exportedSchema)) {
        Stop-Verify 'schema export' "The exporter reported success but wrote nothing to $exportedSchema."
    }
    if (-not (Test-Path -LiteralPath $CommittedSchema)) {
        Stop-Verify 'schema drift' "There is no committed snapshot at $CommittedSchema."
    }

    if (Test-SameText $CommittedSchema $exportedSchema) {
        Write-Ok 'schema matches schema/mosaic.graphql'
    } else {
        Stop-Verify 'schema drift' (Join-Lines @(
            'The exported schema is not the one committed in schema/mosaic.graphql.'
            ''
            'If the change is deliberate, regenerate the snapshot and commit it:'
            ''
            '    dotnet run --project src/Mosaic.Api -- schema export --output schema/mosaic.graphql'
            ''
            'If it is not, a dependency changed the schema behind your back. That is'
            'what this check exists to catch.'
            ''
            (Get-SchemaDiff -ExpectedPath $CommittedSchema -ActualPath $exportedSchema -WorkDir $tempDir -Label 'mosaic')))
    }

    # -- 4. the three sample projects --------------------------------------

    if (-not (Test-Path -LiteralPath $SamplesDir)) {
        Write-Skipped 'sample schemas' 'samples/three-approaches does not exist yet'
    } else {
        $exportedSamples = @{}
        $missingProjects = @()

        foreach ($approach in $SampleApproaches.Keys) {
            $projectDir = Join-Path $SamplesDir $SampleApproaches[$approach]
            $csproj = $null
            if (Test-Path -LiteralPath $projectDir) {
                $csproj = Get-ChildItem -LiteralPath $projectDir -Filter '*.csproj' -File | Select-Object -First 1
            }

            if (-not $csproj) {
                $missingProjects += $SampleApproaches[$approach]
                continue
            }

            # No --no-build here, unlike the main service above. Mosaic.slnx
            # lists all three sample projects, so step 2 has already built them
            # and this is a no-op rebuild costing well under a second. It stays
            # because it is the one thing that keeps this step honest if a
            # fourth sample is ever added to the folder and not to the solution.
            $out = Join-Path $tempDir "$approach.graphql"
            & dotnet run --project $csproj.FullName -c Release --no-launch-profile -- schema export --output $out
            if ($LASTEXITCODE -ne 0) {
                Stop-Verify "sample schema $approach" "Exporting the schema from $($csproj.FullName) failed."
            }
            if (-not (Test-Path -LiteralPath $out)) {
                Stop-Verify "sample schema $approach" "The exporter reported success but wrote nothing to $out."
            }

            $exportedSamples[$approach] = $out
        }

        if ($missingProjects.Count -gt 0) {
            Stop-Verify 'sample schemas' (Join-Lines @(
                'samples/three-approaches exists but does not hold all three projects.'
                "Missing (no .csproj found under samples/three-approaches/): $($missingProjects -join ', ')"))
        }

        # Byte for byte here, not normalised: all three were produced by the same
        # exporter on the same machine in the same run, so any difference at all
        # is a real difference.
        $reference = @($SampleApproaches.Keys)[0]
        $referenceHash = Get-FileSha256 $exportedSamples[$reference]
        foreach ($approach in (@($SampleApproaches.Keys) | Select-Object -Skip 1)) {
            if ((Get-FileSha256 $exportedSamples[$approach]) -ne $referenceHash) {
                Stop-Verify 'sample schemas identical' (Join-Lines @(
                    "The $approach sample does not export the same SDL as the $reference one."
                    'The whole point of the three is that they describe one schema three ways.'
                    ''
                    (Get-SchemaDiff `
                        -ExpectedPath $exportedSamples[$reference] `
                        -ActualPath $exportedSamples[$approach] `
                        -WorkDir $tempDir `
                        -Label "sample-$approach")))
            }
        }
        Write-Ok 'the three sample schemas are byte-identical'

        foreach ($approach in $SampleApproaches.Keys) {
            $committedSample = Join-Path $SampleSchemaDir "$approach.graphql"
            if (-not (Test-Path -LiteralPath $committedSample)) {
                Stop-Verify "sample schema $approach" "There is no committed snapshot at $committedSample."
            }
            if (-not (Test-SameText $committedSample $exportedSamples[$approach])) {
                Stop-Verify "sample schema $approach" (Join-Lines @(
                    "schema/samples/$approach.graphql is not what the project exports."
                    ''
                    (Get-SchemaDiff `
                        -ExpectedPath $committedSample `
                        -ActualPath $exportedSamples[$approach] `
                        -WorkDir $tempDir `
                        -Label "committed-$approach")))
            }
        }
        Write-Ok 'sample schemas match schema/samples'
    }

    # -- 5. start the service and run the chapter's query ------------------

    if (Test-PortInUse $Port) {
        Stop-Verify 'start api' (Join-Lines @(
            "Something is already listening on port $Port."
            'Stop it first: a docker compose stack, a debugger, or an earlier run of this script.'))
    }

    $apiStdout = Join-Path $tempDir 'api.out.log'
    $apiStderr = Join-Path $tempDir 'api.err.log'

    # The URL goes in through the environment rather than the command line:
    # RunWithGraphQLCommands parses the process arguments itself, and it should
    # not have to know about --urls.
    $env:ASPNETCORE_URLS = $BaseUrl

    # --no-launch-profile means launchSettings.json is ignored, and without it
    # ASP.NET Core defaults to Production. HotChocolate 16 answers introspection
    # only in Development, so the Postman collection's introspection request
    # would fail with HC0046. Say Development explicitly rather than relying on
    # a profile this script deliberately does not load.
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $apiProcess = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', $ApiProject, '-c', 'Release', '--no-build', '--no-launch-profile') `
        -WorkingDirectory $RepoRoot `
        -RedirectStandardOutput $apiStdout `
        -RedirectStandardError $apiStderr `
        -NoNewWindow `
        -PassThru

    $healthDeadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $healthy = $false
    while ((Get-Date) -lt $healthDeadline) {
        if ($apiProcess.HasExited) {
            Stop-Verify 'start api' (Join-Lines @(
                "The service exited with code $($apiProcess.ExitCode) during start-up."
                ''
                (Get-LogTail $apiStdout)
                (Get-LogTail $apiStderr)))
        }

        try {
            $health = Invoke-WebRequest -Uri "$BaseUrl/health" -TimeoutSec 5 -SkipHttpErrorCheck
            if ($health.StatusCode -eq 200) {
                $healthy = $true
                break
            }
        } catch {
            # Not listening yet. Keep polling until the deadline.
        }

        Start-Sleep -Milliseconds 500
    }

    if (-not $healthy) {
        Stop-Verify 'start api' (Join-Lines @(
            "$BaseUrl/health did not answer within $StartupTimeoutSeconds seconds."
            ''
            (Get-LogTail $apiStdout)
            (Get-LogTail $apiStderr)))
    }
    Write-Ok "api answering on $BaseUrl/health"

    # -TimeoutSec is spelled that way for PowerShell 7.0; on 7.5 and later it is
    # an alias for -ConnectionTimeoutSeconds. Either way the service has already
    # answered /health by this point, so it is only a backstop.
    $requestBody = @{ query = $VerifyQuery } | ConvertTo-Json -Compress
    $response = Invoke-WebRequest `
        -Uri "$BaseUrl/graphql" `
        -Method Post `
        -ContentType 'application/json' `
        -Headers @{ Accept = 'application/json' } `
        -Body $requestBody `
        -TimeoutSec 120 `
        -SkipHttpErrorCheck

    if ($response.StatusCode -ne 200) {
        Stop-Verify 'graphql query' (Join-Lines @(
            "POST $BaseUrl/graphql answered $($response.StatusCode)."
            $response.Content))
    }

    $payload = $response.Content | ConvertFrom-Json

    if ($payload.PSObject.Properties.Name -contains 'errors') {
        Stop-Verify 'graphql query' (Join-Lines @(
            'The response carries an errors key. The query is supposed to succeed outright.'
            ($payload.errors | ConvertTo-Json -Depth 10)))
    }

    $products = @()
    if (($payload.PSObject.Properties.Name -contains 'data') -and $payload.data) {
        if ($payload.data.PSObject.Properties.Name -contains 'products') {
            $products = @($payload.data.products)
        }
    }

    if ($products.Count -ne $ExpectedProductCount) {
        Stop-Verify 'product count' "Expected $ExpectedProductCount products, got $($products.Count)."
    }

    $reviewCount = 0
    foreach ($product in $products) {
        $reviewCount += @($product.reviews).Count
    }

    if ($reviewCount -ne $ExpectedReviewCount) {
        Stop-Verify 'review count' "Expected $ExpectedReviewCount reviews across all products, got $reviewCount."
    }
    Write-Ok "query returned $ExpectedProductCount products and $ExpectedReviewCount reviews"

    # -- 6. the lookup count -----------------------------------------------

    # The middleware logs the total after the response has been written, so the
    # line can land a moment after the HTTP call returns.
    $logDeadline = (Get-Date).AddSeconds(15)
    $loggedCounts = @()
    while ((Get-Date) -lt $logDeadline) {
        $loggedCounts = @(
            [regex]::Matches((Get-LogText $apiStdout), 'Service lookups this request: (\d+)') |
                ForEach-Object { $_.Groups[1].Value })
        if ($loggedCounts.Count -gt 0) {
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if ($loggedCounts.Count -eq 0) {
        Stop-Verify 'lookup count' (Join-Lines @(
            'The service never logged a lookup count for the query.'
            'Either the counting middleware is gone or the log level hides it.'
            ''
            (Get-LogTail $apiStdout)))
    }

    if ($loggedCounts -notcontains "$ExpectedLookupCount") {
        Stop-Verify 'lookup count' (Join-Lines @(
            "Expected the service to log 'Service lookups this request: $ExpectedLookupCount'."
            "It logged: $($loggedCounts -join ', ')."
            ''
            "That number is quoted in the book: 1 lookup for the product list, $ExpectedProductCount for"
            "their reviews, $ExpectedReviewCount for the review authors. If it moved, either the seed data"
            'or the resolvers changed and the chapter needs rewriting - or someone'
            'fixed the N+1 early.'))
    }
    Write-Ok "service logged 'Service lookups this request: $ExpectedLookupCount'"

    # -- 6b. the request pipeline ------------------------------------------

    # The pipeline is logged once, while the schema is being built, so by the
    # time a query has been answered these lines are already there.
    $logText = Get-LogText $apiStdout

    $loggedPipeline = @(
        [regex]::Matches($logText, '(?m)^\s+\d+\. (\S+)\s*$') |
            ForEach-Object { $_.Groups[1].Value })

    if ($loggedPipeline.Count -eq 0) {
        Stop-Verify 'request pipeline' (Join-Lines @(
            'The service never logged its request pipeline.'
            'AddPipelineReport() is what writes it; check it is still registered'
            'in Program.cs, and registered after AddGraphQL().'
            ''
            (Get-LogTail $apiStdout)))
    }

    if ($loggedPipeline.Count -ne $ExpectedPipeline.Count) {
        Stop-Verify 'request pipeline' (Join-Lines @(
            "Expected $($ExpectedPipeline.Count) middleware in the pipeline, found $($loggedPipeline.Count)."
            "Found: $($loggedPipeline -join ', ')."
            ''
            'Chapter 3 prints this list and counts it. If HotChocolate changed the'
            'default pipeline, the chapter needs rewriting, not this assertion.'))
    }

    for ($i = 0; $i -lt $ExpectedPipeline.Count; $i++) {
        if ($loggedPipeline[$i] -ne $ExpectedPipeline[$i]) {
            Stop-Verify 'request pipeline' (Join-Lines @(
                "Middleware $($i + 1) should be $($ExpectedPipeline[$i]) but was $($loggedPipeline[$i])."
                "Full pipeline: $($loggedPipeline -join ', ')."
                ''
                'The order is the spine of chapter 3. Do not reorder it to make'
                'this pass; work out what moved and why.'))
        }
    }
    Write-Ok "request pipeline is the expected $($ExpectedPipeline.Count) middleware, in order"

    # -- 6c. the request timeline ------------------------------------------

    $timelineDeadline = (Get-Date).AddSeconds(15)
    $loggedResolvers = @()
    while ((Get-Date) -lt $timelineDeadline) {
        $loggedResolvers = @(
            [regex]::Matches((Get-LogText $apiStdout), '(\d+) resolvers\)') |
                ForEach-Object { $_.Groups[1].Value })
        if ($loggedResolvers.Count -gt 0) {
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if ($loggedResolvers.Count -eq 0) {
        Stop-Verify 'request timeline' (Join-Lines @(
            'The service never logged a request timeline.'
            'RequestTimelineListener is what writes it. It is registered through'
            'AddDiagnosticEventListener, and it needs AddApplicationService<ILoggerFactory>()'
            'alongside it or the schema will not build at all.'
            ''
            (Get-LogTail $apiStdout)))
    }

    if ($loggedResolvers -notcontains "$ExpectedResolverCount") {
        Stop-Verify 'request timeline' (Join-Lines @(
            "Expected the timeline to report $ExpectedResolverCount resolvers for the query."
            "It reported: $($loggedResolvers -join ', ')."
            ''
            'Chapter 3 makes a point of this matching the lookup count exactly:'
            'every resolver the engine runs does one domain-service lookup, and'
            'the plain record properties are not resolvers at all.'))
    }
    Write-Ok "request timeline reported $ExpectedResolverCount resolvers"

    # -- 7. the postman collection -----------------------------------------

    $newmanCommand = $null
    $newmanPrefix = @()
    $localNewman = Join-Path $RepoRoot 'node_modules' '.bin' ($IsWindows ? 'newman.cmd' : 'newman')

    if (Test-Path -LiteralPath $localNewman) {
        $newmanCommand = $localNewman
    } elseif (Get-Command npx -ErrorAction SilentlyContinue) {
        # --no means "use what is already installed, do not download anything".
        & npx --no newman --version *> $null
        if ($LASTEXITCODE -eq 0) {
            $newmanCommand = 'npx'
            $newmanPrefix = @('--no', 'newman')
        }
    }

    if (-not (Test-Path -LiteralPath $PostmanCollection) -or -not (Test-Path -LiteralPath $PostmanEnvironment)) {
        Write-Skipped 'postman' 'the collection or its environment is not in postman/ yet'
    } elseif (-not $newmanCommand) {
        Write-Skipped 'postman' 'newman is not installed - run npm install first'
    } else {
        # baseUrl is overridden rather than trusted: the environment file says
        # 5100, and this script can be pointed at another port.
        $newmanArgs = $newmanPrefix + @(
            'run', $PostmanCollection,
            '--environment', $PostmanEnvironment,
            '--env-var', "baseUrl=$BaseUrl",
            '--bail')
        & $newmanCommand @newmanArgs
        if ($LASTEXITCODE -ne 0) {
            Stop-Verify 'postman' "newman exited with $LASTEXITCODE; its output above says which request failed."
        }
        Write-Ok 'postman collection'
    }
} catch {
    $exitCode = 1
    if (-not $script:Failed) {
        # Something threw that was not one of our own checks.
        $message = "[FAIL] unexpected error: $($_.Exception.Message)"
        $script:Summary.Add($message)
        Write-Host $message -ForegroundColor Red
        Write-Host $_.ScriptStackTrace
    }
} finally {
    # -- 8. give the machine back the way we found it ----------------------

    if ($apiProcess -and -not $apiProcess.HasExited) {
        try {
            # dotnet run launches the application as a child process, so the whole
            # tree has to go. Killing only the process we started leaves the app
            # holding the port.
            $apiProcess.Kill($true)
            [void] $apiProcess.WaitForExit(15000)
        } catch {
            Write-Host "Could not stop the service (pid $($apiProcess.Id)): $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    if ($apiProcess) {
        $portDeadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $portDeadline -and (Test-PortInUse $Port)) {
            Start-Sleep -Milliseconds 250
        }
        if (Test-PortInUse $Port) {
            Write-Host "Warning: something is still listening on port $Port after the service was stopped." -ForegroundColor Yellow
        }
    }

    if ($aspNetCoreUrlsWasSet) {
        $env:ASPNETCORE_URLS = $previousAspNetCoreUrls
    } else {
        Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
    }

    if ($aspNetCoreEnvironmentWasSet) {
        $env:ASPNETCORE_ENVIRONMENT = $previousAspNetCoreEnvironment
    } else {
        Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    }

    if ($tempDir -and (Test-Path -LiteralPath $tempDir)) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host '--- summary ---'
foreach ($line in $script:Summary) {
    Write-Host $line
}

if ($exitCode -eq 0) {
    Write-Host 'PASS' -ForegroundColor Green
} else {
    Write-Host 'FAIL' -ForegroundColor Red
}

exit $exitCode
