using Mosaic.Catalog;
using Mosaic.Catalog.Data;
using Mosaic.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Chapter 12. Catalog had none of this: the request timeline, the resolver
// count and the two counters were all built inside Mosaic.Api in chapters 3 and
// 4, and chapter 8's extraction left them there. For four chapters this service
// could not say what any request had cost it, which is what chapter 10 meant by
// half of every federated query being unobserved. Three lines fix the half that
// was a missing registration. The half that is a missing trace is chapter 23's.
builder.Services.AddMosaicServiceDefaults();

// Catalog's own database, on the PostgreSQL server all six services share. EF
// Core creates it on first start, so nothing in docker-compose.yml had to learn
// about this service to give it storage.
builder.Services.AddCatalogDatabase(
    builder.Configuration.GetConnectionString("Catalog")
        ?? throw new InvalidOperationException(
            "No connection string named 'Catalog'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Catalog in the environment."));

// One domain. Mosaic.Api's equivalent line listed five until chapter 12, and
// there is no Mosaic.Api now.
builder.Services.AddCatalogDomain();

builder.AddGraphQL()
    // AddApolloFederation, registerNodeInterface: false, the two cost options,
    // the application-scoped logger factory and the timeline listener. These
    // were five separate calls in this file with a long comment against each,
    // and the comments are still there - in Mosaic.ServiceDefaults, once,
    // instead of six times.
    .AddMosaicSubgraph()
    .AddCatalog()
    .RegisterDbContextFactory<CatalogDbContext>()
    // browseProducts still pages, filters, sorts and projects exactly as
    // chapter 4 left it. Neither extraction touched the field.
    .AddFiltering()
    .AddSorting()
    .AddPagingArguments();

builder.Services.AddMosaicPipelineReport();

var app = builder.Build();

app.UseMosaicServiceDefaults();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
