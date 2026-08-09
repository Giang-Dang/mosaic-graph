using Mosaic.Ordering;
using Mosaic.Ordering.Data;
using Mosaic.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMosaicServiceDefaults();

builder.Services.AddOrderingDatabase(
    builder.Configuration.GetConnectionString("Ordering")
        ?? throw new InvalidOperationException(
            "No connection string named 'Ordering'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Ordering in the environment."));

builder.Services.AddOrderingDomain();

builder.AddGraphQL()
    .AddMosaicSubgraph()
    .AddOrdering()
    .RegisterDbContextFactory<OrderingDbContext>();

builder.Services.AddMosaicPipelineReport();

var app = builder.Build();

app.UseMosaicServiceDefaults();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
