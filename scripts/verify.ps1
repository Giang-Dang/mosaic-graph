#!/usr/bin/env pwsh
#Requires -Version 7.0

<#
.SYNOPSIS
    Verifies the Mosaic sample service end to end.

.DESCRIPTION
    This is the gate that has to pass before a chapter tag is cut, and it is the
    script a reader runs to check that the code in the book still does what the
    book says it does. It starts the database, builds the solution, checks the
    committed schema snapshot against a freshly exported one, starts the
    service, runs the chapter's query and asserts the numbers the chapter
    quotes.

    Since chapter 4 this needs Docker: Mosaic's data lives in PostgreSQL and the
    script brings the container up itself. Everything else it needs is the .NET
    SDK pinned in global.json.

    Since chapter 5 the run starts by dropping Mosaic's schema and reseeding it.
    The Postman collection submits a review, so a run leaves the database
    changed, and a gate whose result depends on how many times it has been run
    is not a gate. Do not point this at a database holding anything you want.

    Since chapter 7 it also verifies the federated-wire sample: it composes the
    two subgraph schemas with wgc, starts both subgraphs and the Cosmo Router,
    runs a second Postman collection against all three, and checks that the
    requests the router sent are the ones the chapter prints. That section
    needs Docker again, pulls the router image the first time, and can be
    turned off with -SkipWire.

    Since chapter 8 there are two services rather than one. Catalog left Mosaic
    and both halves are Apollo Federation subgraphs now, so this script starts
    both, checks both committed schemas against a fresh export and against what
    each service publishes through _service, and asserts the numbers chapter 8
    quotes for _entities. It also composes the two with wgc. Chapter 8 shows no
    composition at all - that is chapter 9's subject - but a pair of subgraphs
    that has quietly stopped composing is exactly the kind of breakage a gate
    exists to catch.

    Since chapter 10 there is a router in front of those two services. This
    script starts it from docker-compose.yml, runs a third Postman collection
    against it - the storefront query that no single service can answer, and
    the plan the router made to answer it - and then runs scripts/router-cases.mjs,
    which reproduces the three router behaviours chapter 10 calls surprising.
    That section can be turned off with -SkipRouter.

    Nothing here is clever on purpose. A reader who has never written a line of
    PowerShell should be able to read it top to bottom and see what is checked.

    The sh version of this script, scripts/verify.sh, does the same work for
    readers on Linux and macOS. Changes to one belong in the other.

.EXAMPLE
    pwsh scripts/verify.ps1
#>

[CmdletBinding()]
param(
    # The port Mosaic is started on. Matches docker-compose.yml and the http
    # launch profile.
    [int] $Port = 5100,

    # The Catalog subgraph, extracted in chapter 8.
    [int] $CatalogSubgraphPort = 5101,

    # Chapter 7's federated-wire sample: two subgraphs on the host and the
    # Cosmo Router in a container. All three match the launch profiles, the
    # routing URLs in samples/federated-wire/graph.yaml, and docker-compose.yml.
    [int] $CatalogPort = 5201,
    [int] $ReviewsPort = 5202,
    [int] $RouterPort = 3002,

    # Skip the federated-wire section. It is the slowest part of a run and one
    # of the two that pull a container image from a registry.
    [switch] $SkipWire,

    # Skip chapter 10's router section. Same image as the wire router, so the
    # pull is shared, but it starts five containers of its own across the
    # collection and the three router cases.
    [switch] $SkipRouter,

    # How long to wait for the service to answer /health before giving up.
    [int] $StartupTimeoutSeconds = 60,

    # How long to wait for PostgreSQL to report healthy.
    [int] $DatabaseTimeoutSeconds = 90,

    # Leave the database container running when the script finishes. Off by
    # default so a verification run gives the machine back the way it found it;
    # useful while iterating, because starting PostgreSQL is the slowest step.
    [switch] $KeepDatabase
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
$PostmanCollection  = Join-Path $RepoRoot 'postman' 'mosaic-federation.postman_collection.json'
$PostmanEnvironment = Join-Path $RepoRoot 'postman' 'mosaic-federation.local.postman_environment.json'
$BaseUrl            = "http://localhost:$Port"

# -- chapter 8's second subgraph ---------------------------------------------

$CatalogProject     = Join-Path $RepoRoot 'src' 'Mosaic.Catalog' 'Mosaic.Catalog.csproj'
$CatalogSchema      = Join-Path $RepoRoot 'schema' 'catalog.graphql'
$CatalogSubgraphUrl = "http://localhost:$CatalogSubgraphPort"
$FederationGraph    = Join-Path $RepoRoot 'federation' 'mosaic.yaml'

# -- chapter 9's composition -------------------------------------------------

# The composed router execution config is committed, unlike chapter 7's, which
# is a build artifact and gitignored. The difference is that chapter 9 prints
# what is inside this one, so it has to be a file a reader can open and the
# gate has to notice when a fresh compose stops matching it.
$FederationSupergraph = Join-Path $RepoRoot 'federation' 'supergraph.json'
$CompositionCases     = Join-Path $RepoRoot 'scripts' 'composition-cases.mjs'

# -- chapter 10's router -----------------------------------------------------

# The router runs from docker-compose.yml like any other service and mounts the
# two files below: the graph chapter 9 composed, and its own configuration. It
# publishes the same port as chapter 7's wire-router, which is safe only
# because the two sections start and stop in sequence.
$RouterConfig     = Join-Path $RepoRoot 'router' 'config.yaml'
$RouterPostman    = Join-Path $RepoRoot 'postman' 'mosaic-router.postman_collection.json'
$RouterPostmanEnv = Join-Path $RepoRoot 'postman' 'mosaic-router.local.postman_environment.json'
$RouterCases      = Join-Path $RepoRoot 'scripts' 'router-cases.mjs'
$MosaicRouterUrl  = "http://localhost:$RouterPort"

# -- chapter 7's federated-wire sample ---------------------------------------

$WireDir            = Join-Path $RepoRoot 'samples' 'federated-wire'
$WireGraph          = Join-Path $WireDir 'graph.yaml'
$WireSupergraph     = Join-Path $WireDir 'supergraph.json'
$WirePostman        = Join-Path $RepoRoot 'postman' 'federated-wire.postman_collection.json'
$WirePostmanEnv     = Join-Path $RepoRoot 'postman' 'federated-wire.local.postman_environment.json'
$CatalogUrl         = "http://localhost:$CatalogPort"
$ReviewsUrl         = "http://localhost:$ReviewsPort"
$RouterUrl          = "http://localhost:$RouterPort"

# The two subgraphs: the project that produces each one, and the schema it has
# to keep publishing. Both files are what `_service { sdl }` returns, which is
# what a composer reads, so this is a check on the federated contract and not
# only on the SDL.
$WireSubgraphs = [ordered] @{
    'catalog' = @{
        Project = Join-Path $WireDir 'Mosaic.Sample.Wire.Catalog'
        Schema  = Join-Path $SampleSchemaDir 'wire-catalog.graphql'
        Url     = $CatalogUrl
        Port    = $CatalogPort
    }
    'reviews' = @{
        Project = Join-Path $WireDir 'Mosaic.Sample.Wire.Reviews'
        Schema  = Join-Path $SampleSchemaDir 'wire-reviews.graphql'
        Url     = $ReviewsUrl
        Port    = $ReviewsPort
    }
}

# What chapter 7 prints, asserted against what the subgraphs actually received.
# These are the two request bodies the router sent while the Postman collection
# ran, quoted exactly as the chapter quotes them. A change to the router's
# planning shows up here as a failed gate rather than as a stale listing.
$ExpectedCatalogFetch = '{"query":"{products {title price __typename id}}"}'
$ExpectedReviewsFetch =
    '{"variables":{"representations":[{"__typename":"Product","id":"1"},' +
    '{"__typename":"Product","id":"2"},{"__typename":"Product","id":"3"}]},' +
    '"query":"query($representations: [_Any!]!){_entities(representations: $representations)' +
    '{... on Product {__typename reviews {rating body}}}}"}'

# The three sample projects, in the order the chapter introduces them: the name
# each schema is committed under in schema/samples, and the folder under
# samples/three-approaches that produces it. All three describe the same schema
# three different ways, so all three must export exactly the same SDL.
$SampleApproaches = [ordered] @{
    'implementation-first' = 'Mosaic.Sample.ImplementationFirst'
    'code-first'           = 'Mosaic.Sample.CodeFirst'
    'schema-first'         = 'Mosaic.Sample.SchemaFirst'
}

# The chapter's query and the numbers it produces.
#
# Chapters 2 to 5 asked this of one service:
#
#   { products { title reviews(first: 12) { nodes { rating author { displayName } } } } }
#
# Neither service can answer it since chapter 8. `products` is Catalog's and
# `reviews` is Mosaic's, and until chapter 10 puts a router in front of them
# nothing joins the two. So the question is asked in two halves, which is
# exactly what chapter 8 is about.
#
# The Catalog half is the plain root field.
$CatalogQuery         = '{ products { id title } }'
$ExpectedProductCount = 25

# The Mosaic half is the same nested selection, reached the way a router would
# reach it: one _entities call carrying every product key Catalog just handed
# over. first: 12 is not arbitrary - the most reviewed product has exactly 12,
# so this still asks for every review in the seed data and the total is still
# 120.
$MosaicEntitiesQuery =
    'query($representations: [_Any!]!) { _entities(representations: $representations) ' +
    '{ ... on Product { reviews(first: 12) { nodes { rating author { id displayName } } } } } }'
$ExpectedReviewCount = 120

# The resolver count did not move, and that is the point. It was one root field
# plus 25 review connections plus 120 authors; it is now one _entities field
# plus 25 plus 120. Same shape, same number, different first term.
$ExpectedResolverCount = 146

# The statement count did move, by exactly one. As a monolith this query cost
# three: the products, their reviews, and the twelve distinct authors. Mosaic
# no longer fetches the products, so it costs two, and the one that left is the
# statement Catalog runs instead.
$ExpectedSqlCommandCount = 2

# The lookup counter follows the statement count for the same reason: Mosaic
# asks its own domains two questions, the reviews batch and the authors batch,
# and no longer asks Catalog anything because it cannot.
$ExpectedLookupCount = 2

# Catalog's reference resolver sits behind the same DataLoader Product.node
# uses, so a batch of any size costs one statement. This is the assertion that
# would catch a subgraph resolving representations one at a time, which no
# assertion on the answer could see.
$CatalogEntitiesQuery =
    'query($representations: [_Any!]!) { _entities(representations: $representations) ' +
    '{ ... on Product { title sku } } }'

# An order is the other direction: Mosaic hands out a product key it cannot
# resolve itself. The orders query below also selects Order.total, which is the
# regression test for the Include that was missing from chapter 4 until chapter
# 8 - total throws when the lines are not loaded, so selecting it is enough.

# How many times the verify query is sent, and how far above the expected
# number a single run is allowed to land. See the comment beside the repeat
# loop: one split batch costs one extra statement.
$VerifyQueryRuns = 5
$SplitBatchAllowance = 1

# The request pipeline HotChocolate assembles for this service, in order. Twelve
# of these come from the default pipeline; CostAnalyzerMiddleware is inserted
# after DocumentValidationMiddleware by the cost analyzer that AddGraphQLServer
# turns on unless default security is disabled. Chapter 3 prints this list, so a
# change here is a change to the chapter.
#
# AddApolloFederation() did not touch it. Chapter 8 asserts that in prose, so
# this list staying at thirteen is part of chapter 8's evidence as well as
# chapter 3's.
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
$catalogProcess = $null
$tempDir = $null
$startedDatabase = $false
$wireProcesses = @{}
$startedRouter = $false
$startedMosaicRouter = $false
$previousAspNetCoreUrls = $env:ASPNETCORE_URLS
$aspNetCoreUrlsWasSet = $null -ne $previousAspNetCoreUrls
$previousAspNetCoreEnvironment = $env:ASPNETCORE_ENVIRONMENT
$aspNetCoreEnvironmentWasSet = $null -ne $previousAspNetCoreEnvironment
$previousResetDatabase = $env:MOSAIC_RESET_DATABASE
$resetDatabaseWasSet = $null -ne $previousResetDatabase
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

    # -- 1b. the database ---------------------------------------------------

    # Mosaic has needed PostgreSQL since chapter 4, and docker-compose.yml is
    # the only description of it, so the script starts it from there rather
    # than asking the reader to remember a command.
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Stop-Verify 'database' (Join-Lines @(
            'docker is not on PATH, and Mosaic has needed PostgreSQL since chapter 4.'
            'Install Docker, or point ConnectionStrings__Mosaic at a PostgreSQL 18'
            'server you already have and start this script with the database up.'))
    }

    & docker compose --project-directory $RepoRoot up --detach --wait --wait-timeout $DatabaseTimeoutSeconds mosaic-db
    if ($LASTEXITCODE -ne 0) {
        Stop-Verify 'database' (Join-Lines @(
            "docker compose up exited with $LASTEXITCODE."
            'If the container is restarting, read its log: the PostgreSQL 18 image'
            'refuses to start against a volume written by an earlier major version.'
            ''
            '    docker compose logs mosaic-db'))
    }
    $startedDatabase = $true
    Write-Ok 'postgres is up and healthy'

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

    # Two schemas since chapter 8, checked the same way. Each subgraph is a
    # separate contract with the composer, so a drift in either is a drift.
    $Subgraphs = [ordered] @{
        'mosaic'  = @{ Project = $ApiProject;     Committed = $CommittedSchema; Url = $BaseUrl;     Port = $Port }
        'catalog' = @{ Project = $CatalogProject; Committed = $CatalogSchema;   Url = $CatalogSubgraphUrl;  Port = $CatalogSubgraphPort }
    }

    foreach ($name in $Subgraphs.Keys) {
        $subgraph = $Subgraphs[$name]
        $exportedSchema = Join-Path $tempDir "$name.exported.graphql"
        & dotnet run --project $subgraph.Project -c Release --no-build --no-launch-profile -- schema export --output $exportedSchema
        if ($LASTEXITCODE -ne 0) {
            Stop-Verify "schema export ($name)" 'dotnet run -- schema export failed; its output above says why.'
        }
        if (-not (Test-Path -LiteralPath $exportedSchema)) {
            Stop-Verify "schema export ($name)" "The exporter reported success but wrote nothing to $exportedSchema."
        }
        if (-not (Test-Path -LiteralPath $subgraph.Committed)) {
            Stop-Verify "schema drift ($name)" "There is no committed snapshot at $($subgraph.Committed)."
        }

        if (-not (Test-SameText $subgraph.Committed $exportedSchema)) {
            $relative = $subgraph.Committed.Substring($RepoRoot.Length + 1).Replace('\', '/')
            Stop-Verify "schema drift ($name)" (Join-Lines @(
                "The exported schema is not the one committed in $relative."
                ''
                'If the change is deliberate, regenerate the snapshot and commit it:'
                ''
                "    dotnet run --project $($subgraph.Project) -- schema export --output $relative"
                ''
                'If it is not, a dependency changed the schema behind your back. That is'
                'what this check exists to catch. Since chapter 8 it also catches a'
                'change to one subgraph that would break composition with the other.'
                ''
                (Get-SchemaDiff -ExpectedPath $subgraph.Committed -ActualPath $exportedSchema -WorkDir $tempDir -Label $name)))
        }

        $subgraph.Exported = $exportedSchema
    }
    Write-Ok 'both subgraph schemas match the committed snapshots'

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

    # -- 4b. the entity-attribute-placement sample -------------------------

    # Chapter 8 prints this sample's SDL as the evidence that
    # [ReferenceResolver] inside an [ObjectType<T>] class becomes an ordinary
    # field. The leaked field is the whole finding, so a HotChocolate release
    # that stopped leaking it would make the chapter wrong, and this is where
    # that would show up.
    $PlacementProject = Join-Path $RepoRoot 'samples' 'entity-attribute-placement'
    $PlacementSchema = Join-Path $SampleSchemaDir 'entity-attribute-placement.graphql'

    if (-not (Test-Path -LiteralPath $PlacementProject)) {
        Write-Skipped 'placement sample' 'samples/entity-attribute-placement does not exist yet'
    } else {
        $placementExport = Join-Path $tempDir 'entity-attribute-placement.graphql'
        & dotnet run --project $PlacementProject -c Release --no-build --no-launch-profile -- schema export --output $placementExport
        if ($LASTEXITCODE -ne 0) {
            Stop-Verify 'placement sample' 'Exporting the entity-attribute-placement schema failed.'
        }
        if (-not (Test-SameText $PlacementSchema $placementExport)) {
            Stop-Verify 'placement sample' (Join-Lines @(
                'schema/samples/entity-attribute-placement.graphql is not what the project exports.'
                ''
                (Get-SchemaDiff -ExpectedPath $PlacementSchema -ActualPath $placementExport `
                    -WorkDir $tempDir -Label 'placement')))
        }

        # The leak by name, because that is the claim rather than the file
        # being unchanged. Alpha puts [ReferenceResolver] on the type extension
        # class and Bravo puts it on the record.
        $placementSdl = Get-NormalisedText $PlacementSchema
        if ($placementSdl -notmatch '(?s)type Alpha @key\(fields: "id"\) \{\s*resolveByKey') {
            Stop-Verify 'placement sample' (Join-Lines @(
                'Alpha no longer publishes resolveByKey as a field.'
                'Chapter 8 is built on that leak. If HotChocolate stopped turning a'
                '[ReferenceResolver] method in an [ObjectType<T>] class into an ordinary'
                'field, the chapter needs rewriting rather than this check needing'
                'loosening.'))
        }
        if ($placementSdl -match '(?s)type Bravo @key\(fields: "id"\) \{\s*resolveByKey') {
            Stop-Verify 'placement sample' 'Bravo leaked resolveByKey, which is the placement chapter 8 calls safe.'
        }
        Write-Ok 'the placement sample still leaks exactly the field chapter 8 prints'
    }

    # -- 5. start both subgraphs and ask the chapter's question in halves ---

    foreach ($name in $Subgraphs.Keys) {
        if (Test-PortInUse $Subgraphs[$name].Port) {
            Stop-Verify "start $name" (Join-Lines @(
                "Something is already listening on port $($Subgraphs[$name].Port)."
                'Stop it first: a docker compose stack, a debugger, or an earlier run of this script.'))
        }
    }

    $apiStdout = Join-Path $tempDir 'api.out.log'
    $apiStderr = Join-Path $tempDir 'api.err.log'
    $catalogStdout = Join-Path $tempDir 'catalog.out.log'
    $catalogStderr = Join-Path $tempDir 'catalog.err.log'

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

    # Chapter 5 gave Mosaic a mutation, and the Postman collection uses it. A
    # verification run therefore leaves a review behind, and the next run would
    # start with 121 of them and fail the seeded-count assertion above. So each
    # run starts from a dropped and reseeded schema. That costs a second or two
    # and buys a gate whose result does not depend on how many times it has been
    # run before, which is the only kind worth having.
    $env:MOSAIC_RESET_DATABASE = '1'
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

    # -- 5b. the Catalog subgraph ------------------------------------------

    # Started after Mosaic rather than beside it, because both of them create
    # and seed a database on the way up and doing that one at a time makes a
    # failure readable.
    $env:ASPNETCORE_URLS = $CatalogSubgraphUrl
    $catalogProcess = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', $CatalogProject, '-c', 'Release', '--no-build', '--no-launch-profile') `
        -WorkingDirectory $RepoRoot `
        -RedirectStandardOutput $catalogStdout `
        -RedirectStandardError $catalogStderr `
        -NoNewWindow `
        -PassThru

    $catalogDeadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $catalogHealthy = $false
    while ((Get-Date) -lt $catalogDeadline) {
        if ($catalogProcess.HasExited) {
            Stop-Verify 'start catalog' (Join-Lines @(
                "The Catalog subgraph exited with code $($catalogProcess.ExitCode) during start-up."
                ''
                (Get-LogTail $catalogStdout)
                (Get-LogTail $catalogStderr)))
        }
        try {
            $health = Invoke-WebRequest -Uri "$CatalogSubgraphUrl/health" -TimeoutSec 5 -SkipHttpErrorCheck
            if ($health.StatusCode -eq 200) {
                $catalogHealthy = $true
                break
            }
        } catch {
            # Not listening yet.
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $catalogHealthy) {
        Stop-Verify 'start catalog' (Join-Lines @(
            "$CatalogSubgraphUrl/health did not answer within $StartupTimeoutSeconds seconds."
            ''
            (Get-LogTail $catalogStdout)
            (Get-LogTail $catalogStderr)))
    }
    Write-Ok "catalog answering on $CatalogSubgraphUrl/health"

    # -- 5c. what each subgraph publishes ----------------------------------

    # Not `schema export` but the field a composer actually reads. The two
    # happen to agree in HotChocolate 16.6.0; asserting the one the router
    # ecosystem depends on is the assertion worth having.
    foreach ($name in $Subgraphs.Keys) {
        $subgraph = $Subgraphs[$name]
        $serviceResponse = Invoke-WebRequest -Uri "$($subgraph.Url)/graphql" `
            -Method Post -ContentType 'application/json' `
            -Headers @{ Accept = 'application/json' } `
            -Body '{"query":"{ _service { sdl } }"}' -TimeoutSec 30 -SkipHttpErrorCheck

        if ($serviceResponse.StatusCode -ne 200) {
            Stop-Verify "$name _service" (Join-Lines @(
                "_service on the $name subgraph answered $($serviceResponse.StatusCode)."
                'A subgraph that cannot answer _service is not a subgraph.'
                $serviceResponse.Content))
        }

        $publishedSdl = ($serviceResponse.Content | ConvertFrom-Json).data._service.sdl
        $publishedPath = Join-Path $tempDir "$name.published.graphql"
        [System.IO.File]::WriteAllText($publishedPath, $publishedSdl)

        if (-not (Test-SameText $subgraph.Committed $publishedPath)) {
            Stop-Verify "$name published schema" (Join-Lines @(
                "What the $name subgraph publishes through _service is not what is"
                "committed. That file is the composer's input."
                ''
                (Get-SchemaDiff -ExpectedPath $subgraph.Committed -ActualPath $publishedPath `
                    -WorkDir $tempDir -Label "published-$name")))
        }

        if ($publishedSdl -notmatch '@key\(fields: "id"\)') {
            Stop-Verify "$name published schema" (Join-Lines @(
                "The $name subgraph publishes no @key(fields: `"id`")."
                'A schema printed without its key directives composes into a graph with'
                'no entities in it, which is the failure chapter 7 warned about.'))
        }
    }
    Write-Ok 'both subgraphs publish the committed schemas through _service'

    # -- 5d. the catalog half ----------------------------------------------

    # -TimeoutSec is spelled that way for PowerShell 7.0; on 7.5 and later it is
    # an alias for -ConnectionTimeoutSeconds. Either way the service has already
    # answered /health by this point, so it is only a backstop.
    function Invoke-Gql {
        param([string] $Url, [string] $Query, $Variables = $null, [string] $Step)

        $payload = @{ query = $Query }
        if ($Variables) { $payload.variables = $Variables }

        $response = Invoke-WebRequest `
            -Uri "$Url/graphql" -Method Post -ContentType 'application/json' `
            -Headers @{ Accept = 'application/json' } `
            -Body ($payload | ConvertTo-Json -Depth 12 -Compress) `
            -TimeoutSec 120 -SkipHttpErrorCheck

        if ($response.StatusCode -ne 200) {
            Stop-Verify $Step "POST $Url/graphql answered $($response.StatusCode).`n$($response.Content)"
        }

        $parsed = $response.Content | ConvertFrom-Json
        if ($parsed.PSObject.Properties.Name -contains 'errors') {
            Stop-Verify $Step (Join-Lines @(
                'The response carries an errors key. This query is supposed to succeed outright.'
                ($parsed.errors | ConvertTo-Json -Depth 10)))
        }

        return $parsed
    }

    $catalogPayload = Invoke-Gql -Url $CatalogSubgraphUrl -Query $CatalogQuery -Step 'catalog products'
    $products = @($catalogPayload.data.products)

    if ($products.Count -ne $ExpectedProductCount) {
        Stop-Verify 'product count' "Expected $ExpectedProductCount products from Catalog, got $($products.Count)."
    }
    Write-Ok "catalog answered $ExpectedProductCount products"

    # Every key exactly as Catalog gave it. Re-encoding one here would test this
    # script's idea of the format rather than the two services' agreement about
    # it, which is the only thing that matters.
    $representations = @($products | ForEach-Object { @{ __typename = 'Product'; id = $_.id } })

    # -- 5e. the mosaic half, through _entities ----------------------------

    $entitiesPayload = Invoke-Gql -Url $BaseUrl -Query $MosaicEntitiesQuery `
        -Variables @{ representations = $representations } -Step 'mosaic _entities'

    $entities = @($entitiesPayload.data._entities)
    if ($entities.Count -ne $ExpectedProductCount) {
        Stop-Verify 'entity count' (Join-Lines @(
            "Sent $ExpectedProductCount representations and got $($entities.Count) entities back."
            'The specification is positional: answer n belongs to representation n,'
            'so a subgraph must never reorder or deduplicate the list it was handed.'))
    }

    $reviewCount = 0
    foreach ($entity in $entities) {
        $reviewCount += @($entity.reviews.nodes).Count
    }

    if ($reviewCount -ne $ExpectedReviewCount) {
        Stop-Verify 'review count' (Join-Lines @(
            "Expected $ExpectedReviewCount reviews across all representations, got $reviewCount."
            'This is the number chapters 2 to 5 measured through Query.products. The'
            'field it arrives through changed in chapter 8; the answer did not.'))
    }
    Write-Ok "mosaic answered $ExpectedProductCount representations with $ExpectedReviewCount reviews"

    # A key that is not one of ours: a null entity and no errors key. The raw
    # Guid is the interesting case, because that is what this identifier looked
    # like before chapter 5 made it a global object identifier.
    foreach ($badKey in @('not-a-key', 'a0000000-0000-4000-8000-000000000001')) {
        $badResponse = Invoke-WebRequest `
            -Uri "$CatalogSubgraphUrl/graphql" -Method Post -ContentType 'application/json' `
            -Headers @{ Accept = 'application/json' } `
            -Body (@{
                query = $CatalogEntitiesQuery
                variables = @{ representations = @(@{ __typename = 'Product'; id = $badKey }) }
            } | ConvertTo-Json -Depth 12 -Compress) `
            -TimeoutSec 60 -SkipHttpErrorCheck

        $badPayload = $badResponse.Content | ConvertFrom-Json
        if ($badPayload.PSObject.Properties.Name -contains 'errors') {
            Stop-Verify 'undecodable key' (Join-Lines @(
                "A representation carrying the key '$badKey' produced an errors array."
                'It is supposed to produce a null entity. The specification makes'
                '[_Entity] nullable for exactly this, and a subgraph that throws instead'
                'turns one bad key into a failed batch.'
                $badResponse.Content))
        }
        if (@($badPayload.data._entities).Count -ne 1 -or $null -ne $badPayload.data._entities[0]) {
            Stop-Verify 'undecodable key' (Join-Lines @(
                "A representation carrying the key '$badKey' did not produce exactly one null."
                $badResponse.Content))
        }
    }
    Write-Ok 'an undecodable key produces a null entity and no error'

    # -- 5f. one batch, one statement --------------------------------------

    # Catalog has no request timeline of its own, so this is asserted from the
    # answer rather than from a counter: 25 representations in, 25 titles out,
    # in order. The statement count behind it is measured in the chapter's
    # research file, not here.
    $catalogEntities = Invoke-Gql -Url $CatalogSubgraphUrl -Query $CatalogEntitiesQuery `
        -Variables @{ representations = $representations } -Step 'catalog _entities'

    $resolved = @($catalogEntities.data._entities)
    if ($resolved.Count -ne $ExpectedProductCount) {
        Stop-Verify 'catalog _entities' "Sent $ExpectedProductCount representations, got $($resolved.Count) back."
    }
    for ($i = 0; $i -lt $products.Count; $i++) {
        if ($resolved[$i].title -ne $products[$i].title) {
            Stop-Verify 'catalog _entities' (Join-Lines @(
                "Entity $i is '$($resolved[$i].title)' but representation $i named '$($products[$i].title)'."
                'The answer is positional and nothing in the response says which'
                'representation an entity belongs to, so an out-of-order reply is a'
                'silently wrong one.'))
        }
    }
    Write-Ok 'catalog resolved every representation, in order'

    # -- 5g. the other direction -------------------------------------------

    # An order line hands out a product key Mosaic cannot resolve itself. The
    # total is the regression test for the Include that was missing from
    # chapter 4 until chapter 8: Order.total throws when the lines are not
    # loaded, and nothing in the collection had ever asked for one.
    # Mosaic has no root field that lists customers, so the keys come out of
    # the answer above: every review carries its author. That is worth noticing
    # rather than working around. Since chapter 8 every entry into this service
    # starts either at one of its two root fields or at a key somebody else is
    # holding.
    #
    # Seven of the twelve seeded customers have no orders at all, so this walks
    # the authors until it finds one who does rather than assuming.
    $customerKeys = @($entities.reviews.nodes.author.id | Select-Object -Unique)
    if ($customerKeys.Count -lt 1) {
        Stop-Verify 'orders' 'No review carried an author, so there is no customer key to follow.'
    }

    $orders = @()
    $customerKey = $null
    foreach ($candidate in $customerKeys) {
        $ordersPayload = Invoke-Gql -Url $BaseUrl `
            -Query ("{ ordersByCustomer(customerId: `"$candidate`") " +
                    '{ total { amount } lines { quantity product { id } } } }') `
            -Step 'orders'
        $orders = @($ordersPayload.data.ordersByCustomer)
        if ($orders.Count -gt 0) {
            $customerKey = $candidate
            break
        }
    }

    if (-not $customerKey) {
        Stop-Verify 'orders' (Join-Lines @(
            "None of the $($customerKeys.Count) customers who wrote a review has an order."
            'The seed data gives eight orders to seven of the twelve customers, so'
            'this means the orders are not being read rather than that the data is thin.'))
    }

    $lineCount = ($orders | ForEach-Object { @($_.lines).Count } | Measure-Object -Sum).Sum
    if ($lineCount -lt 1) {
        Stop-Verify 'order lines' (Join-Lines @(
            'Every order came back with an empty lines array.'
            'OrderLine is a related entity with a shadow key, not an owned type, so'
            'OrderingService has to Include it. It did not, from chapter 4 until'
            'chapter 8, and no request in the collection had ever asked.'))
    }

    $lineProductKey = ($orders | ForEach-Object { $_.lines } | Select-Object -First 1).product.id
    if ($representations.id -notcontains $lineProductKey -and $products.id -notcontains $lineProductKey) {
        Stop-Verify 'order lines' (Join-Lines @(
            "An order line answered product key '$lineProductKey', which is not one of"
            'the keys Catalog handed out. The two services encode the same identifier'
            'the same way or they do not share an entity at all.'))
    }
    Write-Ok 'an order line answers a product key Catalog also answers'

    # The _entities query, four more times, because one sample is not enough to
    # assert a batching number against.
    #
    # A DataLoader batch is dispatched when the coordinator has seen it
    # untouched for the settle time across two evaluation rounds. Almost always
    # the 120 author resolvers all enqueue their keys inside that window and the
    # batch goes once. Occasionally - measured at two requests in four hundred
    # on this machine - they do not, the batch is dispatched with what it has,
    # and the stragglers form a second one. The answers are identical; the
    # statement count is one higher.
    #
    # So the assertions below are: at least one of the five runs hit the
    # batched number exactly, and none of them exceeded it by more than a
    # single split batch. A service whose DataLoaders had been removed could
    # satisfy neither.
    for ($i = 1; $i -lt $VerifyQueryRuns; $i++) {
        Invoke-Gql -Url $BaseUrl -Query $MosaicEntitiesQuery `
            -Variables @{ representations = $representations } -Step 'mosaic _entities' | Out-Null
    }

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
            'That number is quoted in the book. It was 146 through chapters 2 and 3'
            'and at tag ch04-ef, 3 once chapter 4 added DataLoaders, and 2 since'
            'chapter 8 took the product lookup out of this service altogether. If it'
            'moved again, either a DataLoader stopped batching or a resolver went'
            'back to asking a service directly.'))
    }

    $lookupCeiling = $ExpectedLookupCount + $SplitBatchAllowance
    $tooManyLookups = @($loggedCounts | Where-Object { [int] $_ -gt $lookupCeiling })
    if ($tooManyLookups.Count -gt 0) {
        Stop-Verify 'lookup count' (Join-Lines @(
            "One of the runs asked for more than $lookupCeiling lookups: $($loggedCounts -join ', ')."
            ''
            'A single split batch costs one extra lookup and is expected now and'
            'again. More than that is a resolver that is not going through a'
            'DataLoader at all.'))
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
            [regex]::Matches((Get-LogText $apiStdout), '(\d+) resolvers,') |
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

    # -- 6d. the database round trips --------------------------------------

    $loggedSql = @(
        [regex]::Matches((Get-LogText $apiStdout), '(\d+) SQL\)') |
            ForEach-Object { $_.Groups[1].Value })

    if ($loggedSql.Count -eq 0) {
        Stop-Verify 'sql command count' (Join-Lines @(
            'The timeline never reported a SQL command count.'
            'SqlCommandCounter is the EF Core interceptor that produces it, and it'
            'is attached to the pooled context factory in AddMosaicDatabase.'
            ''
            (Get-LogTail $apiStdout)))
    }

    if ($loggedSql -notcontains "$ExpectedSqlCommandCount") {
        Stop-Verify 'sql command count' (Join-Lines @(
            "Expected the timeline to report $ExpectedSqlCommandCount SQL commands for the query."
            "It reported: $($loggedSql -join ', ')."
            ''
            'This is the number chapter 4 is about and chapter 8 moved by one. If it'
            'went up, something started querying per row. If it went down, something'
            'started batching, and the chapter that claims otherwise needs rewriting.'))
    }

    $sqlCeiling = $ExpectedSqlCommandCount + $SplitBatchAllowance
    $tooManyCommands = @($loggedSql | Where-Object { [int] $_ -gt $sqlCeiling })
    if ($tooManyCommands.Count -gt 0) {
        Stop-Verify 'sql command count' (Join-Lines @(
            "One of the runs sent more than $sqlCeiling statements: $($loggedSql -join ', ')."
            ''
            'A single split batch costs one extra statement and is expected now and'
            'again. More than that is an N+1 growing back.'))
    }
    Write-Ok "request timeline reported $ExpectedSqlCommandCount SQL commands"

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
        # Both URLs are overridden rather than trusted: the environment file
        # says 5100 and 5101, and this script can be pointed elsewhere.
        $newmanArgs = $newmanPrefix + @(
            'run', $PostmanCollection,
            '--environment', $PostmanEnvironment,
            '--env-var', "mosaicUrl=$BaseUrl",
            '--env-var', "catalogUrl=$CatalogSubgraphUrl",
            '--bail')
        & $newmanCommand @newmanArgs
        if ($LASTEXITCODE -ne 0) {
            Stop-Verify 'postman' "newman exited with $LASTEXITCODE; its output above says which request failed."
        }
        Write-Ok 'postman collection'
    }

    # -- 8. the two subgraphs still compose ---------------------------------

    # wgc is a local dev dependency, pinned in package.json beside newman. It
    # composes from committed schema files and talks to nothing.
    $wgcCommand = $null
    $wgcPrefix = @()
    $localWgc = Join-Path $RepoRoot 'node_modules' '.bin' ($IsWindows ? 'wgc.cmd' : 'wgc')
    if (Test-Path -LiteralPath $localWgc) {
        $wgcCommand = $localWgc
    } elseif (Get-Command npx -ErrorAction SilentlyContinue) {
        & npx --no wgc --help *> $null
        if ($LASTEXITCODE -eq 0) {
            $wgcCommand = 'npx'
            $wgcPrefix = @('--no', 'wgc')
        }
    }

    # Chapter 8 shows no composition at all: everything it does is done against
    # one subgraph at a time, by hand, and chapter 9 is where composition
    # becomes the subject. The check was here from chapter 8 anyway, because
    # three separate things about these two schemas would break the graph only
    # when it is assembled - two subgraphs both declaring Query.node, the cost
    # directives HotChocolate stamps by default, and PageCursor being the one
    # paging type nothing marks shareable - and none of them is visible from
    # either service on its own.
    #
    # Chapter 9 adds two things to it. The composed config is compared against
    # the committed one, because the chapter prints what is inside it. And the
    # composition cases run, because the chapter prints the composer's errors
    # too, and an error message is as easy to go stale as a schema.
    if (-not (Test-Path -LiteralPath $FederationGraph)) {
        Write-Skipped 'composition' 'federation/mosaic.yaml does not exist yet'
    } elseif (-not $wgcCommand) {
        Stop-Verify 'composition' (Join-Lines @(
            'wgc is not installed. It is a dev dependency: run npm install.'))
    } else {
        $supergraph = Join-Path $tempDir 'supergraph.json'
        & $wgcCommand @($wgcPrefix + @('router', 'compose', '-i', $FederationGraph, '-o', $supergraph))
        if ($LASTEXITCODE -ne 0) {
            Stop-Verify 'composition' (Join-Lines @(
                "wgc router compose exited with $LASTEXITCODE."
                'The two subgraph schemas under schema/ no longer compose into one'
                'graph. That is a real finding rather than a tooling problem, and the'
                'table above says which coordinate the composer objected to.'))
        }
        if (-not (Test-Path -LiteralPath $supergraph)) {
            Stop-Verify 'composition' "wgc reported success but wrote nothing to $supergraph."
        }
        Write-Ok 'catalog and mosaic compose into one supergraph'

        # -- 8a. the composed config is the one chapter 9 takes apart --------

        if (-not (Test-Path -LiteralPath $FederationSupergraph)) {
            Write-Skipped 'supergraph drift' 'federation/supergraph.json does not exist yet'
        } elseif (-not (Test-SameText $FederationSupergraph $supergraph)) {
            Stop-Verify 'supergraph drift' (Join-Lines @(
                'A fresh compose does not match federation/supergraph.json.'
                ''
                'Chapter 9 prints the contents of that file: the datasource'
                'configurations, the string storage, the compatibility version and the'
                'client schema with no join directives in it. A change here is a change'
                'to the chapter.'
                ''
                'If it is deliberate, recompose and commit the result:'
                ''
                '    npx wgc router compose -i federation/mosaic.yaml -o federation/supergraph.json'
                ''
                'If it is not, wgc changed the config format. That is a finding, and the'
                'version is pinned in package.json precisely so it cannot happen quietly.'))
        } else {
            Write-Ok 'the composed config matches federation/supergraph.json'
        }

        # -- 8b. the errors chapter 9 prints ---------------------------------

        # Every case is Mosaic's own pair with one edit applied, so these assert
        # the messages the chapter quotes rather than messages from a fixture
        # invented to produce them.
        if (-not (Test-Path -LiteralPath $CompositionCases)) {
            Write-Skipped 'composition cases' 'scripts/composition-cases.mjs does not exist yet'
        } elseif (-not (Get-Command node -ErrorAction SilentlyContinue)) {
            Stop-Verify 'composition cases' 'node is not on PATH; it is needed to run scripts/composition-cases.mjs.'
        } else {
            & node $CompositionCases
            if ($LASTEXITCODE -ne 0) {
                Stop-Verify 'composition cases' (Join-Lines @(
                    "scripts/composition-cases.mjs exited with $LASTEXITCODE."
                    'One of the composition errors chapter 9 prints is no longer the error'
                    'the composer produces. The output above says which case and how it'
                    'differs. Fix the chapter, not the assertion.'))
            }
            Write-Ok 'the composition errors chapter 9 prints are the ones wgc produces'
        }

        # -- 8c. chapter 10: a router in front of the two ---------------------

        # The first section that asks the graph a question rather than asking a
        # service one. It runs here, after the composed config has been checked
        # against a fresh compose, because that file is what the router mounts:
        # verifying the graph the chapter describes means verifying the file the
        # chapter composed.
        #
        # It comes before the federated-wire section on purpose. Both routers
        # publish 3002 and the two are never meant to be up together, so this
        # one is taken down again at the end of the block.
        if ($SkipRouter) {
            Write-Skipped 'router' '-SkipRouter was passed'
        } elseif (-not (Test-Path -LiteralPath $RouterConfig)) {
            Write-Skipped 'router' 'router/config.yaml does not exist yet'
        } else {
            if (Test-PortInUse $RouterPort) {
                Stop-Verify 'start router' (Join-Lines @(
                    "Something is already listening on port $RouterPort."
                    'An earlier run may have left one behind: `docker compose down mosaic-router`'
                    'clears this one, and `docker compose --profile wire down` clears'
                    "chapter 7's, which publishes the same port."))
            }

            & docker compose --project-directory $RepoRoot up --detach mosaic-router
            if ($LASTEXITCODE -ne 0) {
                Stop-Verify 'start router' (Join-Lines @(
                    "docker compose up mosaic-router exited with $LASTEXITCODE."
                    'The first run of this pulls the router image.'))
            }
            $startedMosaicRouter = $true

            $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
            $up = $false
            while ((Get-Date) -lt $deadline) {
                try {
                    if ((Invoke-WebRequest -Uri "$MosaicRouterUrl/health" -TimeoutSec 5 -SkipHttpErrorCheck).StatusCode -eq 200) {
                        $up = $true
                        break
                    }
                } catch {
                    # Not listening yet.
                }
                Start-Sleep -Milliseconds 500
            }
            if (-not $up) {
                Stop-Verify 'start router' (Join-Lines @(
                    "$MosaicRouterUrl/health did not answer within $StartupTimeoutSeconds seconds."
                    ''
                    ((& docker compose --project-directory $RepoRoot logs --tail 40 mosaic-router) | Out-String)))
            }
            Write-Ok "the router answers on $MosaicRouterUrl/health"

            # The storefront query, the plan behind it, and what the router
            # does not expose. Chapter 10 prints all three.
            if (-not (Test-Path -LiteralPath $RouterPostman) -or -not (Test-Path -LiteralPath $RouterPostmanEnv)) {
                Write-Skipped 'router postman' 'the router collection or its environment is missing from postman/'
            } elseif (-not $newmanCommand) {
                Write-Skipped 'router postman' 'newman is not installed - run npm install first'
            } else {
                & $newmanCommand @($newmanPrefix + @(
                    'run', $RouterPostman,
                    '--environment', $RouterPostmanEnv,
                    '--env-var', "routerUrl=$MosaicRouterUrl",
                    '--bail'))
                if ($LASTEXITCODE -ne 0) {
                    Stop-Verify 'router postman' (Join-Lines @(
                        "newman exited with $LASTEXITCODE; its output above says which request failed."
                        'The storefront query answering out of two services is chapter 10''s'
                        'headline, and the query plan assertions are the listing it prints.'))
                }
                Write-Ok 'the router answers the query neither subgraph can'
            }

            # And the three things chapter 10 says are surprising, each one a
            # router started on purpose against a config made for the case.
            # Same arrangement as chapter 9's composition cases and for the same
            # reason: one implementation, called by both verify scripts.
            if (-not (Test-Path -LiteralPath $RouterCases)) {
                Write-Skipped 'router cases' 'scripts/router-cases.mjs does not exist yet'
            } elseif (-not (Get-Command node -ErrorAction SilentlyContinue)) {
                Stop-Verify 'router cases' 'node is not on PATH; it is needed to run scripts/router-cases.mjs.'
            } else {
                & node $RouterCases
                if ($LASTEXITCODE -ne 0) {
                    Stop-Verify 'router cases' (Join-Lines @(
                        "scripts/router-cases.mjs exited with $LASTEXITCODE."
                        'One of the three router behaviours chapter 10 describes has changed.'
                        'The output above says which. Fix the chapter, not the assertion.'))
                }
                Write-Ok 'the router behaves the three ways chapter 10 says it does'
            }

            # Down rather than stop, and now rather than in the finally block,
            # because the federated-wire section below wants this port.
            & docker compose --project-directory $RepoRoot down mosaic-router *> $null
            $startedMosaicRouter = $false
        }
    }

    # -- 9. chapter 7's federated wire --------------------------------------

    # Two subgraphs on the host and the Cosmo Router in a container. This is
    # the one section that needs a registry pull, and the one that can be
    # skipped, because everything above it is about Mosaic and none of this is.
    if ($SkipWire) {
        Write-Skipped 'federated wire' '-SkipWire was passed'
    } elseif (-not (Test-Path -LiteralPath $WireDir)) {
        Write-Skipped 'federated wire' 'samples/federated-wire does not exist yet'
    } elseif (-not $newmanCommand) {
        Write-Skipped 'federated wire' 'newman is not installed - run npm install first'
    } else {
        # -- 9a. compose the supergraph -------------------------------------

        # wgc was found in step 8, which needs it for Mosaic's own two
        # subgraphs. This section composes the chapter 7 sample instead.
        if (-not $wgcCommand) {
            Stop-Verify 'federated wire' (Join-Lines @(
                'wgc is not installed, and the router cannot start without a composed'
                'execution config. It is a dev dependency: run npm install.'))
        }

        & $wgcCommand @($wgcPrefix + @('router', 'compose', '-i', $WireGraph, '-o', $WireSupergraph))
        if ($LASTEXITCODE -ne 0) {
            Stop-Verify 'wire composition' (Join-Lines @(
                "wgc router compose exited with $LASTEXITCODE."
                'Composition failing is a real finding, not a tooling problem: the two'
                'subgraph schemas under schema/samples no longer compose into one graph.'))
        }
        if (-not (Test-Path -LiteralPath $WireSupergraph)) {
            Stop-Verify 'wire composition' "wgc reported success but wrote nothing to $WireSupergraph."
        }
        Write-Ok 'wgc composed the two subgraphs into one supergraph'

        # -- 9b. start both subgraphs ---------------------------------------

        foreach ($name in $WireSubgraphs.Keys) {
            $subgraph = $WireSubgraphs[$name]
            if (Test-PortInUse $subgraph.Port) {
                Stop-Verify "start $name subgraph" (Join-Lines @(
                    "Something is already listening on port $($subgraph.Port)."
                    'Stop it first: an earlier run of this script, or a debugger.'))
            }

            $env:ASPNETCORE_URLS = $subgraph.Url
            $stdout = Join-Path $tempDir "wire-$name.out.log"
            $stderr = Join-Path $tempDir "wire-$name.err.log"
            $subgraph.Stdout = $stdout

            $wireProcesses[$name] = Start-Process `
                -FilePath 'dotnet' `
                -ArgumentList @('run', '--project', $subgraph.Project, '-c', 'Release', '--no-build', '--no-launch-profile') `
                -WorkingDirectory $RepoRoot `
                -RedirectStandardOutput $stdout `
                -RedirectStandardError $stderr `
                -NoNewWindow `
                -PassThru
        }

        foreach ($name in $WireSubgraphs.Keys) {
            $subgraph = $WireSubgraphs[$name]
            $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
            $up = $false
            while ((Get-Date) -lt $deadline) {
                if ($wireProcesses[$name].HasExited) {
                    Stop-Verify "start $name subgraph" (Join-Lines @(
                        "The $name subgraph exited with code $($wireProcesses[$name].ExitCode) during start-up."
                        ''
                        (Get-LogTail $subgraph.Stdout)))
                }
                try {
                    $probe = Invoke-WebRequest -Uri "$($subgraph.Url)/graphql" `
                        -Method Post -ContentType 'application/json' `
                        -Headers @{ Accept = 'application/json' } `
                        -Body '{"query":"{ __typename }"}' -TimeoutSec 5 -SkipHttpErrorCheck
                    if ($probe.StatusCode -eq 200) {
                        $up = $true
                        break
                    }
                } catch {
                    # Not listening yet.
                }
                Start-Sleep -Milliseconds 500
            }
            if (-not $up) {
                Stop-Verify "start $name subgraph" (Join-Lines @(
                    "$($subgraph.Url)/graphql did not answer within $StartupTimeoutSeconds seconds."
                    ''
                    (Get-LogTail $subgraph.Stdout)))
            }
        }
        Write-Ok "both subgraphs answering on $CatalogPort and $ReviewsPort"

        # -- 9c. the published schemas --------------------------------------

        # Not `schema export` but the field a composer actually reads. The two
        # happen to agree in HotChocolate 16.6.0; asserting the one the router
        # ecosystem depends on is the assertion worth having.
        foreach ($name in $WireSubgraphs.Keys) {
            $subgraph = $WireSubgraphs[$name]
            $serviceResponse = Invoke-WebRequest -Uri "$($subgraph.Url)/graphql" `
                -Method Post -ContentType 'application/json' `
                -Headers @{ Accept = 'application/json' } `
                -Body '{"query":"{ _service { sdl } }"}' -TimeoutSec 30 -SkipHttpErrorCheck

            if ($serviceResponse.StatusCode -ne 200) {
                Stop-Verify "$name _service" (Join-Lines @(
                    "_service on the $name subgraph answered $($serviceResponse.StatusCode)."
                    $serviceResponse.Content))
            }

            $publishedSdl = ($serviceResponse.Content | ConvertFrom-Json).data._service.sdl
            $publishedPath = Join-Path $tempDir "wire-$name.published.graphql"
            [System.IO.File]::WriteAllText($publishedPath, $publishedSdl)

            if (-not (Test-SameText $subgraph.Schema $publishedPath)) {
                Stop-Verify "$name subgraph schema" (Join-Lines @(
                    "What the $name subgraph publishes is not what is committed in"
                    "$($subgraph.Schema)."
                    ''
                    'That file is the composer''s input. If the change is deliberate,'
                    'regenerate it from _service and recompose; if it is not, something'
                    'moved the federated contract without saying so.'
                    ''
                    (Get-SchemaDiff -ExpectedPath $subgraph.Schema -ActualPath $publishedPath `
                        -WorkDir $tempDir -Label "wire-$name")))
            }
        }
        Write-Ok 'both subgraphs publish the committed schemas through _service'

        # -- 9d. the router --------------------------------------------------

        if (Test-PortInUse $RouterPort) {
            Stop-Verify 'start router' (Join-Lines @(
                "Something is already listening on port $RouterPort."
                'Stop it first: `docker compose --profile wire down` clears an earlier run.'))
        }

        & docker compose --project-directory $RepoRoot --profile wire up --detach wire-router
        if ($LASTEXITCODE -ne 0) {
            Stop-Verify 'start router' (Join-Lines @(
                "docker compose up wire-router exited with $LASTEXITCODE."
                'The first run of this pulls the router image; a failure here is'
                'usually the registry rather than the graph.'))
        }
        $startedRouter = $true

        $routerDeadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
        $routerUp = $false
        while ((Get-Date) -lt $routerDeadline) {
            try {
                $health = Invoke-WebRequest -Uri "$RouterUrl/health" -TimeoutSec 5 -SkipHttpErrorCheck
                if ($health.StatusCode -eq 200) {
                    $routerUp = $true
                    break
                }
            } catch {
                # Not listening yet.
            }
            Start-Sleep -Milliseconds 500
        }
        if (-not $routerUp) {
            Stop-Verify 'start router' (Join-Lines @(
                "$RouterUrl/health did not answer within $StartupTimeoutSeconds seconds."
                ''
                ((& docker compose --project-directory $RepoRoot logs --tail 40 wire-router) | Out-String)))
        }
        Write-Ok "router answering on $RouterUrl/health"

        # -- 9e. the collection ----------------------------------------------

        if (-not (Test-Path -LiteralPath $WirePostman) -or -not (Test-Path -LiteralPath $WirePostmanEnv)) {
            Stop-Verify 'wire postman' 'the federated-wire collection or its environment is missing from postman/'
        }

        $wireArgs = $newmanPrefix + @(
            'run', $WirePostman,
            '--environment', $WirePostmanEnv,
            '--env-var', "catalogUrl=$CatalogUrl",
            '--env-var', "reviewsUrl=$ReviewsUrl",
            '--env-var', "routerUrl=$RouterUrl",
            '--bail')
        & $newmanCommand @wireArgs
        if ($LASTEXITCODE -ne 0) {
            Stop-Verify 'wire postman' "newman exited with $LASTEXITCODE; its output above says which request failed."
        }
        Write-Ok 'federated-wire postman collection'

        # -- 9f. what actually went over the wire -----------------------------

        # The collection asserts the query plan the router reports. This asserts
        # the requests the subgraphs received, which is the same claim checked
        # from the other end, and it is the pair of listings chapter 7 prints.
        # .Contains, not -like: the reviews body below is a JSON array, and -like
        # would read its square brackets as a wildcard character class and match
        # nothing. The catalog body has no brackets and would have passed either
        # way, which is exactly how that bug survives being written.
        $catalogLog = Get-LogText $WireSubgraphs['catalog'].Stdout
        if (-not $catalogLog.Contains($ExpectedCatalogFetch)) {
            Stop-Verify 'wire traffic' (Join-Lines @(
                'The catalog subgraph never received the request chapter 7 prints:'
                ''
                "    $ExpectedCatalogFetch"
                ''
                'The router adds __typename and id to that selection because it needs'
                'a key for the second fetch. If the body changed, the chapter is wrong'
                'rather than the router.'))
        }

        $reviewsLog = Get-LogText $WireSubgraphs['reviews'].Stdout
        if (-not $reviewsLog.Contains($ExpectedReviewsFetch)) {
            Stop-Verify 'wire traffic' (Join-Lines @(
                'The reviews subgraph never received the entity fetch chapter 7 prints:'
                ''
                "    $ExpectedReviewsFetch"
                ''
                'Three representations in one call is the claim. Three separate calls'
                'would still answer correctly and would still fail this check.'))
        }
        Write-Ok 'the router sent the two requests chapter 7 prints'
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

    if ($catalogProcess -and -not $catalogProcess.HasExited) {
        try {
            $catalogProcess.Kill($true)
            [void] $catalogProcess.WaitForExit(15000)
        } catch {
            Write-Host "Could not stop the Catalog subgraph (pid $($catalogProcess.Id)): $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    foreach ($pair in @(@{ Process = $apiProcess; Port = $Port }, @{ Process = $catalogProcess; Port = $CatalogSubgraphPort })) {
        if (-not $pair.Process) {
            continue
        }
        $portDeadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $portDeadline -and (Test-PortInUse $pair.Port)) {
            Start-Sleep -Milliseconds 250
        }
        if (Test-PortInUse $pair.Port) {
            Write-Host "Warning: something is still listening on port $($pair.Port) after the service was stopped." -ForegroundColor Yellow
        }
    }

    foreach ($name in @($wireProcesses.Keys)) {
        $process = $wireProcesses[$name]
        if ($process -and -not $process.HasExited) {
            try {
                $process.Kill($true)
                [void] $process.WaitForExit(15000)
            } catch {
                Write-Host "Could not stop the $name subgraph (pid $($process.Id)): $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    }

    if ($startedRouter) {
        # down rather than stop: the container holds a bind mount on a file this
        # script recomposes on every run, and a stopped container keeps it.
        & docker compose --project-directory $RepoRoot --profile wire down wire-router *> $null
    }

    # Normally already down: the router section takes its own container away so
    # that the wire section can have the port. This catches the run that failed
    # somewhere in between.
    if ($startedMosaicRouter) {
        & docker compose --project-directory $RepoRoot down mosaic-router *> $null
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

    if ($resetDatabaseWasSet) {
        $env:MOSAIC_RESET_DATABASE = $previousResetDatabase
    } else {
        Remove-Item Env:MOSAIC_RESET_DATABASE -ErrorAction SilentlyContinue
    }

    if ($tempDir -and (Test-Path -LiteralPath $tempDir)) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # The container is stopped, not removed, and its volume is left in place.
    # Not that the data in it survived: since chapter 5 this script starts the
    # service with MOSAIC_RESET_DATABASE set, so the run began by dropping the
    # schema. Keeping the volume only saves the container from initialising
    # itself again. Do not point this script at a database holding anything you
    # want to keep.
    if ($startedDatabase -and -not $KeepDatabase) {
        & docker compose --project-directory $RepoRoot stop mosaic-db *> $null
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
