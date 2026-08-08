using Mosaic.Api.Accounts;
using Mosaic.Api.Catalog;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Inventory;
using Mosaic.Api.Ordering;
using Mosaic.Api.Pricing;
using Mosaic.Api.Reviews;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MosaicDataOptions>(builder.Configuration.GetSection("Mosaic:Data"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ServiceCallCounter>();

// Mosaic's six domains. Today they are six folders in one deployable, and this
// list is the only place that fact is written down.
builder.Services
    .AddCatalogDomain()
    .AddPricingDomain()
    .AddInventoryDomain()
    .AddReviewsDomain()
    .AddAccountsDomain()
    .AddOrderingDomain();

builder.AddGraphQL().AddMosaic();

var app = builder.Build();

app.UseServiceCallCounting();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapGraphQL();

app.RunWithGraphQLCommands(args);
