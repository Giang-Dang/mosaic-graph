# Three approaches, one schema

Three ASP.NET Core services that publish the same GraphQL schema, byte for byte,
written three different ways. The schema is a deliberately small slice of Mosaic:

```graphql
type Query {
  productById(id: ID!): Product
}

type Product {
  id: ID!
  sku: String!
  title: String!
}
```

Each project hard-codes the same three products and depends on nothing in
`src/Mosaic.Api`. That is on purpose: later chapters rewrite the real service,
and these samples have to keep compiling and producing the same SDL when they do.

All three run on HotChocolate 16.6.0 and .NET 10, with versions coming from the
repository's `Directory.Packages.props`.

| Project | Port | Package it needs beyond the server | Schema lives in |
| --- | --- | --- | --- |
| `Mosaic.Sample.ImplementationFirst` | 5101 | `HotChocolate.Types.Analyzers` | C# attributes, read by a source generator |
| `Mosaic.Sample.CodeFirst` | 5102 | none | `ObjectType<T>` descriptor classes |
| `Mosaic.Sample.SchemaFirst` | 5103 | none | an SDL string, bound to CLR types at startup |

## What each one shows

### Implementation-first (port 5101)

The style `src/Mosaic.Api` itself uses. `[QueryType]` and `[ObjectType<Product>]`
mark `public static partial class`es; `HotChocolate.Types.Analyzers` finds every
marked type in the assembly at compile time and generates one registration
method. The name of that method comes from the assembly-level attribute in
`Properties/ModuleInfo.cs`: `[assembly: Module("Catalog")]` generates
`AddCatalog()`.

The consequence to notice is in `Program.cs`. It names no type at all, and it
needs no `using` for the sample's own namespace. Adding a type to the schema
means adding a file with an attribute on it; nothing central changes.

### Code-first (port 5102)

Descriptor classes. `ProductType : ObjectType<Product>` and
`QueryType : ObjectType<Query>` override `Configure` and write out each field's
name, argument and GraphQL type by hand. Nothing is discovered: `Program.cs`
lists `AddQueryType<QueryType>()` and `AddType<ProductType>()`, and a type that
is not reachable from that list is not in the schema.

More typing than the other two, and the schema is spread across C# classes, but
the descriptor API is where the fine-grained knobs live (directives, custom
resolvers per field, per-field middleware), so this is the fallback when
attributes cannot express something.

### Schema-first (port 5103)

The SDL is the source of truth and lives in `Program.cs` as a raw string
literal. C# contributes two things: `BindRuntimeType<Product>("Product")` says
which CLR type stands behind the `Product` type, and
`AddResolver<QueryResolvers>("Query")` says which class holds the Query
resolvers. Methods bind to fields by name with the `Get` prefix dropped, so
`GetProductById` answers `productById`.

Two things are worth knowing before you copy this.

**The v16 documentation has no schema-first page.** The API is shipped and
supported, but the pages that rank in search are from v10 to v14 and are wrong
for 16.x. The wiring above was verified against the 16.6.0 assemblies and the
16.6.0 test suite (`Core/test/Types.Tests/SchemaFirstTests.cs` and
`Core/test/Execution.Tests/Integration/HelloWorldSchemaFirst/`), then by
compiling and running it.

**A resolver class is not interchangeable with an `AddResolver` delegate.** The
delegate form works:

```csharp
.AddResolver("Query", "productById", ctx => Catalog.ById(ctx.ArgumentValue<Guid>("id")))
```

It compiles, and HotChocolate coerces the `ID!` argument into a `Guid`. But a
delegate is opaque to the cost analyzer, which cannot tell it is cheap and so
assigns it the default resolver cost of 10. The exported SDL then reads

```graphql
productById(id: ID!): Product @cost(weight: "10")
```

and no longer matches the other two samples. The resolver *class* is compiled by
HotChocolate, which can see the method is synchronous and takes nothing but
field arguments, treats it as a pure resolver, and adds no cost directive.

If you would rather keep the SDL in its own file, `AddDocumentFromFile(path)`
exists in 16.6.0 and takes a path resolved against the process working
directory. The string literal is used here so the whole comparison fits on one
page.

## Running them

From the repository root:

```
dotnet run --project samples/three-approaches/Mosaic.Sample.ImplementationFirst
dotnet run --project samples/three-approaches/Mosaic.Sample.CodeFirst
dotnet run --project samples/three-approaches/Mosaic.Sample.SchemaFirst
```

Ports come from each project's `Properties/launchSettings.json`. Nitro is at
`http://localhost:5101/graphql` and the two ports after it. A query that works
against all three:

```graphql
{
  productById(id: "a0000000-0000-4000-8000-000000000002") {
    id
    sku
    title
  }
}
```

Or over curl:

```
curl -s -X POST http://localhost:5101/graphql \
  -H "Content-Type: application/json" \
  -d '{"query":"{ productById(id: \"a0000000-0000-4000-8000-000000000002\") { id sku title } }"}'
```

## Exporting and diffing the SDL

Each project ends with `app.RunWithGraphQLCommands(args)` and references
`HotChocolate.AspNetCore.CommandLine`, which is what makes `schema export` work,
exactly as in the main service. The working directory for `dotnet run` is the
project folder, so pass an absolute output path:

```
dotnet run --project samples/three-approaches/Mosaic.Sample.ImplementationFirst \
  -- schema export --output <repo>/schema/samples/implementation-first.graphql
dotnet run --project samples/three-approaches/Mosaic.Sample.CodeFirst \
  -- schema export --output <repo>/schema/samples/code-first.graphql
dotnet run --project samples/three-approaches/Mosaic.Sample.SchemaFirst \
  -- schema export --output <repo>/schema/samples/schema-first.graphql
```

The export writes a `<name>-settings.json` next to each SDL file. That dump is
not an artifact worth keeping; delete it.

The three files under `schema/samples/` are checked in and are identical:

```
cd schema/samples
sha256sum implementation-first.graphql code-first.graphql schema-first.graphql
cmp implementation-first.graphql code-first.graphql
cmp implementation-first.graphql schema-first.graphql
```

The running servers agree too, which is the stronger check because it does not
depend on the exporter: `curl -s http://localhost:5101/graphql?sdl` returns the
same bytes on all three ports.
