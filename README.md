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
| `ch08` | 8. The First Cut: Extracting Catalog | Two services. `Mosaic.Catalog` on 5101 owns `Product`; Mosaic on 5100 keeps the other five domains and contributes `price`, `availableQuantity`, `reviews` and `averageRating` to the same type. Both are federation subgraphs |

Later chapters add their tags here as they are written. The convention is `chNN`
for the end-of-chapter state, and `chNN-<step>` if a chapter needs an
intermediate one.

## What you need

- .NET SDK 10.0.302 or later in the same feature band (pinned in `global.json`)
- Docker. Since chapter 4 Mosaic keeps its data in PostgreSQL, and
  `docker-compose.yml` is the only description of it. Since chapter 8 there are
  two services and two databases in that one container
- Node, to run the Postman collections from the command line and, since chapter
  7, to compose supergraphs with `wgc`

## Running it

Start the database first, then both services, in two terminals. It is the same
container whether you then run them from the SDK or from their own images:

```
docker compose up -d mosaic-db
dotnet run --project src/Mosaic.Catalog    # http://localhost:5101
dotnet run --project src/Mosaic.Api        # http://localhost:5100
```

Two commands since chapter 8. Each service keeps its own database on that one
PostgreSQL container, `catalog` and `mosaic`, and creates and seeds it on first
start:

```
info: Mosaic.Catalog.Data.CatalogDatabaseSeeder[0]
      Seeded 25 products into a newly created database.
```

```
info: Mosaic.Api.Infrastructure.Data.DatabaseSeeder[0]
      Seeded 25 prices, 120 reviews, 12 customers and 8 orders into a newly created schema.
```

No new container was needed for the new service. `EnsureCreatedAsync` creates
the database as well as the schema, so `catalog` appeared on the server that was
already running, beside `mosaic` and separate from it.

Both services serve Nitro, HotChocolate's built-in IDE, at `/graphql`. Open
<http://localhost:5101/graphql> and ask Catalog for products:

```graphql
{
  browseProducts(first: 5) {
    nodes { id sku title category }
  }
}
```

`products` still answers and is deprecated since chapter 5; `browseProducts` is
what replaced it. Both moved to Catalog whole in chapter 8, along with
`productById` and `productBySku`. Chapter 5 is about which changes a client can
survive and which it cannot, and the graph still carries one of each.

What no longer answers anywhere is the query the earlier chapters opened with:

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

Catalog has the titles and cannot price them. Mosaic has the prices and cannot
list the products. Assembling that answer out of two services is a router's job,
and chapter 10 is where one goes in front of these two. Until then, the way to
ask a subgraph about a product it did not find is `_entities`, below.

Since chapter 5 there is also a `Mutation` and a `Subscription`, both still
Mosaic's. Both take a product id, and the id comes from Catalog now: ask
<http://localhost:5101/graphql> for `{ browseProducts(first: 1) { nodes { id } } }`
and paste the string it answers with into <http://localhost:5100/graphql>. Open
two Nitro tabs, subscribe in one and write in the other:

```graphql
subscription { onReviewAdded(productId: "<a product id>") { rating body } }
```

That the same string means the same product in both services is the whole
mechanism of this chapter, not a convenience: it is the federation key.

Or in containers, which publish the same two ports so every URL above still
works:

```
docker compose up --build
```

## The two subgraphs

`src/Mosaic.Catalog`, on 5101, owns the `Product` entity: `sku`, `title`,
`description`, `category`, and the four root fields that find products -
`products`, `browseProducts`, `productById` and `productBySku`.

`src/Mosaic.Api`, on 5100, keeps Pricing, Inventory, Reviews, Accounts and
Ordering. It declares the same `Product`, with the same key, and contributes
`price`, `availableQuantity`, `reviews` and `averageRating` to it. Its own
`Product` class holds an id and nothing else.

Both publish `_service`, which is how a composer reads a subgraph's schema, and
`_entities`, which is how anything asks a subgraph for an object by key. Both
schemas are committed, one file each: `schema/catalog.graphql` and
`schema/mosaic.graphql`. `federation/mosaic.yaml` names the pair and says where
each answers. Chapter 8 creates that file and composes nothing with it; chapter
9 is where composition is the subject.

`Product.id` is the field chapter 5 designed, unchanged: a Relay global
identifier, base64, carrying the type name beside the key. Since chapter 8 it is
also the federation `@key`, which makes that opaque string the entire contract
between the two services. Product 1's is `UHJvZHVjdDoAAACgAAAAQIAAAAAAAAAB`.

Ask Catalog for it by key:

```
curl -s http://localhost:5101/graphql \
  -H 'Content-Type: application/json' \
  -d '{"query":"query($representations: [_Any!]!) { _entities(representations: $representations) { ... on Product { sku title } } }","variables":{"representations":[{"__typename":"Product","id":"UHJvZHVjdDoAAACgAAAAQIAAAAAAAAAB"}]}}'
```

```json
{"data":{"_entities":[{"sku":"MOS-FRN-0001","title":"Larsen Oak Dining Table"}]}}
```

Send the same representation to 5100 and put `price { amount currency }` in the
selection instead, and Mosaic answers for the same product out of its own
tables. Neither service can answer for the other's fields, and neither needs to:
a representation carries `__typename` and the key, and that is all either one
reads.

`Query.node` and `Query.nodes` are gone from both. Two subgraphs cannot both
declare them, and `@shareable` would be a lie, because neither service can
resolve the other's node types. The `Node` interface stayed, and so did the
global identifiers, which is what makes the key above work at all. Chapter 13 is
where a federated `node` field comes back.

## Verifying it

```
pwsh scripts/verify.ps1      # Windows, macOS, Linux
bash scripts/verify.sh       # macOS, Linux
```

This is the gate a chapter tag has to pass. It starts PostgreSQL from
`docker-compose.yml`, restores and builds the solution in Release with warnings
as errors, regenerates both service schemas and fails if either has drifted from
its committed copy under `schema/`, checks that the three sample projects still
produce byte-identical SDL, composes the two subgraphs `federation/mosaic.yaml`
names and fails if they stop composing, starts both services, asserts Catalog
answers with 25 products and that Mosaic answers for all 25 of them through
`_entities` with their 120 reviews, checks the request pipeline is the expected
thirteen middleware in order, checks the resolver and SQL command counts the
book quotes, and runs the Postman collections.

Since chapter 5 the run starts by dropping the schema and reseeding it. The
collection submits a review, so a run leaves the database changed, and a gate
whose result depends on how many times it has been run is not a gate. The switch
is `MOSAIC_RESET_DATABASE=1`, which the scripts set for you. Since chapter 8
both services read it, under that one name on purpose, and there are two
databases to drop. Do not point the scripts at a server holding anything you
want to keep.

It stops the database container on the way out and leaves its volume in place.
Pass `-KeepDatabase` (or set `MOSAIC_KEEP_DATABASE=1`) to leave it running, which
is worth doing while iterating: starting PostgreSQL is the slowest step.

The Postman collections need newman, which is pinned as a local dev dependency:

```
npm ci
npx newman run postman/mosaic-federation.postman_collection.json \
    -e postman/mosaic-federation.local.postman_environment.json
```

That collection is chapter 8's, and it is the first one that talks to two
services: it asks Catalog and Mosaic each for what it owns, and each for the
same product by key.

Since chapter 7 the run finishes with the federated-wire sample: it composes the
two subgraph schemas, starts both subgraphs and the router, runs a second
collection against all three, and then checks the subgraph logs for the two
requests the chapter prints. That last check is the interesting one, because the
collection reads the router's own account of what it planned and this reads what
the subgraphs actually received. Pass `-SkipWire` (or set `MOSAIC_SKIP_WIRE=1`)
to leave it out; it is the slowest section and the only one that pulls an image.

## Layout

```
src/Mosaic.Catalog/      the Catalog subgraph, on 5101
  Catalog/               products: identity, description, category
    Data/                the DbContext, the seeder, the service and its DataLoader
    Model/               the Product record, its key decoder, and the reference
                         resolver that turns a representation back into one
    Types/               the root fields, and Product's id and node resolver
  Federation/            PageCursor, marked shareable so both graphs may declare it
src/Mosaic.Api/          the other five domains, on 5100
  Catalog/Model/         all that is left of Catalog here: a Product carrying an
                         id and a reference resolver, for the other domains to
                         hang their fields on
  Pricing/               what a product costs
  Inventory/             whether you can have one
  Reviews/               what customers thought
  Accounts/              who the customers are
  Ordering/              what they bought
  Infrastructure/        the lookup counter
    Data/                the DbContext, the seeder and the SQL command counter
    Diagnostics/         the pipeline report and the per-request timeline
    Errors/              the Error interface every domain error implements
    Federation/          PageCursor again, the same two attributes
federation/mosaic.yaml   which subgraphs the graph is made of, and where each
                         one answers
samples/three-approaches/  the same tiny schema, three authoring styles
samples/resolver-scopes/   what [UseRequestScope] changes, in two fields
samples/federated-wire/    two subgraphs and a router, so the traffic between
                           them can be read; chapter 7
schema/                  committed SDL snapshots, one per service
postman/                 collections and environments
scripts/                 verify.ps1 and verify.sh
```

Each domain's `Data/` folder holds everything that domain knows about storage:
its service, its DataLoaders, its `IEntityTypeConfiguration` and its seed rows.
`MosaicDbContext` collects those configurations and owns no mapping of its own,
and `CatalogDbContext` does the same in the other service for the one domain it
has.

Every field lives in the folder of the domain that owns it, including fields on
types another domain defined. `Product` is a Catalog record, but `Product.price`
is a resolver in `Pricing/Types/` and `Product.reviews` is one in
`Reviews/Types/`. That is what let chapter 8 lift Catalog out into a service of
its own without rewriting a resolver: `ProductPricingNode`,
`ProductInventoryNode` and `ProductReviewsNode` did not change by a character,
because the type they hang fields off is still called `Product` and still lives
in `Catalog/Model`. It is a different class in a different assembly now, holding
nothing but an id, and the three files never noticed.

## About the lookup count

For four chapters this section was one query and three numbers. Since chapter 8
no single service can answer that query, so the numbers had to be measured
again. The story is the same one; what follows is where each number landed.

Chapters 2 and 3, one service, the query on the product page:

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

At tag `ch08` that query is not Mosaic's to answer at all: `products` went to
Catalog. The nearest thing Mosaic can be asked is the same page arriving the way
a router would send it, as 25 representations through `_entities`, with
`reviews(first: 12)` and each review's author:

```graphql
query($representations: [_Any!]!) {
  _entities(representations: $representations) {
    ... on Product {
      reviews(first: 12) { nodes { rating author { displayName } } }
    }
  }
}
```

That reports 146 resolvers and 2 SQL. The resolver count did not move: the
entity fetch stands where the product list used to, and the 25 review resolvers
and 120 author resolvers under it are the ones that were always there. The
statement that left is the one that fetched the products, and Catalog runs it
now.

Catalog's side of the same page is one statement, and it is one because of how
it was asked. All 25 keys in a single `_entities` call cost 1 statement; the
same 25 keys sent one call at a time cost 25. A DataLoader batches within a
request, and a subgraph's request is now somebody else's HTTP call, so the
batching chapter 4 bought reaches exactly as far as the caller's own batching
does. That is chapter 4's N+1 again, one level up, and it is the router's to
avoid rather than the subgraph's.

One statement on Catalog and two on Mosaic is three, which is what the single
service ran at `ch04` and `ch05`: the same three fetches, against two databases
in two processes.

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
    (document cache miss, operation cache miss, 146 resolvers, 2 SQL)
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
on the same line was the whole point of chapter 4's exercise; since chapter 8
the same line says 146 and 2, and the statement that is missing is one Catalog
now runs in another process.

To see what `[UseRequestScope]` changes:

```
dotnet run --project samples/resolver-scopes
```

then ask for two default-scope fields and two request-scope ones in a single
query. The default ones each get their own service scope; the annotated ones
share the request's.

## Watching a federated request go through

New in chapter 7, and deliberately not part of Mosaic: Mosaic was one service
when this was written, and the sample exists only so that the messages between a
router and a subgraph can be read without anything else in the way. It stays
separate at this tag, because Mosaic's own two subgraphs have no router in front
of them until chapter 10.

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
