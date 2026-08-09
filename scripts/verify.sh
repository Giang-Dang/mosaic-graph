#!/usr/bin/env bash
#
# Verifies the Mosaic sample service end to end.
#
# This is the gate that has to pass before a chapter tag is cut, and it is the
# script a reader runs to check that the code in the book still does what the
# book says it does. It starts the database, builds the solution, checks the
# committed schema snapshots against freshly exported ones, starts the services,
# runs the chapter's query and asserts the numbers the chapter quotes.
#
# Since chapter 4 this needs Docker: Mosaic's data lives in PostgreSQL and the
# script brings the container up itself. Set MOSAIC_KEEP_DATABASE=1 to leave it
# running afterwards, which is worth doing while iterating.
#
# Since chapter 5 the run starts by dropping Mosaic's schema and reseeding it.
# The Postman collection submits a review, so a run leaves the database changed,
# and a gate whose result depends on how many times it has been run is not a
# gate. Do not point this at a database holding anything you want.
#
# Since chapter 7 it also verifies the federated-wire sample: it composes the
# two subgraph schemas with wgc, starts both subgraphs and the Cosmo Router,
# runs a second Postman collection against all three, and checks that the
# requests the router sent are the ones the chapter prints. Set
# MOSAIC_SKIP_WIRE=1 to leave that out.
#
# Since chapter 8 there are two services rather than one. Catalog left Mosaic
# and both halves are Apollo Federation subgraphs now, so this script starts
# both, checks both committed schemas against a fresh export and against what
# each service publishes through _service, and asserts the numbers chapter 8
# quotes for _entities. It also composes the two with wgc. Chapter 8 shows no
# composition at all - that is chapter 9's subject - but a pair of subgraphs
# that has quietly stopped composing is exactly the kind of breakage a gate
# exists to catch.
#
# Since chapter 10 there is a router in front of those two services. This
# script starts it from docker-compose.yml, runs a third Postman collection
# against it - the storefront query that no single service can answer, and the
# plan the router made to answer it - and then runs scripts/router-cases.mjs,
# which reproduces the three router behaviours chapter 10 calls surprising. Set
# MOSAIC_SKIP_ROUTER=1 to leave that out.
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

# The Catalog subgraph, extracted in chapter 8. This is not MOSAIC_CATALOG_PORT
# below: that one is chapter 7's sample catalog, a different service on 5201.
CATALOG_SUBGRAPH_PORT="${MOSAIC_CATALOG_SUBGRAPH_PORT:-5101}"

STARTUP_TIMEOUT_SECONDS="${MOSAIC_STARTUP_TIMEOUT:-60}"
DATABASE_TIMEOUT_SECONDS="${MOSAIC_DATABASE_TIMEOUT:-90}"
KEEP_DATABASE="${MOSAIC_KEEP_DATABASE:-0}"

# Chapter 7's federated-wire sample: two subgraphs on the host and the Cosmo
# Router in a container. All three match the launch profiles, the routing URLs
# in samples/federated-wire/graph.yaml, and docker-compose.yml.
CATALOG_PORT="${MOSAIC_CATALOG_PORT:-5201}"
REVIEWS_PORT="${MOSAIC_REVIEWS_PORT:-5202}"
ROUTER_PORT="${MOSAIC_ROUTER_PORT:-3002}"

# Set MOSAIC_SKIP_WIRE=1 to leave that section out. It is the slowest part of a
# run and one of the two that pull a container image from a registry.
SKIP_WIRE="${MOSAIC_SKIP_WIRE:-0}"

# Chapter 10's router uses the same port and the same image, and is skipped
# with MOSAIC_SKIP_ROUTER=1. It starts five containers of its own across the
# collection and the three router cases.
SKIP_ROUTER="${MOSAIC_SKIP_ROUTER:-0}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

SOLUTION="$REPO_ROOT/Mosaic.slnx"
API_PROJECT="$REPO_ROOT/src/Mosaic.Api/Mosaic.Api.csproj"
COMMITTED_SCHEMA="$REPO_ROOT/schema/mosaic.graphql"
SAMPLES_DIR="$REPO_ROOT/samples/three-approaches"
SAMPLE_SCHEMA_DIR="$REPO_ROOT/schema/samples"
POSTMAN_COLLECTION="$REPO_ROOT/postman/mosaic-federation.postman_collection.json"
POSTMAN_ENVIRONMENT="$REPO_ROOT/postman/mosaic-federation.local.postman_environment.json"
BASE_URL="http://localhost:$PORT"

# -- chapter 8's second subgraph --------------------------------------------

CATALOG_PROJECT="$REPO_ROOT/src/Mosaic.Catalog/Mosaic.Catalog.csproj"
CATALOG_SCHEMA="$REPO_ROOT/schema/catalog.graphql"
CATALOG_SUBGRAPH_URL="http://localhost:$CATALOG_SUBGRAPH_PORT"
FEDERATION_GRAPH="$REPO_ROOT/federation/mosaic.yaml"

# -- chapter 9's composition ------------------------------------------------

# The composed router execution config is committed, unlike chapter 7's, which
# is a build artifact and gitignored. The difference is that chapter 9 prints
# what is inside this one, so it has to be a file a reader can open and the gate
# has to notice when a fresh compose stops matching it.
FEDERATION_SUPERGRAPH="$REPO_ROOT/federation/supergraph.json"
COMPOSITION_CASES="$REPO_ROOT/scripts/composition-cases.mjs"

# -- chapter 10's router -----------------------------------------------------

# The router runs from docker-compose.yml like any other service and mounts two
# files: the graph chapter 9 composed, and its own configuration. It publishes
# the same port as chapter 7's wire-router, which is safe only because the two
# sections start and stop in sequence.
ROUTER_CONFIG="$REPO_ROOT/router/config.yaml"
ROUTER_POSTMAN="$REPO_ROOT/postman/mosaic-router.postman_collection.json"
ROUTER_POSTMAN_ENV="$REPO_ROOT/postman/mosaic-router.local.postman_environment.json"
ROUTER_CASES="$REPO_ROOT/scripts/router-cases.mjs"

# -- chapter 11's entity resolution ------------------------------------------

# Both of these run inside the router section, because @requires and @provides
# are only visible once something is planning across two services. The cases
# script starts the sample under samples/entity-resolution on ports of its own,
# so nothing here has to know about it.
ENTITIES_POSTMAN="$REPO_ROOT/postman/mosaic-entities.postman_collection.json"
ENTITIES_POSTMAN_ENV="$REPO_ROOT/postman/mosaic-entities.local.postman_environment.json"
ENTITY_CASES="$REPO_ROOT/scripts/entity-cases.mjs"

# The two subgraphs since chapter 8, written as <name>:<port>. Both files under
# schema/ are what `_service { sdl }` returns, which is what a composer reads,
# so checking them is a check on the federated contract and not only on the SDL.
# Each subgraph is a separate contract with the composer, so a drift in either
# is a drift.
SUBGRAPHS="mosaic:$PORT
catalog:$CATALOG_SUBGRAPH_PORT"

# Which project produces each subgraph and which committed file it has to keep
# matching. Looked up by name rather than carried as two more columns in the
# list above, because a path can hold a colon and that list cannot.
subgraph_project() {
    case "$1" in
        mosaic) printf '%s' "$API_PROJECT" ;;
        *)      printf '%s' "$CATALOG_PROJECT" ;;
    esac
}

subgraph_schema() {
    case "$1" in
        mosaic) printf '%s' "$COMMITTED_SCHEMA" ;;
        *)      printf '%s' "$CATALOG_SCHEMA" ;;
    esac
}

# The three sample projects, in the order the chapter introduces them, written as
# <name committed under schema/samples>:<folder under samples/three-approaches>.
# All three describe the same schema three different ways, so all three must
# export exactly the same SDL.
SAMPLE_APPROACHES="implementation-first:Mosaic.Sample.ImplementationFirst
code-first:Mosaic.Sample.CodeFirst
schema-first:Mosaic.Sample.SchemaFirst"

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
CATALOG_QUERY='{ products { id title } }'
EXPECTED_PRODUCT_COUNT=25

# The Mosaic half is the same nested selection, reached the way a router would
# reach it: one _entities call carrying every product key Catalog just handed
# over. first: 12 is not arbitrary - the most reviewed product has exactly 12,
# so this still asks for every review in the seed data and the total is still
# 120.
MOSAIC_ENTITIES_QUERY='query($representations: [_Any!]!) { _entities(representations: $representations) { ... on Product { reviews(first: 12) { nodes { rating author { id displayName } } } } } }'
EXPECTED_REVIEW_COUNT=120

# Catalog's reference resolver sits behind the same DataLoader Product.node
# uses, so a batch of any size costs one statement. This is the assertion that
# would catch a subgraph resolving representations one at a time, which no
# assertion on the answer could see.
CATALOG_ENTITIES_QUERY='query($representations: [_Any!]!) { _entities(representations: $representations) { ... on Product { title sku } } }'

# The request pipeline HotChocolate assembles for this service, in order. Twelve
# of these come from the default pipeline; CostAnalyzerMiddleware is inserted
# after DocumentValidationMiddleware by the cost analyzer that AddGraphQL turns
# on unless default security is disabled. Chapter 3 prints this list, so a
# change here is a change to the chapter.
#
# AddApolloFederation() did not touch it. Chapter 8 asserts that in prose, so
# this list staying at thirteen is part of chapter 8's evidence as well as
# chapter 3's.
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

# The resolver count did not move, and that is the point. It was one root field
# plus 25 review connections plus 120 authors; it is now one _entities field
# plus 25 plus 120. Same shape, same number, different first term. Plain record
# properties - title, rating, displayName - are not resolvers and are not
# counted.
EXPECTED_RESOLVER_COUNT=146

# The statement count did move, by exactly one. As a monolith this query cost
# three: the products, their reviews, and the twelve distinct authors. Mosaic no
# longer fetches the products, so it costs two, and the one that left is the
# statement Catalog runs instead.
EXPECTED_SQL_COMMAND_COUNT=2

# The lookup counter follows the statement count for the same reason: Mosaic
# asks its own domains two questions, the reviews batch and the authors batch,
# and no longer asks Catalog anything because it cannot.
EXPECTED_LOOKUP_COUNT=2

# How many times the _entities query is sent, and how far above the expected
# number a single run is allowed to land. See the comment beside the repeat
# loop: one split batch costs one extra statement and one extra lookup.
VERIFY_QUERY_RUNS=5
SPLIT_BATCH_ALLOWANCE=1

WIRE_DIR="$REPO_ROOT/samples/federated-wire"
WIRE_GRAPH="$WIRE_DIR/graph.yaml"
WIRE_SUPERGRAPH="$WIRE_DIR/supergraph.json"
WIRE_POSTMAN="$REPO_ROOT/postman/federated-wire.postman_collection.json"
WIRE_POSTMAN_ENV="$REPO_ROOT/postman/federated-wire.local.postman_environment.json"
CATALOG_URL="http://localhost:$CATALOG_PORT"
REVIEWS_URL="http://localhost:$REVIEWS_PORT"
ROUTER_URL="http://localhost:$ROUTER_PORT"

# The two subgraphs, written as <name>:<project folder>:<port>. The committed
# schema for each is schema/samples/wire-<name>.graphql, and it is what
# `_service { sdl }` returns rather than what `schema export` writes: that
# field is the composer's input, so it is the one worth asserting.
WIRE_SUBGRAPHS="catalog:Mosaic.Sample.Wire.Catalog:$CATALOG_PORT
reviews:Mosaic.Sample.Wire.Reviews:$REVIEWS_PORT"

# What chapter 7 prints, asserted against what the subgraphs actually received.
# These are the two request bodies the router sent while the Postman collection
# ran, quoted exactly as the chapter quotes them. A change to the router's
# planning shows up here as a failed gate rather than as a stale listing.
EXPECTED_CATALOG_FETCH='{"query":"{products {title price __typename id}}"}'
EXPECTED_REVIEWS_FETCH='{"variables":{"representations":[{"__typename":"Product","id":"1"},{"__typename":"Product","id":"2"},{"__typename":"Product","id":"3"}]},"query":"query($representations: [_Any!]!){_entities(representations: $representations){... on Product {__typename reviews {rating body}}}}"}'

API_PID=""
CATALOG_SUBGRAPH_PID=""
TEMP_DIR=""
API_LOG=""
CATALOG_LOG=""
SUMMARY=""
JSON_TOOL=""
STARTED_DATABASE=0
CATALOG_PID=""
REVIEWS_PID=""
STARTED_ROUTER=0
STARTED_MOSAIC_ROUTER=0
WGC_BIN=""
WGC_VIA_NPX=0

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

# Prints the failure, records it, and exits. The EXIT trap stops the services
# and prints the summary; nothing after a failed step is worth running.
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

# wc pads its count with spaces on some platforms, hence the tr.
count_lines() {
    wc -l < "$1" | tr -d ' '
}

# True only when something answers on the port. curl exits 7 when the connection
# is refused and 28 when it times out; neither is a running service. Timeouts
# count as free deliberately, because some machines drop the connection instead
# of refusing it, and reading that as "port taken" would block every run. The
# cost of being wrong is small: the service then fails to bind, and the health
# poll below reports that with the service's own error in the message.
port_taken() {
    local rc
    curl -s -o /dev/null --connect-timeout 1 --max-time 2 "http://127.0.0.1:${1}/" >/dev/null 2>&1
    rc=$?
    case "$rc" in
        7 | 28) return 1 ;;
        *) return 0 ;;
    esac
}

# ---------------------------------------------------------------------------
# Reading the answers
#
# jq is the obvious tool; python3 is there for the machine that does not have
# it. Every function below has one branch for each, and they print the same
# thing.
# ---------------------------------------------------------------------------

# Prints 1 when the response carries an errors key, 0 when it does not.
response_has_errors() {
    if [ "$JSON_TOOL" = "jq" ]; then
        jq -r 'if has("errors") then 1 else 0 end' "$1"
        return $?
    fi

    python3 - "$1" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    payload = json.load(handle)

print(1 if "errors" in payload else 0)
PY
}

# Prints one field of every product in a Catalog `{ products { id title } }`
# answer, one per line, in the order Catalog returned them. $1 the response file,
# $2 the field.
product_field() {
    if [ "$JSON_TOOL" = "jq" ]; then
        jq -r --arg field "$2" '(.data.products // [])[] | (.[$field] // "")' "$1"
        return $?
    fi

    python3 - "$1" "$2" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    payload = json.load(handle)

for product in (payload.get("data") or {}).get("products") or []:
    print(product.get(sys.argv[2]) or "")
PY
}

# Builds the request body for an _entities call out of a Catalog products
# answer: $1 the answer, $2 the query to send. Every key goes back exactly as
# Catalog gave it, which is the whole point - re-encoding one here would test
# this script's idea of the format rather than the two services' agreement about
# it, and the agreement is the only thing that matters.
entities_request() {
    if [ "$JSON_TOOL" = "jq" ]; then
        jq -c --arg query "$2" '{
            query: $query,
            variables: {
                representations: [
                    (.data.products // [])[] | { __typename: "Product", id: .id }
                ]
            }
        }' "$1"
        return $?
    fi

    python3 - "$1" "$2" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    payload = json.load(handle)

products = (payload.get("data") or {}).get("products") or []
representations = [
    {"__typename": "Product", "id": product["id"]} for product in products]

sys.stdout.write(json.dumps(
    {"query": sys.argv[2], "variables": {"representations": representations}}))
PY
}

# Prints "<has_errors> <entity_count> <null_count> <review_count>", tab
# separated, from any _entities answer. A null entity is a legal answer - the
# specification makes [_Entity] nullable - so the nulls are counted rather than
# tripped over.
summarise_entities() {
    if [ "$JSON_TOOL" = "jq" ]; then
        jq -r '[
            (if has("errors") then 1 else 0 end),
            ((.data._entities // []) | length),
            ([ (.data._entities // [])[] | select(. == null) ] | length),
            ([ (.data._entities // [])[] | (.reviews.nodes // []) | length ] | add // 0)
        ] | @tsv' "$1"
        return $?
    fi

    python3 - "$1" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    payload = json.load(handle)

entities = (payload.get("data") or {}).get("_entities") or []
nulls = sum(1 for entity in entities if entity is None)
reviews = sum(
    len(((entity or {}).get("reviews") or {}).get("nodes") or []) for entity in entities)

print("%d\t%d\t%d\t%d" % (
    1 if "errors" in payload else 0, len(entities), nulls, reviews))
PY
}

# Prints the title of every entity in a Catalog _entities answer, one per line,
# with (null) where the entity is null. The list is positional and is compared
# line by line against the titles Catalog gave for the same representations.
entity_titles() {
    if [ "$JSON_TOOL" = "jq" ]; then
        jq -r '(.data._entities // [])[] | if . == null then "(null)" else (.title // "") end' "$1"
        return $?
    fi

    python3 - "$1" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    payload = json.load(handle)

for entity in (payload.get("data") or {}).get("_entities") or []:
    print("(null)" if entity is None else (entity.get("title") or ""))
PY
}

# Prints every distinct customer key carried by a review author in a Mosaic
# _entities answer, one per line. Sorted, so the same run picks the same
# customer twice; which one it picks does not matter.
author_keys() {
    if [ "$JSON_TOOL" = "jq" ]; then
        jq -r '[
            (.data._entities // [])[] | (.reviews.nodes // [])[] | .author.id | select(. != null)
        ] | unique[]' "$1"
        return $?
    fi

    python3 - "$1" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    payload = json.load(handle)

keys = set()
for entity in (payload.get("data") or {}).get("_entities") or []:
    for node in ((entity or {}).get("reviews") or {}).get("nodes") or []:
        author = node.get("author") or {}
        if author.get("id"):
            keys.add(author["id"])

for key in sorted(keys):
    print(key)
PY
}

# Prints "<order_count> <line_count> <first_line_product_key>", tab separated,
# from an ordersByCustomer answer.
summarise_orders() {
    if [ "$JSON_TOOL" = "jq" ]; then
        jq -r '[
            ((.data.ordersByCustomer // []) | length),
            ([ (.data.ordersByCustomer // [])[] | (.lines // []) | length ] | add // 0),
            (([ (.data.ordersByCustomer // [])[] | (.lines // [])[] | .product.id ] | .[0]) // "")
        ] | @tsv' "$1"
        return $?
    fi

    python3 - "$1" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    payload = json.load(handle)

orders = (payload.get("data") or {}).get("ordersByCustomer") or []
lines = [line for order in orders for line in (order.get("lines") or [])]
first_key = (lines[0].get("product") or {}).get("id") or "" if lines else ""

print("%s\t%s\t%s" % (len(orders), len(lines), first_key))
PY
}

# Writes the SDL a subgraph publishes through _service to stdout.
published_sdl() {
    if [ "$JSON_TOOL" = "jq" ]; then
        jq -r '.data._service.sdl' "$1"
        return $?
    fi

    python3 -c 'import json,sys; sys.stdout.write(json.load(open(sys.argv[1], encoding="utf-8"))["data"]["_service"]["sdl"])' "$1"
}

# Posts a request body to a subgraph and saves the answer. $1 the base URL, $2
# the file holding the body, $3 where the answer goes, $4 the step a failure is
# reported under. Every query sent through here is supposed to succeed outright,
# so a non-200 or an errors key ends the run.
post_graphql() {
    local status
    local has_errors

    status="$(curl -sS -o "$3" -w '%{http_code}' \
        --max-time 120 \
        -H 'Content-Type: application/json' \
        -H 'Accept: application/json' \
        --data-binary "@$2" \
        "$1/graphql")"

    if [ "$status" != "200" ]; then
        step_fail "$4" "POST $1/graphql answered $status.

$(cat "$3" 2>/dev/null)"
    fi

    has_errors="$(response_has_errors "$3" 2>/dev/null)"
    if [ -z "$has_errors" ]; then
        step_fail "$4" "Could not read the response as JSON:

$(cat "$3" 2>/dev/null)"
    fi

    if [ "$has_errors" != "0" ]; then
        step_fail "$4" "The response carries an errors key. This query is supposed to succeed outright.

$(cat "$3" 2>/dev/null)"
    fi
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

# Stops one `dotnet run` and everything it started. $1 is the process id, $2 the
# port it was listening on, or empty when nothing needs to wait for that port.
stop_service() {
    local pid="$1"
    local port="$2"
    local waited

    if [ -z "$pid" ]; then
        return 0
    fi
    if ! kill -0 "$pid" 2>/dev/null; then
        return 0
    fi

    # dotnet run launches the application as a child process, so the children go
    # first. Killing only the process we started leaves the app holding the port.
    if command -v pkill >/dev/null 2>&1; then
        pkill -TERM -P "$pid" >/dev/null 2>&1 || true
    fi
    kill -TERM "$pid" >/dev/null 2>&1 || true

    waited=0
    while [ "$waited" -lt 100 ] && kill -0 "$pid" 2>/dev/null; do
        sleep 0.1
        waited=$((waited + 1))
    done

    if kill -0 "$pid" 2>/dev/null; then
        if command -v pkill >/dev/null 2>&1; then
            pkill -KILL -P "$pid" >/dev/null 2>&1 || true
        fi
        kill -KILL "$pid" >/dev/null 2>&1 || true
    fi

    wait "$pid" 2>/dev/null || true

    if [ -z "$port" ]; then
        return 0
    fi

    # The port has to be free when we leave, whatever happened above.
    waited=0
    while [ "$waited" -lt 40 ] && port_taken "$port"; do
        sleep 0.25
        waited=$((waited + 1))
    done
    if port_taken "$port"; then
        printf 'Warning: something is still listening on port %s after the service was stopped.\n' "$port" >&2
    fi
}

stop_api() {
    stop_service "$API_PID" "$PORT"
    API_PID=""
}

# Chapter 8's second service, stopped the same way and on its own port. Two
# services now, so two ports to give back.
stop_catalog() {
    stop_service "$CATALOG_SUBGRAPH_PID" "$CATALOG_SUBGRAPH_PORT"
    CATALOG_SUBGRAPH_PID=""
}

# The chapter 7 subgraphs are plain `dotnet run` children like the two services
# above, and go the same way.
stop_wire_subgraph() {
    stop_service "$1" ""
}

cleanup() {
    status=$?

    stop_api
    stop_catalog
    stop_wire_subgraph "$CATALOG_PID"
    stop_wire_subgraph "$REVIEWS_PID"
    CATALOG_PID=""
    REVIEWS_PID=""

    # down rather than stop: the container holds a bind mount on a file this
    # script recomposes on every run, and a stopped container keeps it.
    if [ "$STARTED_ROUTER" -eq 1 ]; then
        docker compose --project-directory "$REPO_ROOT" --profile wire down wire-router \
            >/dev/null 2>&1 || true
    fi

    # Normally already down: the router section takes its own container away so
    # that the wire section can have the port. This catches the run that failed
    # somewhere in between.
    if [ "$STARTED_MOSAIC_ROUTER" -eq 1 ]; then
        docker compose --project-directory "$REPO_ROOT" down mosaic-router \
            >/dev/null 2>&1 || true
    fi

    if [ -n "$TEMP_DIR" ] && [ -d "$TEMP_DIR" ]; then
        rm -rf "$TEMP_DIR"
    fi

    # The container is stopped, not removed, and its volume is left in place.
    # Not that the data in it survived: since chapter 5 this script starts the
    # service with MOSAIC_RESET_DATABASE set, so the run began by dropping the
    # schema. Keeping the volume only saves the container from initialising
    # itself again.
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
CATALOG_LOG="$TEMP_DIR/catalog.log"

# -- 3. schema drift --------------------------------------------------------

# Two schemas since chapter 8, checked the same way.
for entry in $SUBGRAPHS; do
    name="${entry%%:*}"
    project="$(subgraph_project "$name")"
    committed="$(subgraph_schema "$name")"
    exported="$TEMP_DIR/$name.exported.graphql"

    dotnet run --project "$project" -c Release --no-build --no-launch-profile -- \
        schema export --output "$exported"
    if [ $? -ne 0 ]; then
        step_fail "schema export ($name)" 'dotnet run -- schema export failed; its output above says why.'
    fi
    if [ ! -f "$exported" ]; then
        step_fail "schema export ($name)" "The exporter reported success but wrote nothing to $exported."
    fi
    if [ ! -f "$committed" ]; then
        step_fail "schema drift ($name)" "There is no committed snapshot at $committed."
    fi

    if ! same_text "$committed" "$exported"; then
        step_fail "schema drift ($name)" "The exported schema is not the one committed in ${committed#"$REPO_ROOT/"}.

If the change is deliberate, regenerate the snapshot and commit it:

    dotnet run --project ${project#"$REPO_ROOT/"} -- schema export --output ${committed#"$REPO_ROOT/"}

If it is not, a dependency changed the schema behind your back. That is what
this check exists to catch. Since chapter 8 it also catches a change to one
subgraph that would break composition with the other.

$(schema_diff "$committed" "$exported" "$name")"
    fi
done
step_ok 'both subgraph schemas match the committed snapshots'

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

# -- 4b. the entity-attribute-placement sample ------------------------------

# Chapter 8 prints this sample's SDL as the evidence that [ReferenceResolver]
# inside an [ObjectType<T>] class becomes an ordinary field. The leaked field
# is the whole finding, so a HotChocolate release that stopped leaking it would
# make the chapter wrong, and this is where that would show up.
PLACEMENT_PROJECT="$REPO_ROOT/samples/entity-attribute-placement"
PLACEMENT_SCHEMA="$SAMPLE_SCHEMA_DIR/entity-attribute-placement.graphql"

if [ ! -d "$PLACEMENT_PROJECT" ]; then
    step_skip 'placement sample' 'samples/entity-attribute-placement does not exist yet'
else
    if ! dotnet run --project "$PLACEMENT_PROJECT" -c Release --no-build --no-launch-profile \
            -- schema export --output "$TEMP_DIR/entity-attribute-placement.graphql"; then
        step_fail 'placement sample' 'Exporting the entity-attribute-placement schema failed.'
    fi

    if ! same_text "$PLACEMENT_SCHEMA" "$TEMP_DIR/entity-attribute-placement.graphql"; then
        step_fail 'placement sample' "schema/samples/entity-attribute-placement.graphql is not what the project exports.

$(schema_diff "$PLACEMENT_SCHEMA" "$TEMP_DIR/entity-attribute-placement.graphql" 'placement')"
    fi

    # The leak by name, because that is the claim rather than the file being
    # unchanged. Alpha puts [ReferenceResolver] on the type extension class and
    # Bravo puts it on the record. The newlines are squeezed out so one pattern
    # can span two lines, and the carriage returns go first: the working tree
    # is CRLF on Windows, and a stray \r lands in the middle of the pattern.
    placement_flat="$(tr -d '\r' < "$PLACEMENT_SCHEMA" | tr '\n' ' ' | tr -s ' ')"

    case "$placement_flat" in
        *'type Alpha @key(fields: "id") { resolveByKey'*) ;;
        *)
            step_fail 'placement sample' 'Alpha no longer publishes resolveByKey as a field.
Chapter 8 is built on that leak. If HotChocolate stopped turning a
[ReferenceResolver] method in an [ObjectType<T>] class into an ordinary field,
the chapter needs rewriting rather than this check needing loosening.'
            ;;
    esac

    case "$placement_flat" in
        *'type Bravo @key(fields: "id") { resolveByKey'*)
            step_fail 'placement sample' 'Bravo leaked resolveByKey, which is the placement chapter 8 calls safe.'
            ;;
    esac

    step_ok 'the placement sample still leaks exactly the field chapter 8 prints'
fi

# -- 5. start both subgraphs and ask the chapter's question in halves --------

for entry in $SUBGRAPHS; do
    name="${entry%%:*}"
    subgraph_port="${entry##*:}"
    if port_taken "$subgraph_port"; then
        step_fail "start $name" "Something is already listening on port $subgraph_port.
Stop it first - a stray 'docker compose up', a debugger, or an earlier run of this script."
    fi
done

# The URL goes in through the environment rather than the command line:
# RunWithGraphQLCommands parses the process arguments itself, and it should not
# have to know about --urls.
#
# ASPNETCORE_ENVIRONMENT is set for a different reason. --no-launch-profile means
# launchSettings.json is ignored, and without it ASP.NET Core defaults to
# Production. HotChocolate 16 answers introspection only in Development, so the
# Postman collection's introspection request would fail with HC0046.
#
# MOSAIC_RESET_DATABASE drops the schema and reseeds it before the service takes
# a request. The Postman collection submits a review, so without this the second
# run of this script would find 121 of them and fail an assertion that is not
# wrong. Both services read the same switch and each resets its own database.
ASPNETCORE_URLS="$BASE_URL" ASPNETCORE_ENVIRONMENT=Development MOSAIC_RESET_DATABASE=1 dotnet run \
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

# -- 5b. the Catalog subgraph -----------------------------------------------

# Started after Mosaic rather than beside it, because both of them create and
# seed a database on the way up and doing that one at a time makes a failure
# readable.
ASPNETCORE_URLS="$CATALOG_SUBGRAPH_URL" ASPNETCORE_ENVIRONMENT=Development MOSAIC_RESET_DATABASE=1 dotnet run \
    --project "$CATALOG_PROJECT" -c Release --no-build --no-launch-profile \
    > "$CATALOG_LOG" 2>&1 &
CATALOG_SUBGRAPH_PID=$!

catalog_deadline=$(( $(date +%s) + STARTUP_TIMEOUT_SECONDS ))
catalog_healthy=0
while [ "$(date +%s)" -lt "$catalog_deadline" ]; do
    if ! kill -0 "$CATALOG_SUBGRAPH_PID" 2>/dev/null; then
        step_fail 'start catalog' "The Catalog subgraph exited during start-up.

$(tail -n 40 "$CATALOG_LOG" 2>/dev/null)"
    fi

    if curl -fsS -o /dev/null --max-time 5 "$CATALOG_SUBGRAPH_URL/health" 2>/dev/null; then
        catalog_healthy=1
        break
    fi

    sleep 0.5
done

if [ "$catalog_healthy" -ne 1 ]; then
    step_fail 'start catalog' "$CATALOG_SUBGRAPH_URL/health did not answer within $STARTUP_TIMEOUT_SECONDS seconds.

$(tail -n 40 "$CATALOG_LOG" 2>/dev/null)"
fi
step_ok "catalog answering on $CATALOG_SUBGRAPH_URL/health"

# -- 5c. what each subgraph publishes ---------------------------------------

# Not `schema export` but the field a composer actually reads. The two happen to
# agree in HotChocolate 16.6.0; asserting the one the router ecosystem depends
# on is the assertion worth having.
for entry in $SUBGRAPHS; do
    name="${entry%%:*}"
    subgraph_port="${entry##*:}"
    committed="$(subgraph_schema "$name")"
    published="$TEMP_DIR/$name.published.graphql"

    service_status="$(curl -sS -o "$TEMP_DIR/$name.service.json" -w '%{http_code}' \
        --max-time 30 \
        -H 'Content-Type: application/json' \
        -H 'Accept: application/json' \
        --data-binary '{"query":"{ _service { sdl } }"}' \
        "http://localhost:$subgraph_port/graphql")"

    if [ "$service_status" != "200" ]; then
        step_fail "$name _service" "_service on the $name subgraph answered $service_status.
A subgraph that cannot answer _service is not a subgraph.

$(cat "$TEMP_DIR/$name.service.json" 2>/dev/null)"
    fi

    published_sdl "$TEMP_DIR/$name.service.json" > "$published"

    if ! same_text "$committed" "$published"; then
        step_fail "$name published schema" "What the $name subgraph publishes through _service is not what is
committed in ${committed#"$REPO_ROOT/"}. That file is the composer's input.

$(schema_diff "$committed" "$published" "published-$name")"
    fi

    # grep -F, not grep: the directive is full of characters a regular
    # expression would read as syntax.
    if ! grep -qF '@key(fields: "id")' "$published"; then
        step_fail "$name published schema" "The $name subgraph publishes no @key(fields: \"id\").
A schema printed without its key directives composes into a graph with no
entities in it, which is the failure chapter 7 warned about."
    fi
done
step_ok 'both subgraphs publish the committed schemas through _service'

# -- 5d. the catalog half ---------------------------------------------------

# The query holds no quotes and no backslashes, so this is a safe way to build
# the request body without reaching for a JSON encoder.
printf '{"query":"%s"}' "$CATALOG_QUERY" > "$TEMP_DIR/catalog-request.json"
post_graphql "$CATALOG_SUBGRAPH_URL" "$TEMP_DIR/catalog-request.json" \
    "$TEMP_DIR/catalog-products.json" 'catalog products'

# tr -d '\r' because this script has to run under Git Bash on Windows as well
# as on a real one, and python3 there prints CRLF. A stray carriage return
# inside a key survives into a JSON string literal, where it is an illegal
# control character, and the service answers HC0012 "Invalid JSON document"
# about a request that looks perfectly fine in the log.
product_field "$TEMP_DIR/catalog-products.json" id | tr -d '\r' > "$TEMP_DIR/product-keys.txt"
product_field "$TEMP_DIR/catalog-products.json" title > "$TEMP_DIR/product-titles.txt"
product_count="$(count_lines "$TEMP_DIR/product-keys.txt")"

if [ "$product_count" -ne "$EXPECTED_PRODUCT_COUNT" ]; then
    step_fail 'product count' "Expected $EXPECTED_PRODUCT_COUNT products from Catalog, got $product_count."
fi
step_ok "catalog answered $EXPECTED_PRODUCT_COUNT products"

# -- 5e. the mosaic half, through _entities ---------------------------------

entities_request "$TEMP_DIR/catalog-products.json" "$MOSAIC_ENTITIES_QUERY" \
    > "$TEMP_DIR/mosaic-entities-request.json"
post_graphql "$BASE_URL" "$TEMP_DIR/mosaic-entities-request.json" \
    "$TEMP_DIR/mosaic-entities.json" 'mosaic _entities'

read -r entities_errors entity_count null_entity_count review_count \
    <<< "$(summarise_entities "$TEMP_DIR/mosaic-entities.json")"

if [ "$entity_count" -ne "$EXPECTED_PRODUCT_COUNT" ]; then
    step_fail 'entity count' "Sent $EXPECTED_PRODUCT_COUNT representations and got $entity_count entities back.
The specification is positional: answer n belongs to representation n, so a
subgraph must never reorder or deduplicate the list it was handed."
fi

if [ "$review_count" -ne "$EXPECTED_REVIEW_COUNT" ]; then
    step_fail 'review count' "Expected $EXPECTED_REVIEW_COUNT reviews across all representations, got $review_count.
This is the number chapters 2 to 5 measured through Query.products. The field it
arrives through changed in chapter 8; the answer did not."
fi
step_ok "mosaic answered $EXPECTED_PRODUCT_COUNT representations with $EXPECTED_REVIEW_COUNT reviews"

# A key that is not one of ours: a null entity and no errors key. The raw Guid is
# the interesting case, because that is what this identifier looked like before
# chapter 5 made it a global object identifier.
#
# post_graphql is not used here: it fails on an errors key, and whether there is
# one is exactly what this check is about.
for bad_key in 'not-a-key' 'a0000000-0000-4000-8000-000000000001'; do
    printf '{"query":"%s","variables":{"representations":[{"__typename":"Product","id":"%s"}]}}' \
        "$CATALOG_ENTITIES_QUERY" "$bad_key" > "$TEMP_DIR/bad-key-request.json"

    bad_status="$(curl -sS -o "$TEMP_DIR/bad-key.json" -w '%{http_code}' \
        --max-time 60 \
        -H 'Content-Type: application/json' \
        -H 'Accept: application/json' \
        --data-binary "@$TEMP_DIR/bad-key-request.json" \
        "$CATALOG_SUBGRAPH_URL/graphql")"

    if [ "$bad_status" != "200" ]; then
        step_fail 'undecodable key' "POST $CATALOG_SUBGRAPH_URL/graphql answered $bad_status.

$(cat "$TEMP_DIR/bad-key.json" 2>/dev/null)"
    fi

    read -r bad_errors bad_entity_count bad_null_count bad_review_count \
        <<< "$(summarise_entities "$TEMP_DIR/bad-key.json")"

    if [ "$bad_errors" -ne 0 ]; then
        step_fail 'undecodable key' "A representation carrying the key '$bad_key' produced an errors array.
It is supposed to produce a null entity. The specification makes [_Entity]
nullable for exactly this, and a subgraph that throws instead turns one bad key
into a failed batch.

$(cat "$TEMP_DIR/bad-key.json")"
    fi

    if [ "$bad_entity_count" -ne 1 ] || [ "$bad_null_count" -ne 1 ]; then
        step_fail 'undecodable key' "A representation carrying the key '$bad_key' did not produce exactly one null.

$(cat "$TEMP_DIR/bad-key.json")"
    fi
done
step_ok 'an undecodable key produces a null entity and no error'

# -- 5f. one batch, one statement -------------------------------------------

# Catalog has no request timeline of its own, so this is asserted from the answer
# rather than from a counter: 25 representations in, 25 titles out, in order. The
# statement count behind it is measured in the chapter's research file, not here.
entities_request "$TEMP_DIR/catalog-products.json" "$CATALOG_ENTITIES_QUERY" \
    > "$TEMP_DIR/catalog-entities-request.json"
post_graphql "$CATALOG_SUBGRAPH_URL" "$TEMP_DIR/catalog-entities-request.json" \
    "$TEMP_DIR/catalog-entities.json" 'catalog _entities'

read -r resolved_errors resolved_count resolved_nulls resolved_reviews \
    <<< "$(summarise_entities "$TEMP_DIR/catalog-entities.json")"

if [ "$resolved_count" -ne "$EXPECTED_PRODUCT_COUNT" ]; then
    step_fail 'catalog _entities' "Sent $EXPECTED_PRODUCT_COUNT representations, got $resolved_count back."
fi

entity_titles "$TEMP_DIR/catalog-entities.json" > "$TEMP_DIR/entity-titles.txt"
if ! cmp -s "$TEMP_DIR/product-titles.txt" "$TEMP_DIR/entity-titles.txt"; then
    step_fail 'catalog _entities' "The titles came back in a different order from the representations that
asked for them. The answer is positional and nothing in the response says which
representation an entity belongs to, so an out-of-order reply is a silently
wrong one.

$( ( cd "$TEMP_DIR" && diff -u product-titles.txt entity-titles.txt ) || true )"
fi
step_ok 'catalog resolved every representation, in order'

# -- 5g. the other direction ------------------------------------------------

# An order line hands out a product key Mosaic cannot resolve itself. The total
# is the regression test for the Include that was missing from chapter 4 until
# chapter 8: Order.total throws when the lines are not loaded, and nothing in the
# collection had ever asked for one.
#
# Mosaic has no root field that lists customers, so the keys come out of the
# answer above: every review carries its author. That is worth noticing rather
# than working around. Since chapter 8 every entry into this service starts
# either at one of its two root fields or at a key somebody else is holding.
#
# Seven of the twelve seeded customers have no orders at all, so this walks the
# authors until it finds one who does rather than assuming.
author_keys "$TEMP_DIR/mosaic-entities.json" | tr -d '\r' > "$TEMP_DIR/customer-keys.txt"
customer_key_count="$(count_lines "$TEMP_DIR/customer-keys.txt")"
if [ "$customer_key_count" -lt 1 ]; then
    step_fail 'orders' 'No review carried an author, so there is no customer key to follow.'
fi

customer_key=""
order_count=0
line_count=0
line_product_key=""
while read -r candidate; do
    printf '{"query":"{ ordersByCustomer(customerId: \\"%s\\") { total { amount } lines { quantity product { id } } } }"}' \
        "$candidate" > "$TEMP_DIR/orders-request.json"
    post_graphql "$BASE_URL" "$TEMP_DIR/orders-request.json" "$TEMP_DIR/orders.json" 'orders'

    read -r order_count line_count line_product_key <<< "$(summarise_orders "$TEMP_DIR/orders.json")"
    if [ "$order_count" -gt 0 ]; then
        customer_key="$candidate"
        break
    fi
done < "$TEMP_DIR/customer-keys.txt"

if [ -z "$customer_key" ]; then
    step_fail 'orders' "None of the $customer_key_count customers who wrote a review has an order.
The seed data gives eight orders to seven of the twelve customers, so this means
the orders are not being read rather than that the data is thin."
fi

if [ "$line_count" -lt 1 ]; then
    step_fail 'order lines' "Every order came back with an empty lines array.
OrderLine is a related entity with a shadow key, not an owned type, so
OrderingService has to Include it. It did not, from chapter 4 until chapter 8,
and no request in the collection had ever asked."
fi

if ! grep -qxF "$line_product_key" "$TEMP_DIR/product-keys.txt"; then
    step_fail 'order lines' "An order line answered product key '$line_product_key', which is not one of
the keys Catalog handed out. The two services encode the same identifier the
same way or they do not share an entity at all."
fi
step_ok 'an order line answers a product key Catalog also answers'

# The _entities query, four more times, because one sample is not enough to
# assert a batching number against.
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
        --data-binary "@$TEMP_DIR/mosaic-entities-request.json" \
        "$BASE_URL/graphql")"

    if [ "$repeat_status" != "200" ]; then
        step_fail 'mosaic _entities' "Run $((run + 1)) of the query answered $repeat_status.

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

That number is quoted in the book. It was 146 through chapters 2 and 3 and at
tag ch04-ef, 3 once chapter 4 added DataLoaders, and 2 since chapter 8 took the
product lookup out of this service altogether. If it moved again, either a
DataLoader stopped batching or a resolver went back to asking a service
directly."
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

This is the number chapter 4 is about and chapter 8 moved by one. If it went up,
something started querying per row. If it went down, something started batching,
and the chapter that claims otherwise needs rewriting."
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
    # Both URLs are overridden rather than trusted: the environment file says
    # 5100 and 5101, and this script can be pointed elsewhere.
    if [ "$NEWMAN_VIA_NPX" -eq 1 ]; then
        "$NEWMAN_BIN" --no newman run "$POSTMAN_COLLECTION" \
            --environment "$POSTMAN_ENVIRONMENT" \
            --env-var "mosaicUrl=$BASE_URL" \
            --env-var "catalogUrl=$CATALOG_SUBGRAPH_URL" \
            --bail
    else
        "$NEWMAN_BIN" run "$POSTMAN_COLLECTION" \
            --environment "$POSTMAN_ENVIRONMENT" \
            --env-var "mosaicUrl=$BASE_URL" \
            --env-var "catalogUrl=$CATALOG_SUBGRAPH_URL" \
            --bail
    fi
    if [ $? -ne 0 ]; then
        step_fail 'postman' 'newman failed; its output above says which request failed.'
    fi
    step_ok 'postman collection'
fi

# -- 8. the two subgraphs still compose -------------------------------------

# wgc is a local dev dependency, pinned in package.json beside newman. It
# composes from committed schema files and talks to nothing. It is looked for
# here rather than inside the wire section below because both sections need it.
if [ -x "$REPO_ROOT/node_modules/.bin/wgc" ]; then
    WGC_BIN="$REPO_ROOT/node_modules/.bin/wgc"
elif command -v npx >/dev/null 2>&1 && npx --no wgc --help >/dev/null 2>&1; then
    WGC_BIN="npx"
    WGC_VIA_NPX=1
fi

# Chapter 8 shows no composition at all: everything it does is done against one
# subgraph at a time, by hand, and chapter 9 is where composition becomes the
# subject. The check was here from chapter 8 anyway, because three separate
# things about these two schemas would break the graph only when it is assembled
# - two subgraphs both declaring Query.node, the cost directives HotChocolate
# stamps by default, and PageCursor being the one paging type nothing marks
# shareable - and none of them is visible from either service on its own.
#
# Chapter 9 adds two things to it. The composed config is compared against the
# committed one, because the chapter prints what is inside it. And the
# composition cases run, because the chapter prints the composer's errors too,
# and an error message is as easy to go stale as a schema.
if [ ! -f "$FEDERATION_GRAPH" ]; then
    step_skip 'composition' 'federation/mosaic.yaml does not exist yet'
elif [ -z "$WGC_BIN" ]; then
    step_fail 'composition' 'wgc is not installed. It is a dev dependency: run npm install.'
else
    SUPERGRAPH="$TEMP_DIR/supergraph.json"
    if [ "$WGC_VIA_NPX" -eq 1 ]; then
        "$WGC_BIN" --no wgc router compose -i "$FEDERATION_GRAPH" -o "$SUPERGRAPH"
    else
        "$WGC_BIN" router compose -i "$FEDERATION_GRAPH" -o "$SUPERGRAPH"
    fi
    if [ $? -ne 0 ]; then
        step_fail 'composition' 'wgc router compose failed.
The two subgraph schemas under schema/ no longer compose into one graph. That is
a real finding rather than a tooling problem, and the table above says which
coordinate the composer objected to.'
    fi
    if [ ! -f "$SUPERGRAPH" ]; then
        step_fail 'composition' "wgc reported success but wrote nothing to $SUPERGRAPH."
    fi
    step_ok 'catalog and mosaic compose into one supergraph'

    # -- 8a. the composed config is the one chapter 9 takes apart -----------

    if [ ! -f "$FEDERATION_SUPERGRAPH" ]; then
        step_skip 'supergraph drift' 'federation/supergraph.json does not exist yet'
    elif ! same_text "$FEDERATION_SUPERGRAPH" "$SUPERGRAPH"; then
        step_fail 'supergraph drift' 'A fresh compose does not match federation/supergraph.json.

Chapter 9 prints the contents of that file: the datasource configurations, the
string storage, the compatibility version and the client schema with no join
directives in it. A change here is a change to the chapter.

If it is deliberate, recompose and commit the result:

    npx wgc router compose -i federation/mosaic.yaml -o federation/supergraph.json

If it is not, wgc changed the config format. That is a finding, and the version
is pinned in package.json precisely so it cannot happen quietly.'
    else
        step_ok 'the composed config matches federation/supergraph.json'
    fi

    # -- 8b. the errors chapter 9 prints ------------------------------------

    # Every case is Mosaic's own pair with one edit applied, so these assert the
    # messages the chapter quotes rather than messages from a fixture invented
    # to produce them.
    if [ ! -f "$COMPOSITION_CASES" ]; then
        step_skip 'composition cases' 'scripts/composition-cases.mjs does not exist yet'
    elif ! command -v node >/dev/null 2>&1; then
        step_fail 'composition cases' 'node is not on PATH; it is needed to run scripts/composition-cases.mjs.'
    else
        node "$COMPOSITION_CASES"
        if [ $? -ne 0 ]; then
            step_fail 'composition cases' 'scripts/composition-cases.mjs failed.
One of the composition errors chapter 9 prints is no longer the error the
composer produces. The output above says which case and how it differs. Fix the
chapter, not the assertion.'
        fi
        step_ok 'the composition errors chapter 9 prints are the ones wgc produces'
    fi

    # -- 8c. chapter 10: a router in front of the two -----------------------

    # The first section that asks the graph a question rather than asking a
    # service one. It runs here, after the composed config has been checked
    # against a fresh compose, because that file is what the router mounts:
    # verifying the graph the chapter describes means verifying the file the
    # chapter composed.
    #
    # It comes before the federated-wire section on purpose. Both routers
    # publish 3002 and the two are never meant to be up together, so this one
    # is taken down again at the end of the block.
    if [ "$SKIP_ROUTER" = "1" ]; then
        step_skip 'router' 'MOSAIC_SKIP_ROUTER=1 was set'
    elif [ ! -f "$ROUTER_CONFIG" ]; then
        step_skip 'router' 'router/config.yaml does not exist yet'
    else
        if port_taken "$ROUTER_PORT"; then
            step_fail 'start router' "Something is already listening on port $ROUTER_PORT.
An earlier run may have left one behind: \`docker compose down mosaic-router\`
clears this one, and \`docker compose --profile wire down\` clears chapter 7's,
which publishes the same port."
        fi

        docker compose --project-directory "$REPO_ROOT" up --detach mosaic-router
        if [ $? -ne 0 ]; then
            step_fail 'start router' 'docker compose up mosaic-router failed.
The first run of this pulls the router image.'
        fi
        STARTED_MOSAIC_ROUTER=1

        mosaic_router_deadline=$(( $(date +%s) + STARTUP_TIMEOUT_SECONDS ))
        mosaic_router_up=0
        while [ "$(date +%s)" -lt "$mosaic_router_deadline" ]; do
            if curl -fsS -o /dev/null --max-time 5 "$ROUTER_URL/health" 2>/dev/null; then
                mosaic_router_up=1
                break
            fi
            sleep 0.5
        done

        if [ "$mosaic_router_up" -ne 1 ]; then
            step_fail 'start router' "$ROUTER_URL/health did not answer within $STARTUP_TIMEOUT_SECONDS seconds.

$(docker compose --project-directory "$REPO_ROOT" logs --tail 40 mosaic-router 2>&1)"
        fi
        step_ok "the router answers on $ROUTER_URL/health"

        # The storefront query, the plan behind it, and what the router does
        # not expose. Chapter 10 prints all three.
        if [ ! -f "$ROUTER_POSTMAN" ] || [ ! -f "$ROUTER_POSTMAN_ENV" ]; then
            step_skip 'router postman' 'the router collection or its environment is missing from postman/'
        elif [ -z "$NEWMAN_BIN" ]; then
            step_skip 'router postman' 'newman is not installed - run npm install first'
        else
            if [ "$NEWMAN_VIA_NPX" -eq 1 ]; then
                "$NEWMAN_BIN" --no newman run "$ROUTER_POSTMAN" \
                    --environment "$ROUTER_POSTMAN_ENV" \
                    --env-var "routerUrl=$ROUTER_URL" \
                    --bail
            else
                "$NEWMAN_BIN" run "$ROUTER_POSTMAN" \
                    --environment "$ROUTER_POSTMAN_ENV" \
                    --env-var "routerUrl=$ROUTER_URL" \
                    --bail
            fi
            if [ $? -ne 0 ]; then
                step_fail 'router postman' 'newman failed; its output above says which request failed.
The storefront query answering out of two services is chapter 10'"'"'s headline,
and the query plan assertions are the listing it prints.'
            fi
            step_ok 'the router answers the query neither subgraph can'
        fi

        # And the three things chapter 10 says are surprising, each one a
        # router started on purpose against a config made for the case. Same
        # arrangement as chapter 9's composition cases and for the same reason:
        # one implementation, called by both verify scripts.
        if [ ! -f "$ROUTER_CASES" ]; then
            step_skip 'router cases' 'scripts/router-cases.mjs does not exist yet'
        elif ! command -v node >/dev/null 2>&1; then
            step_fail 'router cases' 'node is not on PATH; it is needed to run scripts/router-cases.mjs.'
        else
            node "$ROUTER_CASES"
            if [ $? -ne 0 ]; then
                step_fail 'router cases' 'scripts/router-cases.mjs failed.
One of the three router behaviours chapter 10 describes has changed. The output
above says which. Fix the chapter, not the assertion.'
            fi
            step_ok 'the router behaves the three ways chapter 10 says it does'
        fi

        # -- chapter 11 -----------------------------------------------------

        # What the second hop costs. This collection needs the router and both
        # subgraphs at once, which is why it runs here rather than beside the
        # subgraph collection above: two of its five requests go straight to a
        # service, to ask it something the router will not.
        if [ ! -f "$ENTITIES_POSTMAN" ] || [ ! -f "$ENTITIES_POSTMAN_ENV" ]; then
            step_skip 'entities postman' 'the entities collection or its environment is missing from postman/'
        elif [ -z "$NEWMAN_BIN" ]; then
            step_skip 'entities postman' 'newman is not installed - run npm install first'
        else
            if [ "$NEWMAN_VIA_NPX" -eq 1 ]; then
                "$NEWMAN_BIN" --no newman run "$ENTITIES_POSTMAN" \
                    --environment "$ENTITIES_POSTMAN_ENV" \
                    --env-var "routerUrl=$ROUTER_URL" \
                    --env-var "mosaicUrl=$BASE_URL" \
                    --env-var "catalogUrl=$CATALOG_SUBGRAPH_URL" \
                    --bail
            else
                "$NEWMAN_BIN" run "$ENTITIES_POSTMAN" \
                    --environment "$ENTITIES_POSTMAN_ENV" \
                    --env-var "routerUrl=$ROUTER_URL" \
                    --env-var "mosaicUrl=$BASE_URL" \
                    --env-var "catalogUrl=$CATALOG_SUBGRAPH_URL" \
                    --bail
            fi
            if [ $? -ne 0 ]; then
                step_fail 'entities postman' 'newman failed; its output above says which request failed.
Product.shippingCost is the first field in Mosaic that cannot be answered
without the other service, and the plan assertions are the listing chapter 11
prints.'
            fi
            step_ok 'a field that needs the other service is answered, and its plan says so'
        fi

        # Eleven cases: three composition, one against Mosaic through _entities,
        # and seven on the sample under samples/entity-resolution, which that
        # script starts and stops itself. Same arrangement as chapters 9 and 10
        # and for the same reason: one implementation, both verify scripts.
        if [ ! -f "$ENTITY_CASES" ]; then
            step_skip 'entity cases' 'scripts/entity-cases.mjs does not exist yet'
        elif ! command -v node >/dev/null 2>&1; then
            step_fail 'entity cases' 'node is not on PATH; it is needed to run scripts/entity-cases.mjs.'
        else
            node "$ENTITY_CASES"
            if [ $? -ne 0 ]; then
                step_fail 'entity cases' 'scripts/entity-cases.mjs failed.
One of the entity-resolution behaviours chapter 11 describes has changed. The
output above says which. Fix the chapter, not the assertion.'
            fi
            step_ok 'entities resolve the eleven ways chapter 11 says they do'
        fi

        # Down rather than stop, and now rather than in the trap, because the
        # federated-wire section below wants this port.
        docker compose --project-directory "$REPO_ROOT" down mosaic-router >/dev/null 2>&1
        STARTED_MOSAIC_ROUTER=0
    fi
fi

# -- 9. chapter 7's federated wire ------------------------------------------

# Two subgraphs on the host and the Cosmo Router in a container. This is the one
# section that needs a registry pull, and the one that can be skipped, because
# everything above it is about Mosaic and none of this is.
if [ "$SKIP_WIRE" = "1" ]; then
    step_skip 'federated wire' 'MOSAIC_SKIP_WIRE=1 was set'
elif [ ! -d "$WIRE_DIR" ]; then
    step_skip 'federated wire' 'samples/federated-wire does not exist yet'
elif [ -z "$NEWMAN_BIN" ]; then
    step_skip 'federated wire' 'newman is not installed - run npm install first'
else
    # -- 9a. compose the supergraph -----------------------------------------

    # wgc was found in step 8, which needs it for Mosaic's own two subgraphs.
    # This section composes the chapter 7 sample instead.
    if [ -z "$WGC_BIN" ]; then
        step_fail 'federated wire' 'wgc is not installed, and the router cannot start without a
composed execution config. It is a dev dependency: run npm install.'
    fi

    if [ "$WGC_VIA_NPX" -eq 1 ]; then
        "$WGC_BIN" --no wgc router compose -i "$WIRE_GRAPH" -o "$WIRE_SUPERGRAPH"
    else
        "$WGC_BIN" router compose -i "$WIRE_GRAPH" -o "$WIRE_SUPERGRAPH"
    fi
    if [ $? -ne 0 ]; then
        step_fail 'wire composition' 'wgc router compose failed.
Composition failing is a real finding, not a tooling problem: the two subgraph
schemas under schema/samples no longer compose into one graph.'
    fi
    if [ ! -f "$WIRE_SUPERGRAPH" ]; then
        step_fail 'wire composition' "wgc reported success but wrote nothing to $WIRE_SUPERGRAPH."
    fi
    step_ok 'wgc composed the two subgraphs into one supergraph'

    # -- 9b. start both subgraphs -------------------------------------------

    for entry in $WIRE_SUBGRAPHS; do
        name="${entry%%:*}"
        rest="${entry#*:}"
        project="$WIRE_DIR/${rest%%:*}"
        subgraph_port="${rest##*:}"

        if port_taken "$subgraph_port"; then
            step_fail "start $name subgraph" "Something is already listening on port $subgraph_port.
Stop it first: an earlier run of this script, or a debugger."
        fi

        ASPNETCORE_URLS="http://localhost:$subgraph_port" ASPNETCORE_ENVIRONMENT=Development \
            dotnet run --project "$project" -c Release --no-build --no-launch-profile \
            > "$TEMP_DIR/wire-$name.log" 2>&1 &
        if [ "$name" = "catalog" ]; then
            CATALOG_PID=$!
        else
            REVIEWS_PID=$!
        fi
    done

    for entry in $WIRE_SUBGRAPHS; do
        name="${entry%%:*}"
        rest="${entry#*:}"
        subgraph_port="${rest##*:}"

        deadline=$(( $(date +%s) + STARTUP_TIMEOUT_SECONDS ))
        up=0
        while [ "$(date +%s)" -lt "$deadline" ]; do
            if curl -fsS -o /dev/null --max-time 5 \
                -H 'Content-Type: application/json' \
                -H 'Accept: application/json' \
                --data-binary '{"query":"{ __typename }"}' \
                "http://localhost:$subgraph_port/graphql" 2>/dev/null; then
                up=1
                break
            fi
            sleep 0.5
        done

        if [ "$up" -ne 1 ]; then
            step_fail "start $name subgraph" "http://localhost:$subgraph_port/graphql did not answer within $STARTUP_TIMEOUT_SECONDS seconds.

$(tail -n 40 "$TEMP_DIR/wire-$name.log" 2>/dev/null)"
        fi
    done
    step_ok "both subgraphs answering on $CATALOG_PORT and $REVIEWS_PORT"

    # -- 9c. the published schemas ------------------------------------------

    for entry in $WIRE_SUBGRAPHS; do
        name="${entry%%:*}"
        rest="${entry#*:}"
        subgraph_port="${rest##*:}"
        committed="$SAMPLE_SCHEMA_DIR/wire-$name.graphql"

        service_status="$(curl -sS -o "$TEMP_DIR/wire-$name.service.json" -w '%{http_code}' \
            --max-time 30 \
            -H 'Content-Type: application/json' \
            -H 'Accept: application/json' \
            --data-binary '{"query":"{ _service { sdl } }"}' \
            "http://localhost:$subgraph_port/graphql")"

        if [ "$service_status" != "200" ]; then
            step_fail "$name _service" "_service on the $name subgraph answered $service_status.

$(cat "$TEMP_DIR/wire-$name.service.json" 2>/dev/null)"
        fi

        published_sdl "$TEMP_DIR/wire-$name.service.json" > "$TEMP_DIR/wire-$name.published.graphql"

        if [ ! -f "$committed" ]; then
            step_fail "$name subgraph schema" "There is no committed snapshot at $committed."
        fi

        if ! same_text "$committed" "$TEMP_DIR/wire-$name.published.graphql"; then
            step_fail "$name subgraph schema" "What the $name subgraph publishes is not what is committed in
schema/samples/wire-$name.graphql.

That file is the composer's input. If the change is deliberate, regenerate it
from _service and recompose; if it is not, something moved the federated
contract without saying so.

$(schema_diff "$committed" "$TEMP_DIR/wire-$name.published.graphql" "wire-$name")"
        fi
    done
    step_ok 'both subgraphs publish the committed schemas through _service'

    # -- 9d. the router ------------------------------------------------------

    if port_taken "$ROUTER_PORT"; then
        step_fail 'start router' "Something is already listening on port $ROUTER_PORT.
Stop it first: \`docker compose --profile wire down\` clears an earlier run."
    fi

    docker compose --project-directory "$REPO_ROOT" --profile wire up --detach wire-router
    if [ $? -ne 0 ]; then
        step_fail 'start router' 'docker compose up wire-router failed.
The first run of this pulls the router image; a failure here is usually the
registry rather than the graph.'
    fi
    STARTED_ROUTER=1

    router_deadline=$(( $(date +%s) + STARTUP_TIMEOUT_SECONDS ))
    router_up=0
    while [ "$(date +%s)" -lt "$router_deadline" ]; do
        if curl -fsS -o /dev/null --max-time 5 "$ROUTER_URL/health" 2>/dev/null; then
            router_up=1
            break
        fi
        sleep 0.5
    done

    if [ "$router_up" -ne 1 ]; then
        step_fail 'start router' "$ROUTER_URL/health did not answer within $STARTUP_TIMEOUT_SECONDS seconds.

$(docker compose --project-directory "$REPO_ROOT" logs --tail 40 wire-router 2>&1)"
    fi
    step_ok "router answering on $ROUTER_URL/health"

    # -- 9e. the collection --------------------------------------------------

    if [ ! -f "$WIRE_POSTMAN" ] || [ ! -f "$WIRE_POSTMAN_ENV" ]; then
        step_fail 'wire postman' 'the federated-wire collection or its environment is missing from postman/'
    fi

    if [ "$NEWMAN_VIA_NPX" -eq 1 ]; then
        "$NEWMAN_BIN" --no newman run "$WIRE_POSTMAN" \
            --environment "$WIRE_POSTMAN_ENV" \
            --env-var "catalogUrl=$CATALOG_URL" \
            --env-var "reviewsUrl=$REVIEWS_URL" \
            --env-var "routerUrl=$ROUTER_URL" \
            --bail
    else
        "$NEWMAN_BIN" run "$WIRE_POSTMAN" \
            --environment "$WIRE_POSTMAN_ENV" \
            --env-var "catalogUrl=$CATALOG_URL" \
            --env-var "reviewsUrl=$REVIEWS_URL" \
            --env-var "routerUrl=$ROUTER_URL" \
            --bail
    fi
    if [ $? -ne 0 ]; then
        step_fail 'wire postman' 'newman failed; its output above says which request failed.'
    fi
    step_ok 'federated-wire postman collection'

    # -- 9f. what actually went over the wire --------------------------------

    # The collection asserts the query plan the router reports. This asserts the
    # requests the subgraphs received, which is the same claim checked from the
    # other end, and it is the pair of listings chapter 7 prints. grep -F, not
    # grep: both strings are full of characters a regular expression would read
    # as syntax.
    if ! grep -qF "$EXPECTED_CATALOG_FETCH" "$TEMP_DIR/wire-catalog.log"; then
        step_fail 'wire traffic' "The catalog subgraph never received the request chapter 7 prints:

    $EXPECTED_CATALOG_FETCH

The router adds __typename and id to that selection because it needs a key for
the second fetch. If the body changed, the chapter is wrong rather than the
router."
    fi

    if ! grep -qF "$EXPECTED_REVIEWS_FETCH" "$TEMP_DIR/wire-reviews.log"; then
        step_fail 'wire traffic' "The reviews subgraph never received the entity fetch chapter 7 prints:

    $EXPECTED_REVIEWS_FETCH

Three representations in one call is the claim. Three separate calls would still
answer correctly and would still fail this check."
    fi
    step_ok 'the router sent the two requests chapter 7 prints'
fi

# -- 10. the EXIT trap stops the services and prints the summary ------------

exit 0
