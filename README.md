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

Later chapters add their tags here as they are written. The convention is `chNN`
for the end-of-chapter state, and `chNN-<step>` if a chapter needs an
intermediate one.

## What you need

- .NET SDK 10.0.302 or later in the same feature band (pinned in `global.json`)
- Docker, if you want to run it in a container
- Node, only to run the Postman collection from the command line

## Running it

```
dotnet run --project src/Mosaic.Api
```

The service listens on <http://localhost:5100>. Open
<http://localhost:5100/graphql> in a browser and HotChocolate serves Nitro, its
built-in IDE. Try:

```graphql
{
  products {
    title
    price { amount currency }
    availableQuantity
    averageRating
  }
}
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

This is the gate a chapter tag has to pass. It restores and builds the solution
in Release with warnings as errors, regenerates the schema and fails if it has
drifted from the committed `schema/mosaic.graphql`, checks that the three
sample projects still produce byte-identical SDL, starts the service, asserts
the seeded catalog answers with 25 products and 120 reviews, and runs the
Postman collection.

The Postman collection needs newman, which is pinned as a local dev dependency:

```
npm ci
npx newman run postman/mosaic.postman_collection.json \
    -e postman/mosaic.local.postman_environment.json
```

## Layout

```
src/Mosaic.Api/          the service; one folder per domain
  Catalog/               products: identity, description, category
  Pricing/               what a product costs
  Inventory/             whether you can have one
  Reviews/               what customers thought
  Accounts/              who the customers are
  Ordering/              what they bought
  Infrastructure/        the lookup counter and its options
    Diagnostics/         the pipeline report and the per-request timeline
samples/three-approaches/  the same tiny schema, three authoring styles
samples/resolver-scopes/   what [UseRequestScope] changes, in two fields
schema/                  committed SDL snapshots
postman/                 collection and environment
scripts/                 verify.ps1 and verify.sh
```

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
overload, deliberately. Against a `List<T>` in memory nobody notices, which is
exactly why this pattern reaches production. Chapter 4 replaces the data layer
and brings that number down.

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
    (document cache miss, operation cache miss, 146 resolvers)
```

Send the same query twice and the second one reports both caches hitting, with
validation and compilation skipped entirely. `parse` never fires over HTTP: the
transport parses the document before the execution pipeline runs, which is also
why a syntax error never produces a timeline line at all.

The resolver count matches the lookup count because every resolver here does
exactly one domain-service lookup. Plain record properties - `title`, `rating`,
`displayName` - are not resolvers and never appear in it.

To see what `[UseRequestScope]` changes:

```
dotnet run --project samples/resolver-scopes
```

then ask for two default-scope fields and two request-scope ones in a single
query. The default ones each get their own service scope; the annotated ones
share the request's.
