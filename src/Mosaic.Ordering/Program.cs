using Mosaic.Ordering;
using Mosaic.Ordering.Data;
using Mosaic.ServiceDefaults;
using Mosaic.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMosaicServiceDefaults();

// Chapter 15. Every order in this database belongs to somebody, and this is
// the service that knows which somebody. Both of its routes in are guarded:
// the root field, and the reference resolver the node field reaches through.
builder.Services.AddMosaicSecurity(builder.Configuration);

builder.Services.AddOrderingDatabase(
    builder.Configuration.GetConnectionString("Ordering")
        ?? throw new InvalidOperationException(
            "No connection string named 'Ordering'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Ordering in the environment."));

builder.Services.AddOrderingDomain();

builder.AddGraphQL()
    .AddMosaicSubgraph()
    .AddMosaicAuthorization()
    .AddOrdering()
    .RegisterDbContextFactory<OrderingDbContext>();

builder.Services.AddMosaicPipelineReport();

var app = builder.Build();

app.UseMosaicServiceDefaults();

app.UseMosaicSecurity();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
