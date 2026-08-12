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
| `ch09` | 9. Composition | Neither service changes by a line. The composed router execution config is committed at `federation/supergraph.json`, and `scripts/composition-cases.mjs` produces five composition errors on purpose, each one the real pair of schemas with a single edit applied |
| `ch10` | 10. Enter the Router | Neither service changes by a line again. The Cosmo Router joins `docker-compose.yml` with a `router/config.yaml` of its own, and the storefront query answers for the first time since chapter 8 |
| `ch11` | 11. Entity Resolution Done Right | `Product.shippingCost`, the first field in Mosaic that needs something Catalog owns, declared `@requires(fields: "category")`. Plus `samples/entity-resolution`, where a reference resolver writes down every call the router makes to it and `@provides` is caught both saving a round trip and telling a lie |
| `ch12` | 12. Strangling the Monolith | Six services. `Mosaic.Api` is gone and its five domains are `Mosaic.Pricing`, `Mosaic.Inventory`, `Mosaic.Accounts`, `Mosaic.Reviews` and `Mosaic.Ordering`, on 5102 to 5106, one database each. `Mosaic.ServiceDefaults` is the first project here that is not a service. `scripts/override-cases.mjs` produces nine `@override` behaviours on purpose, including the book's first composition warning |
| `ch13` | 13. Hard Modeling Problems | Seven services. `Mosaic.Nodes` on 5107 owns `Query.node` and `Query.nodes` for the whole graph and has no database at all; `Review` and `Order` become entities so that the router can find one from an identifier. Catalog's paging cursor gets its tiebreaker back, which is a defect chapter 4 shipped and no schema could show. Plus `samples/interface-object` and `scripts/modeling-cases.mjs`, fourteen cases about enums, value types, scalars and `node` |
| `ch14` | 14. Real-Time in a Federated World | Seven services and an eighth subgraph that is a file. `schema/streams.graphql` declares one subscription field and a NATS subject and has no project behind it: the router subscribes to the broker itself and resolves the payload from a type name and a key. `Mosaic.Reviews` gains the publisher that feeds it and `mosaic-nats` joins `docker-compose.yml`. Plus `scripts/realtime-cases.mjs`, eight cases about a transport no schema mentions, and `scripts/subscription-run.mjs`, which finally puts a subscription inside the gate |

| `ch15` | 15. Identity and Authorization Across the Graph | The first tag where the graph refuses anybody. A symmetric-key JWT, minted by `scripts/mint-token.mjs`; `Customer.email`, `Query.customerById`, `Query.ordersByCustomer` and `submitReview` guarded; and the same rule written twice on purpose, once as a federation directive the router enforces and once as a `[Authorize]` the subgraph enforces, because all seven services listen on a host port. `Mosaic.Nodes` learns who is asking, so that `Query.node` cannot be pointed at somebody else's order, and Ordering's reference resolver learns whose order it is. Plus `scripts/auth-cases.mjs`, five cases about what a composer keeps and what it throws away, and `scripts/auth-run.mjs`, fifteen about what a running graph answers |

Later chapters add their tags here as they are written. The convention is `chNN`
for the end-of-chapter state, and `chNN-<step>` if a chapter needs an
intermediate one.

## What you need

- .NET SDK 10.0.302 or later in the same feature band (pinned in `global.json`)
- Docker. Since chapter 4 Mosaic keeps its data in PostgreSQL, and
  `docker-compose.yml` is the only description of it. Since chapter 12 there are
  six services and six databases in that one container
- Node, to run the Postman collections from the command line and, since chapter
  7, to compose supergraphs with `wgc`

Since chapter 15 four of the services and the router want a signing key in
`MOSAIC_JWT_SECRET`. `docker-compose.yml` supplies a development default, so
`docker compose up` needs nothing from you; a service started by hand with
`dotnet run` will refuse to start without it and say so. The value the compose
file uses is the one this prints:

```
node scripts/mint-token.mjs --secret
```

A token to go with it, for any field the graph now guards. The subject of a
Mosaic token is a customer's global object identifier, which is the same string
`Review.author { id }` hands a client:

```
node scripts/mint-token.mjs --customer Q3VzdG9tZXI6AAAAwAAAAECAAAAAAAAABA==
```

That key is symmetric and it is in a public repository, which means every party
holding it can mint tokens as well as check them. It is a development
convenience and it would be a finding anywhere else; the router's configuration
takes a JWKS url instead, and `router/config.yaml` has the two-line swap
commented beside the block it replaces.

## Running it

Seven services is more than a terminal each, so start them from the compose
file. The first run builds seven images and is slow:

```
docker compose up -d --build mosaic-db mosaic-catalog mosaic-pricing \
    mosaic-inventory mosaic-accounts mosaic-reviews mosaic-ordering \
    mosaic-nodes mosaic-router
docker compose ps
```

| Service | Port | Database | Owns |
|---------|------|----------|------|
| `mosaic-catalog` | 5101 | `catalog` | `Product`, and the root fields that find one |
| `mosaic-pricing` | 5102 | `pricing` | `Product.price`, `Product.shippingCost` |
| `mosaic-inventory` | 5103 | `inventory` | `Product.availableQuantity` |
| `mosaic-accounts` | 5104 | `accounts` | `Customer` |
| `mosaic-reviews` | 5105 | `reviews` | `Review`, `Product.reviews`, `Product.averageRating`, and the one mutation |
| `mosaic-ordering` | 5106 | `ordering` | `Order`, `OrderLine` |
| `mosaic-nodes` | 5107 | | `Query.node`, `Query.nodes` |
| `mosaic-router` | 3002 | | the graph |

Any one of them also runs from the SDK, which is what you want while changing
it:

```
dotnet run --project src/Mosaic.Pricing    # http://localhost:5102
```

Each service keeps its own database on that one PostgreSQL container and creates
and seeds it on first start:

```
info: Mosaic.Catalog.Data.CatalogDatabaseSeeder[0]
      Seeded 25 products into a newly created database.
```

```
info: Mosaic.Pricing.Data.PricingDatabaseSeeder[0]
      Seeded 25 prices into a newly created database.
```

No container was added for any of them. `EnsureCreatedAsync` creates the
database as well as the schema, so all six appeared on the server that was
already running, separate from each other.

Three of the seven have no root field at all. Pricing, Inventory and Reviews
contribute fields to entities somebody else owns, so the only way into them is
an `_entities` call. Open one in Nitro and `Query` has two fields on it, both
beginning with an underscore.

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

What neither of them can answer on its own is the query the earlier chapters
opened with:

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

Catalog has the titles and cannot price them. Pricing has the prices and cannot
list the products. Assembling that answer out of six services is a router's job,
and since chapter 10 there is one: start it and send the same query to
<http://localhost:3002/graphql> instead, where it answers. The section below is
about that. The way to ask one subgraph directly about a product it did not
find is still `_entities`, further down.

Since chapter 5 there is also a `Mutation` and a `Subscription`, and both are
Reviews' since chapter 12. Both take a product id, and the id comes from
Catalog: ask <http://localhost:5101/graphql> for
`{ browseProducts(first: 1) { nodes { id } } }` and paste the string it answers
with into <http://localhost:5105/graphql>. Open two Nitro tabs, subscribe in one
and write in the other:

```graphql
subscription { onReviewAdded(productId: "<a product id>") { rating body } }
```

That the same string means the same product in two services is not a
convenience. It is the federation key, and four of the six services carry their
own copy of the code that decodes it.

## The six subgraphs

`src/Mosaic.Catalog`, on 5101, owns the `Product` entity: `sku`, `title`,
`description`, `category`, and the four root fields that find products -
`products`, `browseProducts`, `productById` and `productBySku`.

`src/Mosaic.Pricing` on 5102, `src/Mosaic.Inventory` on 5103 and
`src/Mosaic.Reviews` on 5105 each declare the same `Product`, with the same key,
and contribute fields to it: `price` and `shippingCost`, `availableQuantity`,
and `reviews` with `averageRating`. Their own `Product` classes hold an id and,
in Pricing's case, an `@external` copy of the category that `shippingCost`
requires.

`src/Mosaic.Accounts`, on 5104, owns `Customer`, which became an entity in
chapter 12 because two other services reference one.

`src/Mosaic.Ordering`, on 5106, owns `Order` and `OrderLine` and contributes
nothing to anybody. It references `Product` and `Customer` and can resolve
neither, which its schema says with `resolvable: false` on both keys.

All six publish `_service`, which is how a composer reads a subgraph's schema,
and `_entities`, which is how anything asks a subgraph for an object by key. All
six schemas are committed, one file each under `schema/`, and
`federation/mosaic.yaml` names them and says where each answers. Chapter 8
creates that file with two entries and composes nothing with it; chapter 9 is
where composition is the subject, and chapter 12 takes it to six.

### Composition (chapter 9)

One command turns the two schemas into one file, with no account and no network:

```
npx wgc router compose -i federation/mosaic.yaml -o federation/supergraph.json
```

That output is a *router execution config*, not a supergraph schema. It carries
the client-facing schema as one string with no federation directives in it, a
routing table under `engineConfig.datasourceConfigurations` saying which
subgraph can answer which fields, and each subgraph's own SDL twice: verbatim
under `federation.serviceSdl`, and normalised in `engineConfig.stringStorage`
under a key that is the SHA-1 of its contents. `compatibilityVersion` is the
router contract version and the composition library version joined by a colon.

Chapter 9 prints pieces of that file, so unlike chapter 7's sample supergraph it
is committed rather than gitignored, and `verify.ps1` recomposes and compares.

The chapter also prints five composition errors, each produced by taking the
committed pair and applying exactly one edit:

```
node scripts/composition-cases.mjs --list
node scripts/composition-cases.mjs                  # assert every message
node scripts/composition-cases.mjs --print missing-key
```

The edits are literal string replacements that must match exactly once, so a
change to a committed schema that removes the text a case edits fails loudly
instead of quietly composing something nobody meant.

### The router (chapter 10)

The file above is what a router loads. Mosaic's is the Cosmo Router, and it is
a service in `docker-compose.yml` like the other three:

```
docker compose up -d mosaic-router      # http://localhost:3002/graphql
```

It needs no account and no registry, and it does not call a subgraph at
startup, so it will come up before either service does. Everything it knows
about Mosaic is in the two files it mounts: `federation/supergraph.json`, and
`router/config.yaml`, which is the router's own configuration rather than the
graph's. That file sets five things out of the 66 the router accepts, and each
one has a comment saying why.

The routing URLs in the composed config say `localhost:5101` through
`localhost:5106`, which inside a container would be the container. The router
rewrites them to `host.docker.internal` on its own -
`localhost_fallback_inside_docker` defaults to true - which keeps one composed
config working whether the subgraphs were started with `dotnet run` or by
compose. A deployment would set `overrides.subgraphs.routing_url` instead.

`config.yaml` turns on watching, so recomposing while the router is up swaps
the graph over without dropping a request:

```
npx wgc router compose -i federation/mosaic.yaml -o federation/supergraph.json
```

Query plans are on, because `dev_mode` is on. Ask for one with
`X-WG-Include-Query-Plan: true`, and add `X-WG-Skip-Loader: true` to get the
plan without executing it.

Six things about this router are worth reproducing rather than reading, three
from chapter 10 and three from chapter 15, and each is a case:

```
node scripts/router-cases.mjs --list
node scripts/router-cases.mjs                       # assert all six
node scripts/router-cases.mjs --print resolvability-off
```

They need Docker and both subgraphs running. The third is the interesting one:
a graph composed with `--disable-resolvability-validation` starts without a
murmur, answers anything that stays inside one subgraph, and returns HTTP 500
with `internal server error` to anything that crosses.

Two numbers chapter 10 prints are timings rather than behaviours, so they get a
script of their own rather than a case:

```
node scripts/measure-router.mjs            # what the router adds, in ms
node scripts/measure-router.mjs --reload   # how long a recompose takes to land
```

That one is deliberately not part of `verify.ps1`. Single-machine timings
asserted in a gate fail on a busier laptop, which teaches nobody anything. Run
it twice and compare the two runs before believing any difference.

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

Send the same representation to 5102 and put `price { amount currency }` in the
selection instead, and Pricing answers for the same product out of its own
tables. Send it to 5103 and ask for `availableQuantity`. No service can answer
for another's fields and none needs to: a representation carries `__typename`
and the key, and that is all any of them reads.

`Query.node` and `Query.nodes` are gone from all six. Two subgraphs cannot both
declare them, and `@shareable` would be a lie, because no service can resolve
another's node types. The `Node` interface stayed, and so did the global
identifiers, which is what makes the key above work at all. Chapter 13 is where
a federated `node` field comes back.

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

Since chapter 10 it also starts Mosaic's own router, runs a third collection
against it - the storefront query, the plan behind it, and the fields the router
does not expose - and then runs `scripts/router-cases.mjs`. Pass `-SkipRouter`
(or set `MOSAIC_SKIP_ROUTER=1`) to leave that out.

Since chapter 7 the run finishes with the federated-wire sample: it composes the
two subgraph schemas, starts both subgraphs and the router, runs a fourth
collection against all three, and then checks the subgraph logs for the two
requests the chapter prints. That last check is the interesting one, because the
collection reads the router's own account of what it planned and this reads what
the subgraphs actually received. Pass `-SkipWire` (or set `MOSAIC_SKIP_WIRE=1`)
to leave it out; it is the slowest section.

The two router sections publish the same port and run in sequence, chapter 10's
first. Neither leaves its container behind.

## Layout

```
src/Mosaic.ServiceDefaults/  the platform, and the only project here that is
                         not a service. What all six do the same way, and
                         nothing any two of them have to agree about
  Counting/              the lookup counter
  Data/                  the SQL command counter and the snake_case convention
  Diagnostics/           the pipeline report and the per-request timeline
  MosaicSubgraphDefaults.cs  the builder calls that make a service composable
src/Mosaic.Catalog/      the Catalog subgraph, on 5101
  Catalog/               products: identity, description, category
    Data/                the DbContext, the seeder, the service and its DataLoader
    Model/               the Product record, its key decoder, and the reference
                         resolver that turns a representation back into one
    Types/               the root fields, and Product's id and node resolver
  Federation/            PageCursor, marked shareable so both graphs may declare it
src/Mosaic.Pricing/      what a product costs, on 5102
  Catalog/Model/         a Product stub with a reference resolver, an @external
                         copy of the category shippingCost requires, and a copy
                         of the key decoder
  Pricing/               prices, shipping rates, and Money
src/Mosaic.Inventory/    whether you can have one, on 5103
src/Mosaic.Accounts/     who the customers are, on 5104. Owns the Customer
                         entity and the only CustomerKey decoder
src/Mosaic.Reviews/      what customers thought, on 5105. The only service that
                         can be written to, and the only one with a subscription
  Errors/                the Error interface every domain error implements
src/Mosaic.Ordering/     what they bought, on 5106. References Product and
                         Customer and resolves neither
federation/mosaic.yaml   which subgraphs the graph is made of, and where each
                         one answers
federation/supergraph.json  the composed graph, committed since chapter 9 and
                         mounted by the router since chapter 10
router/config.yaml       the router's own configuration, which is about the
                         process rather than about the graph
samples/three-approaches/  the same tiny schema, three authoring styles
samples/resolver-scopes/   what [UseRequestScope] changes, in two fields
samples/federated-wire/    two subgraphs and a router, so the traffic between
                           them can be read; chapter 7
samples/entity-attribute-placement/
                           the same entity written four ways, to show where
                           [Key] and [ReferenceResolver] may go; chapter 8
samples/entity-resolution/ two subgraphs built to be watched: what _entities
                           does with a list, and what @provides hides; chapter 11
schema/                  committed SDL snapshots, one per service, plus
                         streams.graphql - a subgraph with no service, whose
                         one field the router resolves off NATS; chapter 14
postman/                 collections and environments
scripts/                 verify.ps1 and verify.sh, plus the case runners they
                         both call: composition-cases.mjs, router-cases.mjs,
                         entity-cases.mjs, override-cases.mjs,
                         modeling-cases.mjs and realtime-cases.mjs, and
                         subscription-run.mjs, which opens both subscriptions
                         through the router and asserts one write reaches both
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
