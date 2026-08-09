using Mosaic.Catalog;
using Mosaic.Catalog.Data;

var builder = WebApplication.CreateBuilder(args);

// Catalog's own database, on the same PostgreSQL server Mosaic uses. EF Core
// creates it on first start, so nothing in docker-compose.yml had to learn
// about this service to give it storage.
builder.Services.AddCatalogDatabase(
    builder.Configuration.GetConnectionString("Catalog")
        ?? throw new InvalidOperationException(
            "No connection string named 'Catalog'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Catalog in the environment."));

// One domain. Mosaic.Api's equivalent line lists five.
builder.Services.AddCatalogDomain();

builder.AddGraphQL()
    // The line that makes this a subgraph rather than a GraphQL service.
    // It adds _service and _entities to Query, the _Any scalar, the _Entity
    // union built from every type carrying [Key], and the @link that declares
    // which version of the federation specification this schema is written
    // against.
    .AddApolloFederation()
    .AddCatalog()
    .RegisterDbContextFactory<CatalogDbContext>()
    // browseProducts still pages, filters, sorts and projects exactly as
    // chapter 4 left it. Extraction did not touch the field.
    .AddFiltering()
    .AddSorting()
    .AddPagingArguments()
    // registerNodeInterface: false is the one deliberate loss of this chapter.
    // It keeps the node id serializer, so Product.id is still the global
    // identifier chapter 5 designed and still encodes the type name beside the
    // key - which matters more than ever now, because that string is the
    // federation key both services have to agree on. What it drops is
    // Query.node and Query.nodes. Two subgraphs both declaring those fields is
    // a composition error, and @shareable would be a lie: neither service can
    // resolve the other's node types. Chapter 13 is where a federated node
    // field comes back.
    .AddGlobalObjectIdentification(registerNodeInterface: false)
    // Two things HotChocolate publishes that the Cosmo composer refuses to
    // read, and both are version skew rather than a mistake in either tool.
    // HotChocolate writes @cost(weight: "10") with a String, following the
    // current cost specification draft; wgc 0.129.7 carries a definition whose
    // weight is an Int!, and rejects every field the analyzer stamped. And
    // @listSize arrives with a slicingArgumentDefaultValue argument the
    // composer's definition does not declare - HotChocolate's own option for
    // it is documented as "the non-spec slicing argument default value", so
    // the library knows.
    //
    // Turning the defaults off does not turn cost analysis off: the limits and
    // the analyzer are separate settings, and an explicit [Cost] still counts.
    // What is lost is the automatic weight on every resolver-backed field, and
    // with it the numbers chapter 5 printed for Product.reviews. Chapter 25 is
    // where cost is a subject rather than a composition problem.
    .ModifyCostOptions(options =>
    {
        options.ApplyCostDefaults = false;
        options.ApplySlicingArgumentDefaultValue = false;
    });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapGraphQL();

app.RunWithGraphQLCommands(args);
