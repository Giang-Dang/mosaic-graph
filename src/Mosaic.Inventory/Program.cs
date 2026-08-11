using Mosaic.Inventory;
using Mosaic.Inventory.Data;
using Mosaic.ServiceDefaults;
using Mosaic.ServiceDefaults.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMosaicServiceDefaults();

// Chapter 15. Every service validates the same tokens the same way; the
// key arrives through configuration and lives in nobody's source file.
builder.Services.AddMosaicJwtAuthentication(builder.Configuration);

builder.Services.AddInventoryDatabase(
    builder.Configuration.GetConnectionString("Inventory")
        ?? throw new InvalidOperationException(
            "No connection string named 'Inventory'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Inventory in the environment."));

builder.Services.AddInventoryDomain();

builder.AddGraphQL()
    .AddMosaicSubgraph()
    // No root field. See the longer comment in Mosaic.Pricing's Program.cs:
    // AddApolloFederation puts _service and _entities on a query type and does
    // not create one, so a subgraph that contributes only entity fields has to
    // ask for an empty Query itself.
    .AddQueryType()
    .AddInventory()
    .RegisterDbContextFactory<InventoryDbContext>();

builder.Services.AddMosaicPipelineReport();

var app = builder.Build();

app.UseMosaicServiceDefaults();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
