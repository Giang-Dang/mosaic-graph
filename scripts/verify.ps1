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
    # The first of Mosaic's seven subgraph ports. They are consecutive from here,
    # in the order federation/mosaic.yaml lists them - catalog, pricing,
    # inventory, accounts, reviews, ordering - and they match docker-compose.yml
    # and the six http launch profiles.
    #
    # It was two ports until chapter 12, and 5100 was the monolith's. Nothing
    # listens on 5100 any more.
    [int] $FirstSubgraphPort = 5101,

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
$SamplesDir         = Join-Path $RepoRoot 'samples' 'three-approaches'
$SampleSchemaDir    = Join-Path $RepoRoot 'schema' 'samples'
# Retired at chapter 12 and replaced, for the reason decision 54 gives about
# the one before it: most of mosaic-federation's requests asked port 5100 for
# something, and there is no port 5100.
$PostmanCollection  = Join-Path $RepoRoot 'postman' 'mosaic-subgraphs.postman_collection.json'
$PostmanEnvironment = Join-Path $RepoRoot 'postman' 'mosaic-subgraphs.local.postman_environment.json'

# -- the seven subgraphs -----------------------------------------------------

# One entry per service, in the order federation/mosaic.yaml lists them and in
# the order chapter 12 extracted them. Everything below that used to be written
# twice - once for Mosaic.Api and once for Mosaic.Catalog - is written once and
# iterated, because seven copies of a startup block is six chances to check one
# service less thoroughly than the others.
#
# Nodes is the seventh and joined at chapter 13. It has no database, so it is
# the one entry here for which the reset-and-reseed further down does nothing,
# and it needs no special case anywhere else: it builds, exports a schema,
# answers /health and composes exactly like the other six.
#
# Ports are assigned from $FirstSubgraphPort rather than typed, so that a
# machine with something already on 5101 can move all seven with one switch.
$Subgraphs = [ordered] @{}
$subgraphNames = @('Catalog', 'Pricing', 'Inventory', 'Accounts', 'Reviews', 'Ordering', 'Nodes')
for ($i = 0; $i -lt $subgraphNames.Count; $i++) {
    $name = $subgraphNames[$i]
    $port = $FirstSubgraphPort + $i
    $Subgraphs[$name.ToLowerInvariant()] = @{
        Display   = $name
        Project   = Join-Path $RepoRoot 'src' "Mosaic.$name" "Mosaic.$name.csproj"
        Committed = Join-Path $RepoRoot 'schema' "$($name.ToLowerInvariant()).graphql"
        Port      = $port
        Url       = "http://localhost:$port"
    }
}

# Named separately where a step talks to one service by name. The wire sample
# further down has its own $CatalogUrl and $ReviewsUrl for two subgraphs that
# have nothing to do with these, which is why every one of these carries the
# Mosaic prefix.
$MosaicCatalogUrl   = $Subgraphs['catalog'].Url
$MosaicPricingUrl   = $Subgraphs['pricing'].Url
$MosaicAccountsUrl  = $Subgraphs['accounts'].Url
$MosaicReviewsUrl   = $Subgraphs['reviews'].Url
$MosaicOrderingUrl  = $Subgraphs['ordering'].Url

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

# -- chapter 11's entity resolution ------------------------------------------

# Both of these run inside the router section, because @requires and @provides
# are only visible once something is planning across two services. The cases
# script starts the sample under samples/entity-resolution on ports of its own,
# so nothing here has to know about it.
$EntitiesPostman    = Join-Path $RepoRoot 'postman' 'mosaic-entities.postman_collection.json'
$EntitiesPostmanEnv = Join-Path $RepoRoot 'postman' 'mosaic-entities.local.postman_environment.json'
$EntityCases        = Join-Path $RepoRoot 'scripts' 'entity-cases.mjs'

# -- chapter 12's @override --------------------------------------------------

# Nine cases: what the composer does with @override in every shape chapter 12
# describes, including the two it rejects, the one it warns about, and the
# federation 2.7 label wgc does not implement. Same arrangement as chapter 9's
# and chapter 10's case scripts - one implementation, called by both verify
# scripts - and the same edit discipline.
$OverrideCases = Join-Path $RepoRoot 'scripts' 'override-cases.mjs'

# -- chapter 13's modelling problems -----------------------------------------

# Sixteen cases across four families: what the composer does with an enum
# declared twice, with a value type declared twice, with a scalar that two
# subgraphs mean different things by, and with Query.node in more than one
# place. Most of them compose, which is why an exit code is not enough and the
# script reads the composed client schema and the routing table instead.
#
# The @interfaceObject cases run against samples/interface-object rather than
# against Mosaic, because Mosaic has no interface whose implementations live in
# two services and inventing one inside a storefront would be worse than a
# sample. Those two subgraph schemas are checked for drift below like any
# other; the sample's own router is not started, because what chapter 13 reads
# out of it beyond composition is a query plan, and decision 62 keeps that out
# of a gate.
$ModelingCases      = Join-Path $RepoRoot 'scripts' 'modeling-cases.mjs'
$InterfaceObjectDir = Join-Path $RepoRoot 'samples' 'interface-object'
$NodesPostman       = Join-Path $RepoRoot 'postman' 'mosaic-nodes.postman_collection.json'
$NodesPostmanEnv    = Join-Path $RepoRoot 'postman' 'mosaic-nodes.local.postman_environment.json'

# -- chapter 14's real time --------------------------------------------------

# Two scripts, because chapter 14 has a static half and a running half and they
# fail for unrelated reasons.
#
# realtime-cases.mjs is the static half and needs nothing started. Its cases
# edit the composer's input rather than a schema, because a subscription
# transport is configured rather than declared, and what it asserts is the
# subscription block the composer wrote - which is the only place any of this
# is visible, since none of it reaches the client-facing schema.
#
# subscription-run.mjs is the running half, and it is the thing chapter 5 said
# did not exist. It opens both of Mosaic's subscriptions through the router,
# submits one review, and checks that both deliver it. That closes the hole
# chapter 5 recorded in its own prose: until this chapter, a change that broke
# onReviewAdded passed this gate without a word.
$RealtimeCases      = Join-Path $RepoRoot 'scripts' 'realtime-cases.mjs'
$SubscriptionRun    = Join-Path $RepoRoot 'scripts' 'subscription-run.mjs'
$RealtimePostman    = Join-Path $RepoRoot 'postman' 'mosaic-realtime.postman_collection.json'
$RealtimePostmanEnv = Join-Path $RepoRoot 'postman' 'mosaic-realtime.local.postman_environment.json'

# What the storefront query costs, and where. Chapter 12 prints these numbers,
# so the gate produces them: the same query through the router with and without
# Product.shippingCost, read off each subgraph's own request timeline. They are
# counts rather than timings, which is why they belong in a gate at all -
# decision 62 keeps milliseconds out of one.
#
# The selection deliberately stops short of Product.reviews. A resolver runs
# per review author, so the count of a query that walks the reviews depends on
# how many reviews exist, and the Postman collection above submits one. Left in,
# these numbers would be true only of a database nothing had written to yet,
# which is the opposite of what a gate wants. averageRating still reaches the
# review table, so the statement count is honest.
#
# Chapter 11 asserted two numbers off one service, because one service was all
# that could report. There are four rows now because there are four services
# doing the work, and two of the six do none of it. Catalog's row is the one
# chapter 11 could not print at all.
$ShippingStorefrontQuery =
    '{ browseProducts(first: 25) { nodes { title price { amount currency } ' +
    'shippingCost { amount currency } availableQuantity averageRating } } }'

$PlainStorefrontQuery =
    '{ browseProducts(first: 25) { nodes { title price { amount currency } ' +
    'availableQuantity averageRating } } }'

# Resolvers and statements per subgraph. Catalog runs the root field and one
# projected query; each of the other three runs one _entities field plus one
# resolver per product, behind one batched statement. Pricing runs two per
# product when shippingCost is selected, and still one statement, because both
# fields take the same DataLoader.
#
# A subgraph absent from these tables is asserted to have reported nothing at
# all, which is the assertion that would catch the router fetching from a
# service it has no reason to call.
$ExpectedStorefrontWithout = [ordered] @{
    catalog   = @{ Resolvers = 1;  Sql = 1 }
    pricing   = @{ Resolvers = 26; Sql = 1 }
    inventory = @{ Resolvers = 26; Sql = 1 }
    reviews   = @{ Resolvers = 26; Sql = 1 }
}

$ExpectedStorefrontWith = [ordered] @{
    catalog   = @{ Resolvers = 1;  Sql = 1 }
    pricing   = @{ Resolvers = 51; Sql = 1 }
    inventory = @{ Resolvers = 26; Sql = 1 }
    reviews   = @{ Resolvers = 26; Sql = 1 }
}

# The three that answer nothing for this query, and should say nothing about
# it. Nodes joined the list at chapter 13 by being the seventh subgraph and
# having no part in a storefront query: the only way into it is Query.node.
$SilentForStorefront = @('accounts', 'ordering', 'nodes')

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

# The book's oldest query and the numbers it produces.
#
# Chapters 2 to 5 asked this of one service:
#
#   { products { title reviews(first: 12) { nodes { rating author { displayName } } } } }
#
# No service can answer it since chapter 8, and after chapter 12 it takes three
# to answer it: `products` is Catalog's, `reviews` is Reviews', and the author
# behind each review is Accounts'. The question is still asked in pieces here,
# without a router, because a subgraph you cannot test on its own is a subgraph
# you will debug through a router.
#
# The Catalog piece is the plain root field.
$CatalogQuery         = '{ products { id title } }'
$ExpectedProductCount = 25

# The Reviews piece is the nested selection, reached the way a router reaches
# it: one _entities call carrying every product key Catalog just handed over.
# first: 12 is not arbitrary - the most reviewed product has exactly 12, so this
# still asks for every review in the seed data and the total is still 120.
#
# The author is a key in a wrapper now rather than a customer, so this selects
# the identifier and stops. Chapter 12 is where that field stopped costing
# Reviews anything at all.
$ReviewsEntitiesQuery =
    'query($representations: [_Any!]!) { _entities(representations: $representations) ' +
    '{ ... on Product { reviews(first: 12) { nodes { rating author { id } } } } } }'
$ExpectedReviewCount = 120

# One _entities field and 25 review connections. It was 146 from chapter 2 to
# chapter 11 and the missing 120 are the authors, which is a stranger result
# than it looks and is worth being exact about.
#
# The engine still produces 120 authors. What it no longer does is run 120
# resolver tasks to get them. GetAuthor takes a parent and returns a new object
# with no await in it, so HotChocolate compiles it to a PureFieldDelegate and
# runs it inline; the ResolveFieldValue diagnostic event is raised inside
# ResolverTask.Execute and BatchResolverTask and nowhere else, so an inlined
# field is never counted. Read at tag 16.6.0, commit 8fea46e.
#
# Which means this number measures resolver tasks rather than fields resolved,
# and has done since chapter 3 without anybody noticing, because until chapter
# 12 every field on this path did something asynchronous.
$ExpectedReviewsResolverCount = 26

# One statement, and it was two at tag ch11. The pair used to be the reviews
# batch and the authors batch; the authors batch is Accounts' now, and Reviews
# is left with the one query it was always going to run.
#
# This is the clearest single number chapter 12 produces. It is not a saving:
# the customers are still fetched, in another process, off another database,
# behind an HTTP call the router makes. What the number measures is that the
# work left this service, which is the only thing an extraction can ever do.
$ExpectedReviewsSqlCount = 1

# The lookup counter follows it exactly, for the reason chapter 4 gave: they are
# the same number whenever one question produces one statement.
$ExpectedReviewsLookupCount = 1

# The third piece, and the one chapter 12 added: the twelve distinct customers
# behind those hundred and twenty reviews, as Accounts resolves them. This is
# the batch that used to happen inside the monolith and now crosses a boundary.
$AccountsEntitiesQuery =
    'query($representations: [_Any!]!) { _entities(representations: $representations) ' +
    '{ ... on Customer { id displayName email } } }'
$ExpectedDistinctCustomers = 12

# Catalog's reference resolver sits behind a DataLoader, so a batch of any size
# costs one statement. This is the assertion that would catch a subgraph
# resolving representations one at a time, which no assertion on the answer
# could see.
$CatalogEntitiesQuery =
    'query($representations: [_Any!]!) { _entities(representations: $representations) ' +
    '{ ... on Product { title sku } } }'

# Pricing answers for the same keys, which is the assertion that would catch
# the four subgraphs disagreeing about what a product key looks like.
$PricingEntitiesQuery =
    'query($representations: [_Any!]!) { _entities(representations: $representations) ' +
    '{ ... on Product { price { amount currency } } } }'

# An order is the other direction: Ordering hands out a product key and a
# customer key it cannot resolve itself. The orders query also selects
# Order.total, which is the regression test for the Include that was missing
# from chapter 4 until chapter 8 - total throws when the lines are not loaded,
# so selecting it is enough.

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

# One process per subgraph, recorded on the $Subgraphs entry as each one starts,
# so the finally block can stop whatever got as far as starting.
$tempDir = $null
$startedDatabase = $false
$startedBroker = $false
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

    # Chapter 14's broker, started the same way and for the same reason: it is
    # described in docker-compose.yml and nowhere else. Reviews publishes to it
    # and the router subscribes to it, and the subgraph that declares the field
    # in between is a schema file with no process.
    #
    # Started here rather than beside the router because Reviews connects to it
    # at start-up, and a broker that arrives late costs a warning per review
    # rather than an error - which would make the event-driven subscription
    # fail much later and for a reason nothing prints.
    & docker compose --project-directory $RepoRoot up --detach --wait --wait-timeout $DatabaseTimeoutSeconds mosaic-nats
    if ($LASTEXITCODE -ne 0) {
        Stop-Verify 'broker' (Join-Lines @(
            "docker compose up mosaic-nats exited with $LASTEXITCODE."
            'The health check hits the monitoring endpoint on 8222, which the -m flag'
            'in the compose command turns on. If the container is up but unhealthy,'
            'something else is on 4222 or 8222.'
            ''
            '    docker compose logs mosaic-nats'))
    }
    $startedBroker = $true
    Write-Ok 'nats is up and healthy'

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

    # Two schemas from chapter 8 and six since chapter 12, checked the same way.
    # Each subgraph is a separate contract with the composer, so a drift in any
    # of them is a drift.
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
            # The printed command uses the absolute path on purpose. `--output`
            # resolves a relative path against the *project* directory rather
            # than the working directory, so a repo-root-relative path pasted at
            # the repo root fails with a DirectoryNotFoundException, which is a
            # baffling thing to be told by a remediation hint.
            Stop-Verify "schema drift ($name)" (Join-Lines @(
                "The exported schema is not the one committed in $relative."
                ''
                'If the change is deliberate, regenerate the snapshot and commit it:'
                ''
                "    dotnet run --project $($subgraph.Project) -- schema export --output `"$($subgraph.Committed)`""
                ''
                'If it is not, a dependency changed the schema behind your back. That is'
                'what this check exists to catch. Since chapter 8 it also catches a'
                'change to one subgraph that would break composition with the others.'
                ''
                (Get-SchemaDiff -ExpectedPath $subgraph.Committed -ActualPath $exportedSchema -WorkDir $tempDir -Label $name)))
        }

        $subgraph.Exported = $exportedSchema
    }
    Write-Ok "all $($Subgraphs.Count) subgraph schemas match the committed snapshots"

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

    # -- 4c. the interface-object sample ------------------------------------

    # Chapter 13's two sample subgraphs. Neither is ever started here: what the
    # gate needs from them is that their committed SDL is what the projects
    # export, because scripts/modeling-cases.mjs composes those files and
    # asserts what the composer says about them. A drift would make three of
    # that script's cases assert something about a schema nobody publishes.
    if (-not (Test-Path -LiteralPath $InterfaceObjectDir)) {
        Write-Skipped 'interface-object sample' 'samples/interface-object does not exist yet'
    } else {
        $interfaceProjects = @{
            'interface-object-library' = Join-Path $InterfaceObjectDir 'Mosaic.Sample.InterfaceObject.Library'
            'interface-object-ratings' = Join-Path $InterfaceObjectDir 'Mosaic.Sample.InterfaceObject.Ratings'
        }

        foreach ($sample in $interfaceProjects.Keys) {
            $committed = Join-Path $SampleSchemaDir "$sample.graphql"
            $exported = Join-Path $tempDir "$sample.graphql"
            & dotnet run --project $interfaceProjects[$sample] -c Release --no-build --no-launch-profile -- schema export --output $exported
            if ($LASTEXITCODE -ne 0) {
                Stop-Verify "interface-object sample ($sample)" 'Exporting the schema failed; its output above says why.'
            }
            if (-not (Test-Path -LiteralPath $committed)) {
                Stop-Verify "interface-object sample ($sample)" "There is no committed snapshot at $committed."
            }
            if (-not (Test-SameText $committed $exported)) {
                Stop-Verify "interface-object sample ($sample)" (Join-Lines @(
                    "schema/samples/$sample.graphql is not what the project exports."
                    ''
                    (Get-SchemaDiff -ExpectedPath $committed -ActualPath $exported `
                        -WorkDir $tempDir -Label $sample)))
            }
        }

        # The directive by name, for the same reason the placement check names
        # a field: the claim is that HotChocolate emits @interfaceObject on the
        # contributing type and @key on the owning interface, and a release
        # that stopped doing either would leave both files looking plausible.
        $ratingsSdl = Get-NormalisedText (Join-Path $SampleSchemaDir 'interface-object-ratings.graphql')
        if ($ratingsSdl -notmatch 'type Media @key\(fields: "id"\) @interfaceObject') {
            Stop-Verify 'interface-object sample' 'The ratings subgraph no longer declares Media as @key + @interfaceObject, which is the whole sample.'
        }
        $librarySdl = Get-NormalisedText (Join-Path $SampleSchemaDir 'interface-object-library.graphql')
        if ($librarySdl -notmatch 'interface Media @key\(fields: "id"\)') {
            Stop-Verify 'interface-object sample' 'The library subgraph no longer keys the Media interface, and an @interfaceObject needs an entity interface to attach to.'
        }
        Write-Ok 'the interface-object sample still declares the two directives chapter 13 prints'
    }

    # -- 5. start the seven subgraphs --------------------------------------

    foreach ($name in $Subgraphs.Keys) {
        if (Test-PortInUse $Subgraphs[$name].Port) {
            Stop-Verify "start $name" (Join-Lines @(
                "Something is already listening on port $($Subgraphs[$name].Port)."
                'Stop it first: a docker compose stack, a debugger, or an earlier run of this script.'))
        }
    }

    # --no-launch-profile means launchSettings.json is ignored, and without it
    # ASP.NET Core defaults to Production. HotChocolate 16 answers introspection
    # only in Development, so the Postman collection's introspection request
    # would fail with HC0046. Say Development explicitly rather than relying on
    # a profile this script deliberately does not load.
    $env:ASPNETCORE_ENVIRONMENT = 'Development'

    # Chapter 5 gave Mosaic a mutation, and the Postman collection uses it. A
    # verification run therefore leaves a review behind, and the next run would
    # start with 121 of them and fail the seeded-count assertion above. So each
    # run starts from a dropped and reseeded database. That costs a second or two
    # and buys a gate whose result does not depend on how many times it has been
    # run before, which is the only kind worth having.
    #
    # One variable resets all six of the services that have a database, which is the whole reason every seeder spells
    # it the same way.
    $env:MOSAIC_RESET_DATABASE = '1'

    # One at a time rather than all seven at once, because each of them creates and
    # seeds a database on the way up and doing that in sequence makes a failure
    # readable. It also costs the slowest part of a run: six .NET services
    # starting one after another.
    foreach ($name in $Subgraphs.Keys) {
        $subgraph = $Subgraphs[$name]
        $subgraph.Stdout = Join-Path $tempDir "$name.out.log"
        $subgraph.Stderr = Join-Path $tempDir "$name.err.log"

        # The URL goes in through the environment rather than the command line:
        # RunWithGraphQLCommands parses the process arguments itself, and it
        # should not have to know about --urls.
        $env:ASPNETCORE_URLS = $subgraph.Url

        $subgraph.Process = Start-Process `
            -FilePath 'dotnet' `
            -ArgumentList @('run', '--project', $subgraph.Project, '-c', 'Release', '--no-build', '--no-launch-profile') `
            -WorkingDirectory $RepoRoot `
            -RedirectStandardOutput $subgraph.Stdout `
            -RedirectStandardError $subgraph.Stderr `
            -NoNewWindow `
            -PassThru

        $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
        $healthy = $false
        while ((Get-Date) -lt $deadline) {
            if ($subgraph.Process.HasExited) {
                Stop-Verify "start $name" (Join-Lines @(
                    "The $name subgraph exited with code $($subgraph.Process.ExitCode) during start-up."
                    ''
                    (Get-LogTail $subgraph.Stdout)
                    (Get-LogTail $subgraph.Stderr)))
            }

            try {
                $health = Invoke-WebRequest -Uri "$($subgraph.Url)/health" -TimeoutSec 5 -SkipHttpErrorCheck
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
            Stop-Verify "start $name" (Join-Lines @(
                "$($subgraph.Url)/health did not answer within $StartupTimeoutSeconds seconds."
                ''
                (Get-LogTail $subgraph.Stdout)
                (Get-LogTail $subgraph.Stderr)))
        }
        Write-Ok "$name answering on $($subgraph.Url)/health"
    }

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

        # Deliberately not anchored on the closing bracket. Two of the six key
        # an entity they can only reference - Ordering's Product and Customer
        # both say resolvable: false - and a check that missed those would be
        # checking four subgraphs and reporting on six.
        if ($publishedSdl -notmatch '@key\(fields: "id"') {
            Stop-Verify "$name published schema" (Join-Lines @(
                "The $name subgraph publishes no @key(fields: `"id`")."
                'A schema printed without its key directives composes into a graph with'
                'no entities in it, which is the failure chapter 7 warned about.'))
        }
    }
    Write-Ok "all $($Subgraphs.Count) subgraphs publish the committed schemas through _service"

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

    $catalogPayload = Invoke-Gql -Url $MosaicCatalogUrl -Query $CatalogQuery -Step 'catalog products'
    $products = @($catalogPayload.data.products)

    if ($products.Count -ne $ExpectedProductCount) {
        Stop-Verify 'product count' "Expected $ExpectedProductCount products from Catalog, got $($products.Count)."
    }
    Write-Ok "catalog answered $ExpectedProductCount products"

    # Every key exactly as Catalog gave it. Re-encoding one here would test this
    # script's idea of the format rather than the six services' agreement about
    # it, which is the only thing that matters.
    $representations = @($products | ForEach-Object { @{ __typename = 'Product'; id = $_.id } })

    # -- 5e. the Reviews half, through _entities ---------------------------

    $entitiesPayload = Invoke-Gql -Url $MosaicReviewsUrl -Query $ReviewsEntitiesQuery `
        -Variables @{ representations = $representations } -Step 'reviews _entities'

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
            'field it arrives through changed in chapter 8, the service that owns it'
            'changed in chapter 12, and the answer has not changed at all.'))
    }
    Write-Ok "reviews answered $ExpectedProductCount representations with $ExpectedReviewCount reviews"

    # -- 5e2. the Accounts half, which chapter 12 created -------------------

    # Every author key those reviews carried, handed to the service that owns
    # customers. Inside the monolith this was a DataLoader call; it is an
    # _entities call across a network now, and it is the clearest single measure
    # of what the split moved.
    $authorKeys = @($entities.reviews.nodes.author.id | Where-Object { $_ } | Select-Object -Unique)
    if ($authorKeys.Count -ne $ExpectedDistinctCustomers) {
        Stop-Verify 'author keys' (Join-Lines @(
            "Expected $ExpectedDistinctCustomers distinct customers behind $ExpectedReviewCount reviews, got $($authorKeys.Count)."
            'Reviews hands back a key in a wrapper and resolves no customer at all'
            'since chapter 12, so this is a count of what it stored rather than of'
            'what it looked up.'))
    }

    $customerReps = @($authorKeys | ForEach-Object { @{ __typename = 'Customer'; id = $_ } })
    $customersPayload = Invoke-Gql -Url $MosaicAccountsUrl -Query $AccountsEntitiesQuery `
        -Variables @{ representations = $customerReps } -Step 'accounts _entities'

    $customers = @($customersPayload.data._entities)
    if ($customers.Count -ne $ExpectedDistinctCustomers) {
        Stop-Verify 'customer count' "Sent $($customerReps.Count) customer representations, got $($customers.Count) back."
    }
    if (@($customers | Where-Object { -not $_.displayName }).Count -gt 0) {
        Stop-Verify 'customer count' (Join-Lines @(
            'Accounts answered a null entity for a key Reviews handed out.'
            'Both services encode a customer identifier the same way or they do not'
            'share an entity at all, and CustomerKey.TryDecode is where that'
            'agreement is written down twice.'))
    }
    Write-Ok "accounts resolved all $ExpectedDistinctCustomers customer keys reviews handed out"

    # -- 5e3. Pricing answers for the same product keys ---------------------

    $pricesPayload = Invoke-Gql -Url $MosaicPricingUrl -Query $PricingEntitiesQuery `
        -Variables @{ representations = $representations } -Step 'pricing _entities'
    $prices = @($pricesPayload.data._entities)
    if ($prices.Count -ne $ExpectedProductCount -or @($prices | Where-Object { -not $_.price }).Count -gt 0) {
        Stop-Verify 'pricing _entities' (Join-Lines @(
            "Pricing answered $($prices.Count) entities for $ExpectedProductCount representations,"
            "$(@($prices | Where-Object { -not $_.price }).Count) of them without a price."
            'Four subgraphs now decode the same product key with four copies of the'
            'same file. This is the assertion that catches one of them drifting.'))
    }
    Write-Ok "pricing priced all $ExpectedProductCount representations catalog handed out"

    # A key that is not one of ours: a null entity and no errors key. The raw
    # Guid is the interesting case, because that is what this identifier looked
    # like before chapter 5 made it a global object identifier.
    foreach ($badKey in @('not-a-key', 'a0000000-0000-4000-8000-000000000001')) {
        $badResponse = Invoke-WebRequest `
            -Uri "$MosaicCatalogUrl/graphql" -Method Post -ContentType 'application/json' `
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

    # Asserted from the answer rather than from a counter: 25 representations
    # in, 25 titles out, in order. Catalog does have a request timeline since
    # chapter 12, and the statement count is checked further down with the other
    # five; what this step is for is the ordering, which no counter can see.
    $catalogEntities = Invoke-Gql -Url $MosaicCatalogUrl -Query $CatalogEntitiesQuery `
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

    # An order line hands out a product key Ordering cannot resolve itself, and
    # an order hands out a customer key it cannot resolve either. The total is
    # the regression test for the Include that was missing from chapter 4 until
    # chapter 8: Order.total throws when the lines are not loaded, and nothing
    # in the collection had ever asked for one.
    #
    # There is still no root field that lists customers, so the keys come out of
    # the answer above: every review carries its author. Since chapter 12 that
    # is a key rather than a customer, which makes the point sharper rather than
    # weaker - it means Reviews and Ordering agree about how a customer is
    # spelled without either of them ever having seen one.
    #
    # Seven of the twelve seeded customers have no orders at all, so this walks
    # the authors until it finds one who does rather than assuming.
    $customerKeys = @($authorKeys)
    if ($customerKeys.Count -lt 1) {
        Stop-Verify 'orders' 'No review carried an author, so there is no customer key to follow.'
    }

    $orders = @()
    $customerKey = $null
    foreach ($candidate in $customerKeys) {
        $ordersPayload = Invoke-Gql -Url $MosaicOrderingUrl `
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
            'the keys Catalog handed out. Four services encode the same identifier the'
            'same way or they do not share an entity at all.'))
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
        Invoke-Gql -Url $MosaicReviewsUrl -Query $ReviewsEntitiesQuery `
            -Variables @{ representations = $representations } -Step 'reviews _entities' | Out-Null
    }

    # -- 6. the lookup count -----------------------------------------------

    # Read off Reviews rather than off the monolith, because Reviews is the
    # service that answered the query above. The middleware logs the total after
    # the response has been written, so the line can land a moment after the
    # HTTP call returns.
    $reviewsStdout = $Subgraphs['reviews'].Stdout
    $logDeadline = (Get-Date).AddSeconds(15)
    $loggedCounts = @()
    while ((Get-Date) -lt $logDeadline) {
        $loggedCounts = @(
            [regex]::Matches((Get-LogText $reviewsStdout), 'Service lookups this request: (\d+)') |
                ForEach-Object { $_.Groups[1].Value })
        if ($loggedCounts.Count -gt 0) {
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if ($loggedCounts.Count -eq 0) {
        Stop-Verify 'lookup count' (Join-Lines @(
            'Reviews never logged a lookup count for the query.'
            'Either the counting middleware is gone or the log level hides it.'
            'UseMosaicServiceDefaults() is what installs it, in every one of the seven.'
            ''
            (Get-LogTail $reviewsStdout)))
    }

    if ($loggedCounts -notcontains "$ExpectedReviewsLookupCount") {
        Stop-Verify 'lookup count' (Join-Lines @(
            "Expected Reviews to log 'Service lookups this request: $ExpectedReviewsLookupCount'."
            "It logged: $($loggedCounts -join ', ')."
            ''
            'That number is quoted in the book. It was 146 through chapters 2 and 3'
            'and at tag ch04-ef, 3 once chapter 4 added DataLoaders, 2 once chapter 8'
            'took the product lookup out of the monolith, and 1 since chapter 12 gave'
            'the authors to Accounts. If it moved, either a DataLoader stopped'
            'batching or a resolver went back to asking a service directly.'))
    }

    $lookupCeiling = $ExpectedReviewsLookupCount + $SplitBatchAllowance
    $tooManyLookups = @($loggedCounts | Where-Object { [int] $_ -gt $lookupCeiling })
    if ($tooManyLookups.Count -gt 0) {
        Stop-Verify 'lookup count' (Join-Lines @(
            "One of the runs asked for more than $lookupCeiling lookups: $($loggedCounts -join ', ')."
            ''
            'A single split batch costs one extra lookup and is expected now and'
            'again. More than that is a resolver that is not going through a'
            'DataLoader at all.'))
    }
    Write-Ok "reviews logged 'Service lookups this request: $ExpectedReviewsLookupCount'"

    # -- 6b. the request pipeline ------------------------------------------

    # The pipeline is logged once, while the schema is being built, so by the
    # time a query has been answered these lines are already there.
    #
    # Checked on every one of the seven since chapter 13. The list is chapter
    # 3's and none of them should differ from it: they all call the same
    # AddMosaicSubgraph, and a service that assembled a different pipeline would
    # be a service whose registrations had drifted from the platform's.
    foreach ($name in $Subgraphs.Keys) {
        $subgraphPipeline = @(
            [regex]::Matches((Get-LogText $Subgraphs[$name].Stdout), '(?m)^\s+\d+\. (\S+)\s*$') |
                ForEach-Object { $_.Groups[1].Value })
        if ($subgraphPipeline.Count -ne $ExpectedPipeline.Count) {
            Stop-Verify 'request pipeline' (Join-Lines @(
                "The $name subgraph assembled $($subgraphPipeline.Count) middleware, not $($ExpectedPipeline.Count)."
                "Found: $($subgraphPipeline -join ', ')."
                ''
                'All six call AddMosaicSubgraph and AddMosaicPipelineReport, so a'
                'difference here is a difference in one service''s registrations.'))
        }
    }

    $logText = Get-LogText $reviewsStdout

    $loggedPipeline = @(
        [regex]::Matches($logText, '(?m)^\s+\d+\. (\S+)\s*$') |
            ForEach-Object { $_.Groups[1].Value })

    if ($loggedPipeline.Count -eq 0) {
        Stop-Verify 'request pipeline' (Join-Lines @(
            'The service never logged its request pipeline.'
            'AddMosaicPipelineReport() is what writes it; check it is still registered'
            'in Program.cs, and registered after AddGraphQL().'
            ''
            (Get-LogTail $reviewsStdout)))
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
            [regex]::Matches((Get-LogText $reviewsStdout), '(\d+) resolvers,') |
                ForEach-Object { $_.Groups[1].Value })
        if ($loggedResolvers.Count -gt 0) {
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if ($loggedResolvers.Count -eq 0) {
        Stop-Verify 'request timeline' (Join-Lines @(
            'The service never logged a request timeline.'
            'RequestTimelineListener is what writes it. AddMosaicSubgraph registers it'
            'through AddDiagnosticEventListener, alongside the'
            'AddApplicationService<ILoggerFactory>() without which no schema builds.'
            ''
            (Get-LogTail $reviewsStdout)))
    }

    if ($loggedResolvers -notcontains "$ExpectedReviewsResolverCount") {
        Stop-Verify 'request timeline' (Join-Lines @(
            "Expected the timeline to report $ExpectedReviewsResolverCount resolvers for the query."
            "It reported: $($loggedResolvers -join ', ')."
            ''
            'One _entities field and 25 review connections. It was 146 until chapter'
            '12, and the 120 that left are the authors: GetAuthor has no await in it'
            'now, so it compiles to a pure resolver, runs inline, and never reaches'
            'the diagnostic event this count comes from. If it is 146 again, somebody'
            'made that field asynchronous.'))
    }
    Write-Ok "request timeline reported $ExpectedReviewsResolverCount resolvers"

    # -- 6d. the database round trips --------------------------------------

    $loggedSql = @(
        [regex]::Matches((Get-LogText $reviewsStdout), '(\d+) SQL\)') |
            ForEach-Object { $_.Groups[1].Value })

    if ($loggedSql.Count -eq 0) {
        Stop-Verify 'sql command count' (Join-Lines @(
            'The timeline never reported a SQL command count.'
            'SqlCommandCounter is the EF Core interceptor that produces it, and each of'
            'the six services with a database attaches it to its pooled context factory.'
            ''
            (Get-LogTail $reviewsStdout)))
    }

    if ($loggedSql -notcontains "$ExpectedReviewsSqlCount") {
        Stop-Verify 'sql command count' (Join-Lines @(
            "Expected the timeline to report $ExpectedReviewsSqlCount SQL commands for the query."
            "It reported: $($loggedSql -join ', ')."
            ''
            'This is the number chapter 4 is about, chapter 8 moved by one, and'
            'chapter 12 left alone: the author batch went to Accounts, and it was'
            'never one of the two statements this service runs.'))
    }

    $sqlCeiling = $ExpectedReviewsSqlCount + $SplitBatchAllowance
    $tooManyCommands = @($loggedSql | Where-Object { [int] $_ -gt $sqlCeiling })
    if ($tooManyCommands.Count -gt 0) {
        Stop-Verify 'sql command count' (Join-Lines @(
            "One of the runs sent more than $sqlCeiling statements: $($loggedSql -join ', ')."
            ''
            'A single split batch costs one extra statement and is expected now and'
            'again. More than that is an N+1 growing back.'))
    }
    Write-Ok "request timeline reported $ExpectedReviewsSqlCount SQL commands"

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
        # Every URL is overridden rather than trusted: the environment file
        # names the default ports, and this script can be pointed elsewhere.
        $newmanArgs = $newmanPrefix + @(
            'run', $PostmanCollection,
            '--environment', $PostmanEnvironment,
            '--env-var', "catalogUrl=$MosaicCatalogUrl",
            '--env-var', "pricingUrl=$MosaicPricingUrl",
            '--env-var', "inventoryUrl=$($Subgraphs['inventory'].Url)",
            '--env-var', "accountsUrl=$MosaicAccountsUrl",
            '--env-var', "reviewsUrl=$MosaicReviewsUrl",
            '--env-var', "orderingUrl=$MosaicOrderingUrl",
            '--bail')
        & $newmanCommand @newmanArgs
        if ($LASTEXITCODE -ne 0) {
            Stop-Verify 'postman' "newman exited with $LASTEXITCODE; its output above says which request failed."
        }
        Write-Ok 'postman collection'
    }

    # -- 8. the six subgraphs still compose ---------------------------------

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
    # three separate things about those two schemas would break the graph only
    # when it is assembled - two subgraphs both declaring Query.node, the cost
    # directives HotChocolate stamps by default, and PageCursor being the one
    # paging type nothing marks shareable - and none of them is visible from
    # either service on its own.
    #
    # Chapter 12 made that argument five times larger. Six services get those
    # three settings right or the graph does not assemble, which is why they
    # live in Mosaic.ServiceDefaults now rather than in six comments.
    #
    # Chapter 9 adds two things to this step. The composed config is compared
    # against the committed one, because the chapter prints what is inside it.
    # And the composition cases run, because the chapter prints the composer's
    # errors too, and an error message is as easy to go stale as a schema.
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
                'The six subgraph schemas under schema/ no longer compose into one'
                'graph. That is a real finding rather than a tooling problem, and the'
                'table above says which coordinate the composer objected to.'))
        }
        if (-not (Test-Path -LiteralPath $supergraph)) {
            Stop-Verify 'composition' "wgc reported success but wrote nothing to $supergraph."
        }
        Write-Ok "all $($Subgraphs.Count) subgraphs compose into one supergraph"

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

        # -- 8b2. what @override does, which is chapter 12's subject ---------

        # Nine cases, and none of them needs a service running: every one is a
        # composition, because everything @override does happens at composition
        # time. That is itself the finding the chapter leads with.
        if (-not (Test-Path -LiteralPath $OverrideCases)) {
            Write-Skipped 'override cases' 'scripts/override-cases.mjs does not exist yet'
        } elseif (-not (Get-Command node -ErrorAction SilentlyContinue)) {
            Stop-Verify 'override cases' 'node is not on PATH; it is needed to run scripts/override-cases.mjs.'
        } else {
            & node $OverrideCases
            if ($LASTEXITCODE -ne 0) {
                Stop-Verify 'override cases' (Join-Lines @(
                    "scripts/override-cases.mjs exited with $LASTEXITCODE."
                    'One of the @override behaviours chapter 12 describes has changed. The'
                    'output above says which case and how. If it is the progressive one,'
                    'wgc may have implemented the federation 2.7 label argument, and that'
                    'is a rewrite of a section rather than a loosened assertion.'))
            }
            Write-Ok 'the nine @override behaviours chapter 12 prints are the ones wgc produces'
        }

        # -- 8b3. the modelling problems, which is chapter 13's subject -------

        # Sixteen cases, and none of them needs a service running either. The
        # difference from the two scripts above is what is asserted: most of
        # these compose, so the assertion is on the composed client schema and
        # on the routing table rather than on an error. An enum that quietly
        # loses a member and a value type that quietly becomes nullable are
        # both successful compositions.
        if (-not (Test-Path -LiteralPath $ModelingCases)) {
            Write-Skipped 'modelling cases' 'scripts/modeling-cases.mjs does not exist yet'
        } elseif (-not (Get-Command node -ErrorAction SilentlyContinue)) {
            Stop-Verify 'modelling cases' 'node is not on PATH; it is needed to run scripts/modeling-cases.mjs.'
        } else {
            & node $ModelingCases
            if ($LASTEXITCODE -ne 0) {
                Stop-Verify 'modelling cases' (Join-Lines @(
                    "scripts/modeling-cases.mjs exited with $LASTEXITCODE."
                    'One of the behaviours chapter 13 describes has changed. The output above'
                    'says which case and how. Two of them are worth reading carefully before'
                    'assuming the case is at fault: the enum merge rules and the composer'
                    'crash on an entity interface whose implementation has no key. If wgc'
                    'turned that crash into an error message, that is a paragraph to rewrite'
                    'rather than an assertion to loosen.'))
            }
            Write-Ok 'the sixteen modelling behaviours chapter 13 prints are the ones wgc produces'
        }

        # -- 8b4. the subscription transport, which is chapter 14's subject ---

        # Eight cases, and the only ones in this script that edit the
        # composer's input rather than a subgraph schema. None of them needs a
        # service running, and all eight compose: a subscription transport is
        # not part of any schema, so there is no error to assert and the
        # assertion is on the execution config wgc wrote.
        #
        # Two of the eight are wgc disagreeing with its own documentation, and
        # both are silent. Worth reading the output rather than the exit code
        # if a wgc upgrade turns them red, because a fix upstream would be good
        # news that fails this gate.
        if (-not (Test-Path -LiteralPath $RealtimeCases)) {
            Write-Skipped 'realtime cases' 'scripts/realtime-cases.mjs does not exist yet'
        } elseif (-not (Get-Command node -ErrorAction SilentlyContinue)) {
            Stop-Verify 'realtime cases' 'node is not on PATH; it is needed to run scripts/realtime-cases.mjs.'
        } else {
            & node $RealtimeCases
            if ($LASTEXITCODE -ne 0) {
                Stop-Verify 'realtime cases' (Join-Lines @(
                    "scripts/realtime-cases.mjs exited with $LASTEXITCODE."
                    'One of the transport behaviours chapter 14 describes has changed. The'
                    'first case to check is the baseline: it reads the reviews subscription'
                    'block out of federation/mosaic.yaml and fails if the pinned subprotocol'
                    'is gone, because the chapter argues for pinning it and the default is'
                    'the protocol Apollo deprecated in 2019.'))
            }
            Write-Ok 'the eight subscription-transport behaviours chapter 14 prints are the ones wgc produces'
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

            # -- chapter 11 -------------------------------------------------

            # What the second hop costs. This collection needs the router and
            # both subgraphs at once, which is why it runs here rather than
            # beside the subgraph collection above: two of its five requests go
            # straight to a service, to ask it something the router will not.
            if (-not (Test-Path -LiteralPath $EntitiesPostman) -or -not (Test-Path -LiteralPath $EntitiesPostmanEnv)) {
                Write-Skipped 'entities postman' 'the entities collection or its environment is missing from postman/'
            } elseif (-not $newmanCommand) {
                Write-Skipped 'entities postman' 'newman is not installed - run npm install first'
            } else {
                & $newmanCommand @($newmanPrefix + @(
                    'run', $EntitiesPostman,
                    '--environment', $EntitiesPostmanEnv,
                    '--env-var', "routerUrl=$MosaicRouterUrl",
                    '--env-var', "pricingUrl=$MosaicPricingUrl",
                    '--env-var', "catalogUrl=$MosaicCatalogUrl",
                    '--bail'))
                if ($LASTEXITCODE -ne 0) {
                    Stop-Verify 'entities postman' (Join-Lines @(
                        "newman exited with $LASTEXITCODE; its output above says which request failed."
                        'Product.shippingCost is the first field in Mosaic that cannot be'
                        'answered without the other service, and the plan assertions are the'
                        'listing chapter 11 prints.'))
                }
                Write-Ok 'a field that needs the other service is answered, and its plan says so'
            }

            # -- chapter 13 -------------------------------------------------

            # One identifier and four owners, plus the cursor fix. Every
            # request goes to the router, because every one of them is about
            # something no single service can do: the node service holds four
            # two-line stubs and no data, so an answer coming back at all is
            # the router having followed a stub to whoever owns the rest.
            if (-not (Test-Path -LiteralPath $NodesPostman) -or -not (Test-Path -LiteralPath $NodesPostmanEnv)) {
                Write-Skipped 'nodes postman' 'the nodes collection or its environment is missing from postman/'
            } elseif (-not $newmanCommand) {
                Write-Skipped 'nodes postman' 'newman is not installed - run npm install first'
            } else {
                & $newmanCommand @($newmanPrefix + @(
                    'run', $NodesPostman,
                    '--environment', $NodesPostmanEnv,
                    '--env-var', "routerUrl=$MosaicRouterUrl",
                    '--env-var', "nodesUrl=$($Subgraphs['nodes'].Url)",
                    '--bail'))
                if ($LASTEXITCODE -ne 0) {
                    Stop-Verify 'nodes postman' (Join-Lines @(
                        "newman exited with $LASTEXITCODE; its output above says which request failed."
                        'The last two requests are the cursor fix and they fail against tag ch12'
                        'on purpose. The four before them are the node field, and a null in any'
                        'of them is the router not following a stub to its owner.'))
                }
                Write-Ok 'one identifier resolves through four owners, and a page does not repeat a row'
            }

            # Eleven cases: three composition, one against Mosaic's _entities,
            # and seven on the sample under samples/entity-resolution, which
            # that script starts and stops itself. Same arrangement as chapters
            # 9 and 10 and for the same reason: one implementation, called by
            # both verify scripts.
            if (-not (Test-Path -LiteralPath $EntityCases)) {
                Write-Skipped 'entity cases' 'scripts/entity-cases.mjs does not exist yet'
            } elseif (-not (Get-Command node -ErrorAction SilentlyContinue)) {
                Stop-Verify 'entity cases' 'node is not on PATH; it is needed to run scripts/entity-cases.mjs.'
            } else {
                & node $EntityCases
                if ($LASTEXITCODE -ne 0) {
                    Stop-Verify 'entity cases' (Join-Lines @(
                        "scripts/entity-cases.mjs exited with $LASTEXITCODE."
                        'One of the entity-resolution behaviours chapter 11 describes has'
                        'changed. The output above says which. Fix the chapter, not the'
                        'assertion.'))
                }
                Write-Ok 'entities resolve the eleven ways chapter 11 says they do'
            }

            # What the storefront query costs and where, off all six timelines.
            # Both documents are warmed first, because a cold request measures
            # the runtime warming up rather than the query, and both are
            # measured in the same run so the two sets can be compared at all.
            #
            # Chapter 11 read one service's timeline. Chapter 12 reads six, and
            # the reason is the whole chapter: the same query costs the same
            # database work spread across four processes, and the two that do
            # nothing have to be checked for doing nothing.
            foreach ($warm in 1..3) {
                Invoke-Gql -Url $MosaicRouterUrl -Query $PlainStorefrontQuery -Step 'storefront warm-up' | Out-Null
                Invoke-Gql -Url $MosaicRouterUrl -Query $ShippingStorefrontQuery -Step 'storefront warm-up' | Out-Null
            }

            $timelinePattern = '(\d+) resolvers, (\d+) SQL\)'
            $storefrontCases = @(
                @{ Name = 'without shippingCost'; Query = $PlainStorefrontQuery;    Expected = $ExpectedStorefrontWithout }
                @{ Name = 'with shippingCost';    Query = $ShippingStorefrontQuery; Expected = $ExpectedStorefrontWith }
            )

            foreach ($storefrontCase in $storefrontCases) {
                # Where every subgraph's log stood before the query, so that the
                # lines this query produces can be told from the ones the
                # warm-up did.
                $before = @{}
                foreach ($name in $Subgraphs.Keys) {
                    $before[$name] = ([regex]::Matches((Get-LogText $Subgraphs[$name].Stdout), $timelinePattern)).Count
                }

                Invoke-Gql -Url $MosaicRouterUrl -Query $storefrontCase.Query -Step "storefront $($storefrontCase.Name)" | Out-Null

                foreach ($name in $storefrontCase.Expected.Keys) {
                    $want = $storefrontCase.Expected[$name]
                    $stdout = $Subgraphs[$name].Stdout

                    $deadline = (Get-Date).AddSeconds(15)
                    $match = $null
                    while ((Get-Date) -lt $deadline) {
                        $all = [regex]::Matches((Get-LogText $stdout), $timelinePattern)
                        if ($all.Count -gt $before[$name]) {
                            $match = $all[$all.Count - 1]
                            break
                        }
                        Start-Sleep -Milliseconds 200
                    }

                    if ($null -eq $match) {
                        Stop-Verify 'storefront cost' (Join-Lines @(
                            "$name logged no timeline for the storefront query $($storefrontCase.Name),"
                            'so either the router did not call it or it is not reporting.'
                            ''
                            (Get-LogTail $stdout)))
                    }

                    $resolvers = [int] $match.Groups[1].Value
                    $sql = [int] $match.Groups[2].Value

                    if ($resolvers -ne $want.Resolvers) {
                        Stop-Verify 'storefront cost' (Join-Lines @(
                            "$name reported $resolvers resolvers for the storefront query $($storefrontCase.Name),"
                            "and chapter 12 prints $($want.Resolvers)."))
                    }
                    if ($sql -ne $want.Sql) {
                        Stop-Verify 'storefront cost' (Join-Lines @(
                            "$name reported $sql SQL commands for the storefront query $($storefrontCase.Name),"
                            "and chapter 12 prints $($want.Sql)."
                            'A resolver that reached for its own DataLoader instead of the one'
                            'its neighbour already uses would show up here and nowhere else.'))
                    }
                }

                # And the two that should have been left alone. This is the
                # assertion that would catch the router fetching from a service
                # the query never mentions.
                foreach ($name in $SilentForStorefront) {
                    $after = ([regex]::Matches((Get-LogText $Subgraphs[$name].Stdout), $timelinePattern)).Count
                    if ($after -ne $before[$name]) {
                        Stop-Verify 'storefront cost' (Join-Lines @(
                            "$name answered a request for the storefront query $($storefrontCase.Name),"
                            'and nothing in that query is its to answer.'))
                    }
                }
            }

            $shippingDelta = $ExpectedStorefrontWith['pricing'].Resolvers - $ExpectedStorefrontWithout['pricing'].Resolvers
            Write-Ok ("the storefront costs $shippingDelta more resolvers in pricing with shippingCost, " +
                "no more SQL anywhere, and nothing at all in accounts, ordering or nodes")

            # -- 8e. chapter 13: one identifier, four types ------------------

            # The node field, through the router, for each of the four types
            # the graph considers globally addressable. Every one of these
            # crosses at least one boundary on purpose: the node service holds
            # nothing but the key, so a field coming back at all is evidence
            # that the router took the stub and went to the owner.
            $nodeSeed = Invoke-Gql -Url $MosaicRouterUrl -Step 'node identifiers' -Query @'
{
  browseProducts(first: 1) { nodes { id reviews(first: 1) { nodes { id author { id } } } } }
}
'@
            $seedProduct = $nodeSeed.data.browseProducts.nodes[0]
            if (-not $seedProduct.reviews.nodes -or $seedProduct.reviews.nodes.Count -eq 0) {
                Stop-Verify 'node identifiers' 'The first product has no reviews, so this step cannot obtain a Review or a Customer identifier.'
            }
            $nodeIds = @{
                Product  = $seedProduct.id
                Review   = $seedProduct.reviews.nodes[0].id
                Customer = $seedProduct.reviews.nodes[0].author.id
            }

            # Seven of the twelve seeded customers have no orders, so walk the
            # reviewers until one of them does. Chapter 8's open item says the
            # same thing about the same seed data: this is a fair description
            # of the schema rather than a workaround, and it is fragile.
            $reviewers = Invoke-Gql -Url $MosaicRouterUrl -Step 'node identifiers' -Query @"
{
  productById(id: "$($nodeIds.Product)") { reviews(first: 20) { nodes { author { id } } } }
}
"@
            foreach ($reviewer in $reviewers.data.productById.reviews.nodes) {
                $orders = Invoke-Gql -Url $MosaicRouterUrl -Step 'node identifiers' -Query @"
{ ordersByCustomer(customerId: "$($reviewer.author.id)") { id } }
"@
                if ($orders.data.ordersByCustomer.Count -gt 0) {
                    $nodeIds.Order = $orders.data.ordersByCustomer[0].id
                    break
                }
            }
            if (-not $nodeIds.ContainsKey('Order')) {
                Stop-Verify 'node identifiers' 'No reviewer of the first product has an order, so this step cannot obtain an Order identifier.'
            }

            # One field per type, and each one owned by a service other than
            # nodes: title is catalog's, displayName is accounts', rating is
            # reviews' and placedAt is ordering's.
            $nodeExpectations = @(
                @{ Type = 'Product';  Field = 'title' }
                @{ Type = 'Customer'; Field = 'displayName' }
                @{ Type = 'Review';   Field = 'rating' }
                @{ Type = 'Order';    Field = 'placedAt' }
            )
            foreach ($expectation in $nodeExpectations) {
                $payload = Invoke-Gql -Url $MosaicRouterUrl -Step "node($($expectation.Type))" -Query @"
{
  node(id: "$($nodeIds[$expectation.Type])") {
    __typename
    ... on $($expectation.Type) { $($expectation.Field) }
  }
}
"@
                if ($payload.data.node.__typename -ne $expectation.Type) {
                    Stop-Verify "node($($expectation.Type))" (Join-Lines @(
                        "node() answered __typename $($payload.data.node.__typename) for a $($expectation.Type) identifier."
                        'The node service decodes the type name out of the identifier, so this is'
                        'either a change to the identifier format or a stub type that went missing.'))
                }
                if ($null -eq $payload.data.node.$($expectation.Field)) {
                    Stop-Verify "node($($expectation.Type))" (Join-Lines @(
                        "node() answered null for $($expectation.Type).$($expectation.Field)."
                        'That field belongs to a service other than nodes, so a null here means'
                        'the router did not follow the stub to its owner. Chapter 13 is built on'
                        'it doing exactly that.'))
                }
            }
            Write-Ok 'node() answers for all four addressable types, with fields from four other services'

            # -- 8f. chapter 13: the cursor carries its tiebreaker -----------

            # The bug chapter 4 shipped and chapter 13 found. browseProducts is
            # projected from the selection set, and the keyset cursor is built
            # from the materialised entity, so a client asking for nothing but
            # title used to get cursors whose tiebreaker was an empty Guid and
            # a second page that repeated a row.
            #
            # Asked without id on purpose, and through the router on purpose.
            # Adding any field from another subgraph makes the planner ask
            # catalog for id anyway and hides the whole thing, which is why
            # eight chapters of federated queries never tripped over it.
            $firstPage = Invoke-Gql -Url $MosaicRouterUrl -Step 'cursor tiebreaker' -Query @'
{ browseProducts(first: 2) { edges { cursor node { title } } } }
'@
            $edges = $firstPage.data.browseProducts.edges
            $lastCursor = $edges[$edges.Count - 1].cursor
            $decoded = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($lastCursor))
            if ($decoded -match '00000000-0000-0000-0000-000000000000') {
                Stop-Verify 'cursor tiebreaker' (Join-Lines @(
                    "The cursor for a page selecting only title decodes to $decoded."
                    'The all-zero Guid means the projection dropped the key the cursor sorts'
                    'on. QueryContext.Include(p => p.Id) in CatalogService is what puts it'
                    'back, and Product needs a parameterless constructor for that to work.'))
            }

            $secondPage = Invoke-Gql -Url $MosaicRouterUrl -Step 'cursor tiebreaker' -Query @"
{ browseProducts(first: 2, after: "$lastCursor") { edges { node { title } } } }
"@
            $firstTitles = @($edges | ForEach-Object { $_.node.title })
            $secondTitles = @($secondPage.data.browseProducts.edges | ForEach-Object { $_.node.title })
            $repeated = @($secondTitles | Where-Object { $firstTitles -contains $_ })
            if ($repeated.Count -gt 0) {
                Stop-Verify 'cursor tiebreaker' (Join-Lines @(
                    "Page two of browseProducts repeats $($repeated -join ', '), which page one already returned."
                    'A keyset cursor that cannot tell two rows apart re-reads the row it should'
                    'have skipped. This is the defect chapter 13 fixes, and it is invisible in'
                    'the published schema, so nothing else in this gate would catch it.'))
            }
            Write-Ok 'a page selecting only title carries real cursors and does not repeat a row'

            # -- 8g. chapter 14: two subscriptions and one write --------------

            # The step chapter 5 said would not exist. Its own prose named this
            # as the one thing nothing re-checked, and decision 43 accepted
            # that, on the grounds that newman speaks request and response and
            # a subscription is a connection that stays open. That is still
            # true of newman and is why this is a node script rather than a
            # tenth collection.
            #
            # One review is submitted through the router while two
            # subscriptions are open on it, and both have to deliver it. They
            # work in opposite directions and that is the point: onReviewAdded
            # is a field of the Reviews service and the router holds a
            # WebSocket to it, reviewPublished is a field of a schema file and
            # the router holds a NATS subscription instead. A failure in one
            # and not the other says which half broke.
            #
            # The author is chosen from a customer who has not reviewed this
            # product in the seed data, because a duplicate is refused by the
            # domain, publishes nothing, and would look exactly like a
            # subscription that stopped working.
            if (-not (Test-Path -LiteralPath $SubscriptionRun)) {
                Write-Skipped 'subscriptions' 'scripts/subscription-run.mjs does not exist yet'
            } elseif (-not (Get-Command node -ErrorAction SilentlyContinue)) {
                Stop-Verify 'subscriptions' 'node is not on PATH; it is needed to run scripts/subscription-run.mjs.'
            } else {
                $subscriptionProduct = Invoke-Gql -Url $MosaicRouterUrl -Step 'subscriptions' -Query @'
{ browseProducts(first: 1) { nodes { id } } }
'@
                $productId = $subscriptionProduct.data.browseProducts.nodes[0].id

                # A customer nobody has to look up by name. Ordering references
                # customers and Reviews does too, so an author on somebody
                # else's review is a real identifier; the seed data gives the
                # first product six reviewers out of twelve customers, so the
                # last author of the third product is reliably not one of them.
                $otherReviewers = Invoke-Gql -Url $MosaicRouterUrl -Step 'subscriptions' -Query @"
{ node(id: "$productId") { ... on Product { reviews(first: 20) { nodes { author { id } } } } } }
"@
                $taken = @($otherReviewers.data.node.reviews.nodes | ForEach-Object { $_.author.id })
                $everyone = Invoke-Gql -Url $MosaicRouterUrl -Step 'subscriptions' -Query @'
{ browseProducts(first: 6) { nodes { reviews(first: 20) { nodes { author { id } } } } } }
'@
                $candidates = @($everyone.data.browseProducts.nodes
                    | ForEach-Object { $_.reviews.nodes }
                    | ForEach-Object { $_.author.id }
                    | Sort-Object -Unique
                    | Where-Object { $taken -notcontains $_ })

                if ($candidates.Count -eq 0) {
                    Stop-Verify 'subscriptions' (Join-Lines @(
                        'Every customer this gate can reach has already reviewed the first product,'
                        'so there is no write left that the domain would accept. Mosaic has no root'
                        'field listing customers, which is why this walks reviews to find one; a'
                        'seed change that gives the first product every reviewer breaks it. Chapter'
                        '19 owns giving the gate a deterministic route to a customer.'))
                }

                # Three writes rather than one, because chapter 14's finding is
                # about what happens per event rather than per subscription,
                # and one event cannot tell those two apart.
                $writers = @($candidates | Select-Object -First 3)

                # What Accounts has served so far. The chapter prints a table
                # of these counts, and the claim in it is that opening a
                # subscription costs Accounts nothing and every event costs it
                # one _entities call. Both halves are asserted below, because a
                # number this book prints has something that produces it again.
                $accountsBefore = ([regex]::Matches(
                    (Get-LogText $Subgraphs['accounts'].Stdout), 'Mosaic\.RequestTimeline')).Count

                & node $SubscriptionRun `
                    --router "$MosaicRouterUrl/graphql" `
                    --reviews "$($Subgraphs['reviews'].Url)/graphql" `
                    --product $productId `
                    --customer ($writers -join ',')
                if ($LASTEXITCODE -ne 0) {
                    Stop-Verify 'subscriptions' (Join-Lines @(
                        "scripts/subscription-run.mjs exited with $LASTEXITCODE."
                        'Its output above says which of the checks failed. If only the'
                        'event-driven half failed, the broker is the first thing to look at:'
                        'the router needs the events.providers.nats block in router/config.yaml'
                        'and Reviews needs ConnectionStrings__Nats, and a Reviews that cannot'
                        'reach the broker logs a warning per review rather than failing.'))
                }
                Write-Ok "$($writers.Count) reviews, submitted through the router, arrived on both subscriptions"

                # The per-event cost, counted rather than asserted from a plan.
                # Each write is answered on two subscriptions and each of those
                # payloads selects author.displayName, which Reviews cannot
                # answer, so Accounts serves one _entities call per delivery.
                # The event-driven subscription also fetches the review itself,
                # which is Reviews' work rather than Accounts'.
                $accountsAfter = ([regex]::Matches(
                    (Get-LogText $Subgraphs['accounts'].Stdout), 'Mosaic\.RequestTimeline')).Count
                $accountsDelta = $accountsAfter - $accountsBefore
                $expectedDelta = $writers.Count * 2
                if ($accountsDelta -ne $expectedDelta) {
                    Stop-Verify 'per-event cost' (Join-Lines @(
                        "Accounts served $accountsDelta requests across $($writers.Count) writes; expected $expectedDelta."
                        'Chapter 14 prints this as one entity fetch per event per subscription,'
                        'and both subscriptions select author.displayName. A smaller number means'
                        'the router started caching the author across events, which would be a'
                        'better graph and a wrong chapter. A larger one means something else in'
                        'this gate is talking to Accounts while the subscription is open.'))
                }
                Write-Ok "each event cost Accounts exactly one entity fetch ($accountsDelta across $($writers.Count) writes on two subscriptions)"
            }

            # The rest of chapter 14, in the form newman can carry. Decision 43
            # is why this is a separate thing from the step above rather than a
            # tenth collection covering everything: a subscription is a
            # connection that stays open and newman is not built for one. What
            # a request and a response can prove is a lot, though - that the
            # graph declares both fields, that the subgraph has heard of only
            # one, that both plan to a Trigger node, and that a mutation now
            # goes through the router and comes back with a field from another
            # subgraph in its payload.
            if (-not (Test-Path -LiteralPath $RealtimePostman) -or -not (Test-Path -LiteralPath $RealtimePostmanEnv)) {
                Write-Skipped 'realtime postman' 'the realtime collection or its environment is missing from postman/'
            } elseif (-not $newmanCommand) {
                Write-Skipped 'realtime postman' 'newman is not installed - run npm install first'
            } else {
                & $newmanCommand @($newmanPrefix + @(
                    'run', $RealtimePostman,
                    '--environment', $RealtimePostmanEnv,
                    '--env-var', "routerUrl=$MosaicRouterUrl",
                    '--env-var', "reviewsUrl=$($Subgraphs['reviews'].Url)",
                    '--bail'))
                if ($LASTEXITCODE -ne 0) {
                    Stop-Verify 'realtime postman' (Join-Lines @(
                        "newman exited with $LASTEXITCODE; its output above says which request failed."
                        'The two plan requests use X-WG-Skip-Loader, so they never touch a'
                        'subgraph and cannot fail for a data reason. The mutation can: it needs'
                        'a customer who has not already reviewed the first product, which the'
                        'first request in the collection goes looking for.'))
                }
                Write-Ok 'the realtime collection passes against the router'
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

    foreach ($name in $Subgraphs.Keys) {
        $process = $Subgraphs[$name].Process
        if ($process -and -not $process.HasExited) {
            try {
                # dotnet run launches the application as a child process, so the
                # whole tree has to go. Killing only the process we started
                # leaves the app holding the port.
                $process.Kill($true)
                [void] $process.WaitForExit(15000)
            } catch {
                Write-Host "Could not stop the $name subgraph (pid $($process.Id)): $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    }

    foreach ($name in $Subgraphs.Keys) {
        if (-not $Subgraphs[$name].Process) {
            continue
        }
        $port = $Subgraphs[$name].Port
        $portDeadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $portDeadline -and (Test-PortInUse $port)) {
            Start-Sleep -Milliseconds 250
        }
        if (Test-PortInUse $port) {
            Write-Host "Warning: something is still listening on port $port after the $name subgraph was stopped." -ForegroundColor Yellow
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

    # The broker holds nothing worth keeping: every subject in this graph is
    # fire and forget, and nothing in Mosaic reads a stream back. Stopped on
    # the same switch as the database so that one flag leaves the whole stack
    # up for poking at.
    if ($startedBroker -and -not $KeepDatabase) {
        & docker compose --project-directory $RepoRoot stop mosaic-nats *> $null
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
