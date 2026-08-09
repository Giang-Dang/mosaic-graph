# mosaic-graph

Companion code for *Federated GraphQL on .NET: Building and running distributed
graphs with HotChocolate and WunderGraph Cosmo*.

Mosaic is an online storefront: a catalog of products, prices that change more
often than the products do, stock levels that change faster still, orders,
customer accounts, and reviews. Six domains, one shared noun. The book starts it
as a single service and takes it apart into a federated graph.

Every listing printed in the book comes from this repository at the tag named in
that chapter, and compiles.

## Chapters and tags

Check out a tag to get the system as it stands at the end of that chapter.

| Tag | Chapter | State |
|-----|---------|-------|
| `ch02` | 2. HotChocolate, Quickly | One service, six domain folders, in-memory data |
| `ch03` | 3. The Life of a Request | The same service, instrumented: pipeline report, per-request timeline, resolver-scope sample |
| `ch04-ef` | 4. Data Without the N+1, halfway | The same schema on PostgreSQL through EF Core. Still 146 lookups, and now 146 round trips |
| `ch04` | 4. Data Without the N+1 | DataLoaders behind the same resolvers: 146 resolvers, 3 round trips. Plus `browseProducts`, paged, filtered, sorted and projected |
| `ch05` | 5. Schema Design That Survives Change | The `Node` interface and real global identifiers, `Product.reviews` as a connection, Mosaic's first mutation with typed errors, a subscription, and `products` deprecated |
| `ch07` | 7. How a Federated Query Actually Runs | Mosaic unchanged. A new sample: two tiny subgraphs and the Cosmo Router, with every request between them logged |

Later chapters add their tags here as they are written. The convention is `chNN`
for the end-of-chapter state, and `chNN-<step>` if a chapter needs an
intermediate one.

## What you need

- .NET SDK 10.0.302 or later in the same feature band (pinned in `global.json`)
- Docker. Since chapter 4 Mosaic keeps its data in PostgreSQL, and
  `docker-compose.yml` is the only description of it
- Node, to run the Postman collections from the command line and, since chapter
  7, to compose the sample supergraph with `wgc`

## Running it

Start the database first. It is the same container whether you then run the
service from the SDK or from its own image:

```
docker compose up -d mosaic-db
dotnet run --project src/Mosaic.Api
```

The service creates its schema and seeds it on first start, and says so:

```
info: Mosaic.Api.Infrastructure.Data.DatabaseSeeder[0]
      Seeded 25 products, 120 reviews, 12 customers and 8 orders into a newly created schema.
```

The service listens on <http://localhost:5100>. Open
<http://localhost:5100/graphql> in a browser and HotChocolate serves Nitro, its
built-in IDE. Try:

```graphql
{
  browseProducts(first: 5) {
    nodes {
      title
      price { amount currency }
      availableQuantity
      averageRating
      reviews(first: 3) {
        totalCount
        nodes { rating author { displayName } }
      }
    }
  }
}
```

`products` still answers and is deprecated since chapter 5; `browseProducts` is
what replaced it. That chapter is about which changes a client can survive and
which it cannot, and Mosaic carries one of each.

Since chapter 5 there is also a `Mutation` and a `Subscription`. Open two Nitro
tabs, subscribe in one and write in the other:

```graphql
subscription { onReviewAdded(productId: "<a product id>") { rating body } }
```

Or in a container, which publishes the same port so every URL above still works:

```
docker compose up --build
```

## Verifying it

```
pwsh scripts/verify.ps1      # Windows, macOS, Linux
bash scripts/verify.sh       # macOS, Linux
```

This is the gate a chapter tag has to pass. It starts PostgreSQL from
`docker-compose.yml`, restores and builds the solution in Release with warnings
as errors, regenerates the schema and fails if it has drifted from the committed
`schema/mosaic.graphql`, checks that the three sample projects still produce
byte-identical SDL, starts the service, asserts the seeded catalog answers with
25 products and 120 reviews, checks the request pipeline is the expected
thirteen middleware in order, checks the lookup, resolver and SQL command counts
the book quotes, and runs the Postman collection.

Since chapter 5 the run starts by dropping Mosaic's schema and reseeding it. The
collection submits a review, so a run leaves the database changed, and a gate
whose result depends on how many times it has been run is not a gate. The switch
is `MOSAIC_RESET_DATABASE=1`, which the scripts set for you. Do not point them at
a database holding anything you want to keep.

It stops the database container on the way out and leaves its volume in place.
Pass `-KeepDatabase` (or set `MOSAIC_KEEP_DATABASE=1`) to leave it running, which
is worth doing while iterating: starting PostgreSQL is the slowest step.

The Postman collections need newman, which is pinned as a local dev dependency:

```
npm ci
npx newman run postman/mosaic.postman_collection.json \
    -e postman/mosaic.local.postman_environment.json
```

Since chapter 7 the run finishes with the federated-wire sample: it composes the
two subgraph schemas, starts both subgraphs and the router, runs a second
collection against all three, and then checks the subgraph logs for the two
requests the chapter prints. That last check is the interesting one, because the
collection reads the router's own account of what it planned and this reads what
the subgraphs actually received. Pass `-SkipWire` (or set `MOSAIC_SKIP_WIRE=1`)
to leave it out; it is the slowest section and the only one that pulls an image.

## Layout

```
src/Mosaic.Api/          the service; one folder per domain
  Catalog/               products: identity, description, category
  Pricing/               what a product costs
  Inventory/             whether you can have one
  Reviews/               what customers thought
  Accounts/              who the customers are
  Ordering/              what they bought
  Infrastructure/        the lookup counter
    Data/                the DbContext, the seeder and the SQL command counter
    Diagnostics/         the pipeline report and the per-request timeline
    Errors/              the Error interface every domain error implements
samples/three-approaches/  the same tiny schema, three authoring styles
samples/resolver-scopes/   what [UseRequestScope] changes, in two fields
samples/federated-wire/    two subgraphs and a router, so the traffic between
                           them can be read; chapter 7
schema/                  committed SDL snapshots
postman/                 collections and environments
scripts/                 verify.ps1 and verify.sh
```

Each domain's `Data/` folder holds everything that domain knows about storage:
its service, its DataLoaders, its `IEntityTypeConfiguration` and its seed rows.
`MosaicDbContext` collects those configurations and owns no mapping of its own.

Every field lives in the folder of the domain that owns it, including fields on
types another domain defined. `Product` is a Catalog record, but `Product.price`
is a resolver in `Pricing/Types/` and `Product.reviews` is one in
`Reviews/Types/`. That is what lets the book later lift a domain out into its
own service without rewriting resolvers.

## About the lookup count

Run the query on the product page and watch the log:

```
Service lookups this request: 146
```

One lookup for the product list, one per product for its reviews, one per review
for its author. The domain services take a single key and have no batch
overload, deliberately.

Against a `List<T>` in memory nobody noticed, which is exactly why this pattern
reaches production. At tag `ch04-ef` the same 146 lookups are 146 statements
against PostgreSQL, and the request timeline reports both numbers.

At tag `ch04` the resolvers are unchanged in shape and the log says:

```
Service lookups this request: 3
```

The engine still runs 146 resolvers. Each one now hands a key to a DataLoader
instead of asking a domain service a question, and the keys are gathered into
three statements: the products, their reviews, and the twelve distinct customers
who wrote those reviews. Only the batch fetches count as lookups, so the two
numbers that used to agree no longer do, and the gap between them is the
chapter.

At tag `ch05` the query has to be written differently, because `reviews` is a
connection now:

```graphql
{ products { title reviews(first: 12) { nodes { rating author { displayName } } } } }
```

It still reports 146 resolvers and 3 SQL. The field changed shape, the batching
did not, and the second statement is now a window function that returns only the
first `n` reviews of each product rather than all of them.

## Watching a request go through

From tag `ch03` the service reports what the execution engine did with it. At
startup it logs the request pipeline it assembled:

```
Request pipeline: 13 middleware
  1. InstrumentationMiddleware
  ...
  13. OperationExecutionMiddleware
```

and every request logs a timeline:

```
parse - validate 0.204ms compile 0.097ms coerce - execute 0.492ms total 0.931ms
    (document cache miss, operation cache miss, 146 resolvers, 3 SQL)
```

Send the same query twice and the second one reports both caches hitting, with
validation and compilation skipped entirely. `parse` never fires over HTTP: the
transport parses the document before the execution pipeline runs, which is also
why a syntax error never produces a timeline line at all.

Plain record properties - `title`, `rating`, `displayName` - are not resolvers
and never appear in the resolver count. Through chapters 2 and 3 that count
matched the lookup count exactly, because every resolver did one lookup.

The last field arrived with chapter 4 and is the one worth watching. It counts
the commands Entity Framework Core actually sent, measured by an interceptor on
the command rather than inferred from anything above it. Watching 146 and 3 sit
on the same line is the whole point of the exercise.

To see what `[UseRequestScope]` changes:

```
dotnet run --project samples/resolver-scopes
```

then ask for two default-scope fields and two request-scope ones in a single
query. The default ones each get their own service scope; the annotated ones
share the request's.

## Watching a federated request go through

New in chapter 7, and deliberately not part of Mosaic: Mosaic is one service
until chapter 8, and this sample exists only so that the messages between a
router and a subgraph can be read without anything else in the way.

Two subgraphs share one entity. Catalog owns `Product` and gives it a title and
a price; Reviews declares the same `Product` with the same key and adds
`reviews`. Three products, three reviews spread 2 / 1 / 0, no database.

```
dotnet run --project samples/federated-wire/Mosaic.Sample.Wire.Catalog   # :5201
dotnet run --project samples/federated-wire/Mosaic.Sample.Wire.Reviews   # :5202

npx wgc router compose -i samples/federated-wire/graph.yaml \
                       -o samples/federated-wire/supergraph.json
docker compose --profile wire up -d wire-router                          # :3002
```

Then ask the router for something neither subgraph can answer alone:

```graphql
{ products { title price reviews { rating body } } }
```

Both subgraphs log every request and response in full, headers and bodies
included, through ASP.NET Core's own HTTP logging middleware. The catalog
console shows the router asking for two fields it was told about and two it was
not:

```
RequestBody: {"query":"{products {title price __typename id}}"}
```

and the reviews console shows the second fetch, with all three products in one
call:

```
RequestBody: {"variables":{"representations":[{"__typename":"Product","id":"1"},
  {"__typename":"Product","id":"2"},{"__typename":"Product","id":"3"}]},
  "query":"query($representations: [_Any!]!){_entities(representations: ...
```

To see the plan behind that without executing it:

```
curl -s http://localhost:3002/graphql \
  -H 'Content-Type: application/json' \
  -H 'X-WG-Include-Query-Plan: true' \
  -H 'X-WG-Skip-Loader: true' \
  -d '{"query":"{ products { title reviews { rating } } }"}'
```

Both headers need `DEV_MODE` on the router, which `docker-compose.yml` sets.
