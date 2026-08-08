using Mosaic.Api.Accounts;
using Mosaic.Api.Catalog;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Infrastructure.Diagnostics;
using Mosaic.Api.Inventory;
using Mosaic.Api.Ordering;
using Mosaic.Api.Pricing;
using Mosaic.Api.Reviews;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MosaicDataOptions>(builder.Configuration.GetSection("Mosaic:Data"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ServiceCallCounter>();

// One timeline per request, read back through RequestServices. Scoped, not
// singleton: the pipeline phases and the resolvers all have to write to the
// same instance, and only for as long as the request lives.
builder.Services.AddScoped<RequestTimeline>();

// Mosaic's six domains. Today they are six folders in one deployable, and this
// list is the only place that fact is written down.
builder.Services
    .AddCatalogDomain()
    .AddPricingDomain()
    .AddInventoryDomain()
    .AddReviewsDomain()
    .AddAccountsDomain()
    .AddOrderingDomain();

builder.AddGraphQL()
    .AddMosaic()
    // A diagnostic listener is built from the schema service provider, which
    // does not inherit the application's registrations. Drop the next line and
    // startup fails with "Unable to resolve service for type
    // 'Microsoft.Extensions.Logging.ILoggerFactory' while attempting to
    // activate 'RequestTimelineListener'".
    .AddApplicationService<ILoggerFactory>()
    .AddDiagnosticEventListener<RequestTimelineListener>();

// Registered after AddGraphQL on purpose. Pipeline modifiers run in
// registration order, and the cost analyzer that AddGraphQL installs inserts
// its middleware through one. Report first and you log the pipeline as it
// stood before that insertion, which is a shorter and wrong list.
builder.Services.AddPipelineReport();

var app = builder.Build();

app.UseServiceCallCounting();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapGraphQL();

app.RunWithGraphQLCommands(args);
