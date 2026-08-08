#!/usr/bin/env bash
#
# Verifies the Mosaic sample service end to end.
#
# This is the gate that has to pass before a chapter tag is cut, and it is the
# script a reader runs to check that the code in the book still does what the
# book says it does. It starts the database, builds the solution, checks the
# committed schema snapshot against a freshly exported one, starts the service,
# runs the chapter's query and asserts the numbers the chapter quotes.
#
# Since chapter 4 this needs Docker: Mosaic's data lives in PostgreSQL and the
# script brings the container up itself. Set MOSAIC_KEEP_DATABASE=1 to leave it
# running afterwards, which is worth doing while iterating.
#
# scripts/verify.ps1 is the same script for readers on Windows. Changes to one
# belong in the other.
#
# Usage: bash scripts/verify.sh
#
# There is no `set -e` on purpose: every command that matters has its exit code
# checked by hand, right where the failure message is written.

set -u

PORT="${MOSAIC_PORT:-5100}"
STARTUP_TIMEOUT_SECONDS="${MOSAIC_STARTUP_TIMEOUT:-60}"
DATABASE_TIMEOUT_SECONDS="${MOSAIC_DATABASE_TIMEOUT:-90}"
KEEP_DATABASE="${MOSAIC_KEEP_DATABASE:-0}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

SOLUTION="$REPO_ROOT/Mosaic.slnx"
API_PROJECT="$REPO_ROOT/src/Mosaic.Api/Mosaic.Api.csproj"
COMMITTED_SCHEMA="$REPO_ROOT/schema/mosaic.graphql"
SAMPLES_DIR="$REPO_ROOT/samples/three-approaches"
SAMPLE_SCHEMA_DIR="$REPO_ROOT/schema/samples"
POSTMAN_COLLECTION="$REPO_ROOT/postman/mosaic.postman_collection.json"
POSTMAN_ENVIRONMENT="$REPO_ROOT/postman/mosaic.local.postman_environment.json"
BASE_URL="http://localhost:$PORT"

# The three sample projects, in the order the chapter introduces them, written as
# <name committed under schema/samples>:<folder under samples/three-approaches>.
# All three describe the same schema three different ways, so all three must
# export exactly the same SDL.
SAMPLE_APPROACHES="implementation-first:Mosaic.Sample.ImplementationFirst
code-first:Mosaic.Sample.CodeFirst
schema-first:Mosaic.Sample.SchemaFirst"

# The chapter's query and the numbers it produces.
#
# The lookup count was the point of the exercise for two chapters: one lookup
# for the product list, one per product for its reviews, one per review for its
# author, 1 + 25 + 120 = 146. Chapter 4's DataLoaders make it 3. The resolver
# count stays at 146, because the engine still runs every one of those
# resolvers; what changed is what a resolver does when it gets there.
VERIFY_QUERY='{ products { title reviews { rating author { displayName } } } }'
EXPECTED_PRODUCT_COUNT=25
EXPECTED_REVIEW_COUNT=120
EXPECTED_LOOKUP_COUNT=3

# The request pipeline HotChocolate assembles for this service, in order. Twelve
# of these come from the default pipeline; CostAnalyzerMiddleware is inserted
# after DocumentValidationMiddleware by the cost analyzer that AddGraphQL turns
# on unless default security is disabled. Chapter 3 prints this list, so a
# change here is a change to the chapter.
EXPECTED_PIPELINE="InstrumentationMiddleware
ExceptionMiddleware
TimeoutMiddleware
DocumentCacheMiddleware
DocumentParserMiddleware
DocumentValidationMiddleware
CostAnalyzerMiddleware
OperationCacheMiddleware
OperationResolverMiddleware
SkipWarmupExecutionMiddleware
OperationVariableCoercionMiddleware
ConcurrencyGateMiddleware
OperationExecutionMiddleware"

# Every resolver the engine runs for the verify query does exactly one
# domain-service lookup, so this matches EXPECTED_LOOKUP_COUNT. Plain record
# properties - title, rating, displayName - are not resolvers and are not
# counted.
EXPECTED_RESOLVER_COUNT=146

# What the same query costs the database. Until chapter 4 this could only be
# guessed at from the lookup count; now an EF Core command interceptor counts
# the statements that actually reach PostgreSQL, and the timeline reports it.
#
# At tag ch04-ef this was 146, equal to the lookup count, because every
# single-key lookup was one statement. The DataLoaders make it 3: the products,
# their reviews in one batch, and the twelve distinct authors of those reviews
# in another.
EXPECTED_SQL_COMMAND_COUNT=3

# How many times the verify query is sent, and how far above the expected
# number a single run is allowed to land. See the comment beside the repeat
# loop: one split batch costs one extra statement and one extra lookup.
VERIFY_QUERY_RUNS=5
SPLIT_BATCH_ALLOWANCE=1

API_PID=""
TEMP_DIR=""
API_LOG=""
SUMMARY=""
JSON_TOOL=""
STARTED_DATABASE=0

# ---------------------------------------------------------------------------
# Step reporting
# ---------------------------------------------------------------------------

step_ok() {
    printf '[ok]   %s\n' "$1"
    SUMMARY="${SUMMARY}[ok]   $1"$'\n'
}

step_skip() {
    printf '[skip] %s - %s\n' "$1" "$2"
    SUMMARY="${SUMMARY}[skip] $1 - $2"$'\n'
}

# Prints the failure, records it, and exits. The EXIT trap stops the service and
# prints the summary; nothing after a failed step is worth running.
step_fail() {
    printf '[FAIL] %s\n' "$1"
    if [ -n "${2:-}" ]; then
        printf '%s\n' "$2"
    fi
    SUMMARY="${SUMMARY}[FAIL] $1"$'\n'
    exit 1
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        step_fail "$2" "$1 is not on PATH. $3"
    fi
}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

# Schemas are compared by content, not byte for byte. The exporter writes CRLF on
# Windows and LF everywhere else, while .gitattributes stores the committed copy
# with LF, so a byte comparison would fail on Windows every time for a difference
# nobody cares about. Line endings and trailing blank lines are normalised away;
# everything else is compared exactly.
normalise_file() {
    printf '%s\n' "$(tr -d '\r' < "$1")" > "$2"
}

same_text() {
    normalise_file "$1" "$TEMP_DIR/compare.a"
    normalise_file "$2" "$TEMP_DIR/compare.b"
    cmp -s "$TEMP_DIR/compare.a" "$TEMP_DIR/compare.b"
}

# $1 expected file, $2 actual file, $3 label used for the temporary copies.
# The subshell cd is only there to keep the file names in the diff header short.
schema_diff() {
    normalise_file "$1" "$TEMP_DIR/$3.expected.graphql"
    normalise_file "$2" "$TEMP_DIR/$3.actual.graphql"
    ( cd "$TEMP_DIR" && diff -u "$3.expected.graphql" "$3.actual.graphql" ) || true
}

# True only when something answers on the port. curl exits 7 when the connection
# is refused and 28 when it times out; neither is a running service. Timeouts
# count as free deliberately, because some machines drop the connection instead
# of refusing it, and reading that as "port taken" would block every run. The
# cost of being wrong is small: the service then fails to bind, and the health
# poll below reports that with the service's own error in the message.
port_in_use() {
    local rc
    curl -s -o /dev/null --connect-timeout 1 --max-time 2 "http://127.0.0.1:${PORT}/" >/dev/null 2>&1
    rc=$?
    case "$rc" in
        7 | 28) return 1 ;;
        *) return 0 ;;
    esac
}

# Prints "<has_errors> <product_count> <review_count>", tab separated. jq is the
# obvious tool; python3 is there for the machine that does not have it.
summarise_response() {
    if [ "$JSON_TOOL" = "jq" ]; then
        jq -r '[
            (if has("errors") then 1 else 0 end),
            ((.data.products // []) | length),
            ([ (.data.products // [])[] | (.reviews // []) | length ] | add // 0)
        ] | @tsv' "$1"
        return $?
    fi

    python3 - "$1" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    payload = json.load(handle)

data = payload.get("data") or {}
products = data.get("products") or []
reviews = sum(len(product.get("reviews") or []) for product in products)

print("%d\t%d\t%d" % (1 if "errors" in payload else 0, len(products), reviews))
PY
}

log_tail() {
    if [ -n "$API_LOG" ] && [ -f "$API_LOG" ]; then
        tail -n 40 "$API_LOG"
    else
        printf '(the service printed nothing)\n'
    fi
}

# ---------------------------------------------------------------------------
# Cleanup: runs whatever happened above
# ---------------------------------------------------------------------------

stop_api() {
    if [ -z "$API_PID" ]; then
        return 0
    fi
    if ! kill -0 "$API_PID" 2>/dev/null; then
        API_PID=""
        return 0
    fi

    # dotnet run launches the application as a child process, so the children go
    # first. Killing only the process we started leaves the app holding the port.
    if command -v pkill >/dev/null 2>&1; then
        pkill -TERM -P "$API_PID" >/dev/null 2>&1 || true
    fi
    kill -TERM "$API_PID" >/dev/null 2>&1 || true

    waited=0
    while [ "$waited" -lt 100 ] && kill -0 "$API_PID" 2>/dev/null; do
        sleep 0.1
        waited=$((waited + 1))
    done

    if kill -0 "$API_PID" 2>/dev/null; then
        if command -v pkill >/dev/null 2>&1; then
            pkill -KILL -P "$API_PID" >/dev/null 2>&1 || true
        fi
        kill -KILL "$API_PID" >/dev/null 2>&1 || true
    fi

    wait "$API_PID" 2>/dev/null || true
    API_PID=""

    # The port has to be free when we leave, whatever happened above.
    waited=0
    while [ "$waited" -lt 40 ] && port_in_use; do
        sleep 0.25
        waited=$((waited + 1))
    done
    if port_in_use; then
        printf 'Warning: something is still listening on port %s after the service was stopped.\n' "$PORT" >&2
    fi
}

cleanup() {
    status=$?

    stop_api

    if [ -n "$TEMP_DIR" ] && [ -d "$TEMP_DIR" ]; then
        rm -rf "$TEMP_DIR"
    fi

    # The container is stopped, not removed, and its volume is left alone. A
    # verification run should not be able to destroy data, and re-seeding an
    # empty database costs a second anyway.
    if [ "$STARTED_DATABASE" -eq 1 ] && [ "$KEEP_DATABASE" != "1" ]; then
        docker compose --project-directory "$REPO_ROOT" stop mosaic-db >/dev/null 2>&1 || true
    fi

    printf '\n--- summary ---\n'
    printf '%s' "$SUMMARY"
    if [ "$status" -eq 0 ]; then
        printf 'PASS\n'
    else
        printf 'FAIL\n'
    fi

    exit "$status"
}

trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# ---------------------------------------------------------------------------
# Verification
# ---------------------------------------------------------------------------

printf 'mosaic verify - %s\n\n' "$REPO_ROOT"

# -- 1. the SDK -------------------------------------------------------------

require_command dotnet 'dotnet sdk' 'Install the .NET SDK version pinned in global.json.'
require_command curl 'prerequisites' 'It is needed to talk to the service.'
require_command diff 'prerequisites' 'It is needed to show schema differences.'

if command -v jq >/dev/null 2>&1; then
    JSON_TOOL="jq"
elif command -v python3 >/dev/null 2>&1; then
    JSON_TOOL="python3"
else
    step_fail 'prerequisites' 'Neither jq nor python3 is on PATH; one of them is needed to read the GraphQL response.'
fi

SDK_VERSION="$(dotnet --version 2>&1)"
if [ $? -ne 0 ]; then
    step_fail 'dotnet sdk' "dotnet --version failed. global.json pins an SDK that is not installed:
$SDK_VERSION"
fi
step_ok "dotnet sdk $SDK_VERSION"

# -- 1b. the database -------------------------------------------------------

# Mosaic has needed PostgreSQL since chapter 4, and docker-compose.yml is the
# only description of it, so the script starts it from there rather than asking
# the reader to remember a command.
require_command docker 'database' \
    'Mosaic has needed PostgreSQL since chapter 4, and this script starts it from docker-compose.yml.'

docker compose --project-directory "$REPO_ROOT" up --detach --wait \
    --wait-timeout "$DATABASE_TIMEOUT_SECONDS" mosaic-db
if [ $? -ne 0 ]; then
    step_fail 'database' "docker compose up failed.
If the container is restarting, read its log: the PostgreSQL 18 image refuses to
start against a volume written by an earlier major version.

    docker compose logs mosaic-db"
fi
STARTED_DATABASE=1
step_ok 'postgres is up and healthy'

# -- 2. restore and build ---------------------------------------------------

dotnet restore "$SOLUTION" --nologo
if [ $? -ne 0 ]; then
    step_fail 'restore' 'dotnet restore failed; its output above says why.'
fi
step_ok 'restore'

dotnet build "$SOLUTION" -c Release --no-restore --nologo
if [ $? -ne 0 ]; then
    step_fail 'build' 'dotnet build failed. TreatWarningsAsErrors is on, so a single warning is enough to get here.'
fi
step_ok 'build (Release)'

# Everything generated from here on lands in one temporary directory, which the
# EXIT trap deletes. That includes the <name>-settings.json the schema exporter
# writes next to every SDL file it produces.
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/mosaic-verify.XXXXXX")"
API_LOG="$TEMP_DIR/api.log"

# -- 3. schema drift --------------------------------------------------------

EXPORTED_SCHEMA="$TEMP_DIR/mosaic.graphql"
dotnet run --project "$API_PROJECT" -c Release --no-build --no-launch-profile -- \
    schema export --output "$EXPORTED_SCHEMA"
if [ $? -ne 0 ]; then
    step_fail 'schema export' 'dotnet run -- schema export failed; its output above says why.'
fi
if [ ! -f "$EXPORTED_SCHEMA" ]; then
    step_fail 'schema export' "The exporter reported success but wrote nothing to $EXPORTED_SCHEMA."
fi
if [ ! -f "$COMMITTED_SCHEMA" ]; then
    step_fail 'schema drift' "There is no committed snapshot at $COMMITTED_SCHEMA."
fi

if same_text "$COMMITTED_SCHEMA" "$EXPORTED_SCHEMA"; then
    step_ok 'schema matches schema/mosaic.graphql'
else
    step_fail 'schema drift' "The exported schema is not the one committed in schema/mosaic.graphql.

If the change is deliberate, regenerate the snapshot and commit it:

    dotnet run --project src/Mosaic.Api -- schema export --output schema/mosaic.graphql

If it is not, a dependency changed the schema behind your back. That is what
this check exists to catch.

$(schema_diff "$COMMITTED_SCHEMA" "$EXPORTED_SCHEMA" mosaic)"
fi

# -- 4. the three sample projects -------------------------------------------

if [ ! -d "$SAMPLES_DIR" ]; then
    step_skip 'sample schemas' 'samples/three-approaches does not exist yet'
else
    MISSING_PROJECTS=""
    for entry in $SAMPLE_APPROACHES; do
        approach="${entry%%:*}"
        project_dir="$SAMPLES_DIR/${entry#*:}"
        csproj=""
        if [ -d "$project_dir" ]; then
            csproj="$(find "$project_dir" -maxdepth 1 -name '*.csproj' | head -n 1)"
        fi

        if [ -z "$csproj" ]; then
            MISSING_PROJECTS="$MISSING_PROJECTS ${entry#*:}"
            continue
        fi

        # No --no-build here, unlike the main service above. Mosaic.slnx lists
        # all three sample projects, so step 2 has already built them and this is
        # a no-op rebuild costing well under a second. It stays because it is the
        # one thing that keeps this step honest if a fourth sample is ever added
        # to the folder and not to the solution.
        dotnet run --project "$csproj" -c Release --no-launch-profile -- \
            schema export --output "$TEMP_DIR/$approach.graphql"
        if [ $? -ne 0 ]; then
            step_fail "sample schema $approach" "Exporting the schema from $csproj failed."
        fi
        if [ ! -f "$TEMP_DIR/$approach.graphql" ]; then
            step_fail "sample schema $approach" "The exporter reported success but wrote nothing for $approach."
        fi
    done

    if [ -n "$MISSING_PROJECTS" ]; then
        step_fail 'sample schemas' "samples/three-approaches exists but does not hold all three projects.
Missing (no .csproj found under samples/three-approaches/):$MISSING_PROJECTS"
    fi

    # Byte for byte here, not normalised: all three were produced by the same
    # exporter on the same machine in the same run, so any difference at all is a
    # real difference.
    REFERENCE_APPROACH="implementation-first"
    for entry in $SAMPLE_APPROACHES; do
        approach="${entry%%:*}"
        if [ "$approach" = "$REFERENCE_APPROACH" ]; then
            continue
        fi
        if ! cmp -s "$TEMP_DIR/$REFERENCE_APPROACH.graphql" "$TEMP_DIR/$approach.graphql"; then
            step_fail 'sample schemas identical' "The $approach sample does not export the same SDL as the $REFERENCE_APPROACH one.
The whole point of the three is that they describe one schema three ways.

$(schema_diff "$TEMP_DIR/$REFERENCE_APPROACH.graphql" "$TEMP_DIR/$approach.graphql" "sample-$approach")"
        fi
    done
    step_ok 'the three sample schemas are byte-identical'

    for entry in $SAMPLE_APPROACHES; do
        approach="${entry%%:*}"
        committed_sample="$SAMPLE_SCHEMA_DIR/$approach.graphql"
        if [ ! -f "$committed_sample" ]; then
            step_fail "sample schema $approach" "There is no committed snapshot at $committed_sample."
        fi
        if ! same_text "$committed_sample" "$TEMP_DIR/$approach.graphql"; then
            step_fail "sample schema $approach" "schema/samples/$approach.graphql is not what the project exports.

$(schema_diff "$committed_sample" "$TEMP_DIR/$approach.graphql" "committed-$approach")"
        fi
    done
    step_ok 'sample schemas match schema/samples'
fi

# -- 5. start the service and run the chapter's query -----------------------

if port_in_use; then
    step_fail 'start api' "Something is already listening on port $PORT.
Stop it first - a stray 'docker compose up', a debugger, or an earlier run of this script."
fi

# The URL goes in through the environment rather than the command line:
# RunWithGraphQLCommands parses the process arguments itself, and it should not
# have to know about --urls.
#
# ASPNETCORE_ENVIRONMENT is set for a different reason. --no-launch-profile means
# launchSettings.json is ignored, and without it ASP.NET Core defaults to
# Production. HotChocolate 16 answers introspection only in Development, so the
# Postman collection's introspection request would fail with HC0046.
ASPNETCORE_URLS="$BASE_URL" ASPNETCORE_ENVIRONMENT=Development dotnet run \
    --project "$API_PROJECT" -c Release --no-build --no-launch-profile \
    > "$API_LOG" 2>&1 &
API_PID=$!

health_deadline=$(( $(date +%s) + STARTUP_TIMEOUT_SECONDS ))
healthy=0
while [ "$(date +%s)" -lt "$health_deadline" ]; do
    if ! kill -0 "$API_PID" 2>/dev/null; then
        step_fail 'start api' "The service exited during start-up.

$(log_tail)"
    fi

    if curl -fsS -o /dev/null --max-time 5 "$BASE_URL/health" 2>/dev/null; then
        healthy=1
        break
    fi

    sleep 0.5
done

if [ "$healthy" -ne 1 ]; then
    step_fail 'start api' "$BASE_URL/health did not answer within $STARTUP_TIMEOUT_SECONDS seconds.

$(log_tail)"
fi
step_ok "api answering on $BASE_URL/health"

# The query holds no quotes and no backslashes, so this is a safe way to build
# the request body without reaching for a JSON encoder.
printf '{"query":"%s"}' "$VERIFY_QUERY" > "$TEMP_DIR/request.json"

HTTP_STATUS="$(curl -sS -o "$TEMP_DIR/response.json" -w '%{http_code}' \
    --max-time 120 \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json' \
    --data-binary "@$TEMP_DIR/request.json" \
    "$BASE_URL/graphql")"

if [ "$HTTP_STATUS" != "200" ]; then
    step_fail 'graphql query' "POST $BASE_URL/graphql answered $HTTP_STATUS.

$(cat "$TEMP_DIR/response.json" 2>/dev/null)"
fi

RESPONSE_SUMMARY="$(summarise_response "$TEMP_DIR/response.json")"
if [ $? -ne 0 ] || [ -z "$RESPONSE_SUMMARY" ]; then
    step_fail 'graphql query' "Could not read the response as JSON:

$(cat "$TEMP_DIR/response.json" 2>/dev/null)"
fi

read -r has_errors product_count review_count <<< "$RESPONSE_SUMMARY"

if [ "$has_errors" -ne 0 ]; then
    step_fail 'graphql query' "The response carries an errors key. The query is supposed to succeed outright.

$(cat "$TEMP_DIR/response.json")"
fi

if [ "$product_count" -ne "$EXPECTED_PRODUCT_COUNT" ]; then
    step_fail 'product count' "Expected $EXPECTED_PRODUCT_COUNT products, got $product_count."
fi

if [ "$review_count" -ne "$EXPECTED_REVIEW_COUNT" ]; then
    step_fail 'review count' "Expected $EXPECTED_REVIEW_COUNT reviews across all products, got $review_count."
fi
step_ok "query returned $EXPECTED_PRODUCT_COUNT products and $EXPECTED_REVIEW_COUNT reviews"

# The same query, four more times, because one sample is not enough to assert a
# batching number against.
#
# A DataLoader batch is dispatched when the coordinator has seen it untouched
# for the settle time across two evaluation rounds. Almost always the 120 author
# resolvers all enqueue their keys inside that window and the batch goes once.
# Occasionally - measured at two requests in four hundred on this machine - they
# do not, the batch is dispatched with what it has, and the stragglers form a
# second one. The answers are identical; the statement count is one higher.
#
# So the assertions below are: at least one of the five runs hit the batched
# number exactly, and none of them exceeded it by more than a single split
# batch. A service whose DataLoaders had been removed could satisfy neither.
run=1
while [ "$run" -lt "$VERIFY_QUERY_RUNS" ]; do
    repeat_status="$(curl -sS -o "$TEMP_DIR/repeat.json" -w '%{http_code}' \
        --max-time 120 \
        -H 'Content-Type: application/json' \
        -H 'Accept: application/json' \
        --data-binary "@$TEMP_DIR/request.json" \
        "$BASE_URL/graphql")"

    if [ "$repeat_status" != "200" ]; then
        step_fail 'graphql query' "Run $((run + 1)) of the query answered $repeat_status.

$(cat "$TEMP_DIR/repeat.json" 2>/dev/null)"
    fi

    run=$((run + 1))
done

# -- 6. the lookup count ----------------------------------------------------

# The middleware logs the total after the response has been written, so the line
# can land a moment after the HTTP call returns.
waited=0
logged_counts=""
while [ "$waited" -lt 60 ]; do
    logged_counts="$(grep -o 'Service lookups this request: [0-9][0-9]*' "$API_LOG" 2>/dev/null \
        | sed 's/.*: //' | tr '\n' ' ')"
    if [ -n "$logged_counts" ]; then
        break
    fi
    sleep 0.25
    waited=$((waited + 1))
done

if [ -z "$logged_counts" ]; then
    step_fail 'lookup count' "The service never logged a lookup count for the query.
Either the counting middleware is gone or the log level hides it.

$(log_tail)"
fi

case " $logged_counts " in
    *" $EXPECTED_LOOKUP_COUNT "*)
        step_ok "service logged 'Service lookups this request: $EXPECTED_LOOKUP_COUNT'"
        ;;
    *)
        step_fail 'lookup count' "Expected the service to log 'Service lookups this request: $EXPECTED_LOOKUP_COUNT'.
It logged: $logged_counts

That number is quoted in the book: one lookup for the product list, one for
every review on it, and one for their authors. It was 146 through chapters 2
and 3 and at tag ch04-ef. If it moved, either a DataLoader stopped batching or
a resolver went back to asking a service directly."
        ;;
esac

lookup_ceiling=$((EXPECTED_LOOKUP_COUNT + SPLIT_BATCH_ALLOWANCE))
for count in $logged_counts; do
    if [ "$count" -gt "$lookup_ceiling" ]; then
        step_fail 'lookup count' "One of the runs asked for more than $lookup_ceiling lookups: $logged_counts

A single split batch costs one extra lookup and is expected now and again. More
than that is a resolver that is not going through a DataLoader at all."
    fi
done

# -- 6b. the request pipeline -----------------------------------------------

# The pipeline is logged once, while the schema is being built, so by the time a
# query has been answered these lines are already there.
LOGGED_PIPELINE="$(sed -n 's/^ *[0-9][0-9]*\. \([^ ][^ ]*\) *$/\1/p' "$API_LOG" 2>/dev/null)"

if [ -z "$LOGGED_PIPELINE" ]; then
    step_fail 'request pipeline' "The service never logged its request pipeline.
AddPipelineReport() is what writes it; check it is still registered in
Program.cs, and registered after AddGraphQL().

$(log_tail)"
fi

if [ "$LOGGED_PIPELINE" != "$EXPECTED_PIPELINE" ]; then
    step_fail 'request pipeline' "The request pipeline is not the one chapter 3 prints.

Expected:
$EXPECTED_PIPELINE

Found:
$LOGGED_PIPELINE

The order is the spine of chapter 3. Do not reorder it to make this pass; work
out what moved and why."
fi
step_ok 'request pipeline is the expected 13 middleware, in order'

# -- 6c. the request timeline -----------------------------------------------

waited=0
logged_resolvers=""
while [ "$waited" -lt 60 ]; do
    logged_resolvers="$(grep -o '[0-9][0-9]* resolvers,' "$API_LOG" 2>/dev/null \
        | sed 's/ resolvers,//' | tr '\n' ' ')"
    if [ -n "$logged_resolvers" ]; then
        break
    fi
    sleep 0.25
    waited=$((waited + 1))
done

if [ -z "$logged_resolvers" ]; then
    step_fail 'request timeline' "The service never logged a request timeline.
RequestTimelineListener is what writes it. It is registered through
AddDiagnosticEventListener, and it needs AddApplicationService<ILoggerFactory>()
alongside it or the schema will not build at all.

$(log_tail)"
fi

case " $logged_resolvers " in
    *" $EXPECTED_RESOLVER_COUNT "*)
        step_ok "request timeline reported $EXPECTED_RESOLVER_COUNT resolvers"
        ;;
    *)
        step_fail 'request timeline' "Expected the timeline to report $EXPECTED_RESOLVER_COUNT resolvers for the query.
It reported: $logged_resolvers

Chapter 3 makes a point of this matching the lookup count exactly: every
resolver the engine runs does one domain-service lookup, and the plain record
properties are not resolvers at all."
        ;;
esac

# -- 6d. the database round trips -------------------------------------------

logged_sql="$(grep -o '[0-9][0-9]* SQL)' "$API_LOG" 2>/dev/null | sed 's/ SQL)//' | tr '\n' ' ')"

if [ -z "$logged_sql" ]; then
    step_fail 'sql command count' "The timeline never reported a SQL command count.
SqlCommandCounter is the EF Core interceptor that produces it, and it is
attached to the pooled context factory in AddMosaicDatabase.

$(log_tail)"
fi

case " $logged_sql " in
    *" $EXPECTED_SQL_COMMAND_COUNT "*)
        step_ok "request timeline reported $EXPECTED_SQL_COMMAND_COUNT SQL commands"
        ;;
    *)
        step_fail 'sql command count' "Expected the timeline to report $EXPECTED_SQL_COMMAND_COUNT SQL commands for the query.
It reported: $logged_sql

This is the number chapter 4 is about. If it went up, something started querying
per row. If it went down, something started batching, and the chapter that
claims otherwise needs rewriting."
        ;;
esac

sql_ceiling=$((EXPECTED_SQL_COMMAND_COUNT + SPLIT_BATCH_ALLOWANCE))
for count in $logged_sql; do
    if [ "$count" -gt "$sql_ceiling" ]; then
        step_fail 'sql command count' "One of the runs sent more than $sql_ceiling statements: $logged_sql

A single split batch costs one extra statement and is expected now and again.
More than that is an N+1 growing back."
    fi
done

# -- 7. the postman collection ----------------------------------------------

NEWMAN_BIN=""
NEWMAN_VIA_NPX=0
if [ -x "$REPO_ROOT/node_modules/.bin/newman" ]; then
    NEWMAN_BIN="$REPO_ROOT/node_modules/.bin/newman"
elif command -v npx >/dev/null 2>&1 && npx --no newman --version >/dev/null 2>&1; then
    # --no means "use what is already installed, do not download anything".
    NEWMAN_BIN="npx"
    NEWMAN_VIA_NPX=1
fi

if [ ! -f "$POSTMAN_COLLECTION" ] || [ ! -f "$POSTMAN_ENVIRONMENT" ]; then
    step_skip 'postman' 'the collection or its environment is not in postman/ yet'
elif [ -z "$NEWMAN_BIN" ]; then
    step_skip 'postman' 'newman is not installed - run npm install first'
else
    # baseUrl is overridden rather than trusted: the environment file says 5100,
    # and this script can be pointed at another port.
    if [ "$NEWMAN_VIA_NPX" -eq 1 ]; then
        "$NEWMAN_BIN" --no newman run "$POSTMAN_COLLECTION" \
            --environment "$POSTMAN_ENVIRONMENT" \
            --env-var "baseUrl=$BASE_URL" \
            --bail
    else
        "$NEWMAN_BIN" run "$POSTMAN_COLLECTION" \
            --environment "$POSTMAN_ENVIRONMENT" \
            --env-var "baseUrl=$BASE_URL" \
            --bail
    fi
    if [ $? -ne 0 ]; then
        step_fail 'postman' 'newman failed; its output above says which request failed.'
    fi
    step_ok 'postman collection'
fi

# -- 8. the EXIT trap stops the service and prints the summary --------------

exit 0
