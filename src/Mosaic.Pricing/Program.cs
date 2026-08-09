using Mosaic.Pricing;
using Mosaic.Pricing.Data;
using Mosaic.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Every Mosaic service makes these three registrations: the HTTP context
// accessor the counters hang off, the lookup counter, and the per-request
// timeline. They lived in Mosaic.Api until chapter 12 and only Mosaic.Api had
// them, which is why chapter 10 could measure the router from outside all three
// processes and from inside exactly one.
builder.Services.AddMosaicServiceDefaults();

// Pricing's own database, on the PostgreSQL server all six services share. EF
// Core creates it on first start, so docker-compose.yml gained a service and no
// storage - the same trick chapter 8 used for Catalog, five more times.
builder.Services.AddPricingDatabase(
    builder.Configuration.GetConnectionString("Pricing")
        ?? throw new InvalidOperationException(
            "No connection string named 'Pricing'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Pricing in the environment."));

// One domain. Mosaic.Api's equivalent line listed five, and that line is the
// clearest measure of what this chapter did.
builder.Services.AddPricingDomain();

builder.AddGraphQL()
    // AddApolloFederation, registerNodeInterface: false, the two cost options,
    // the application-scoped logger factory and the timeline listener. Five
    // calls that every subgraph has to get right and that break the graph
    // rather than the service when it does not; see Mosaic.ServiceDefaults.
    .AddMosaicSubgraph()
    // Pricing has no root field of its own, and this line is what that costs.
    // The source generator emits a query type when it finds a [QueryType]
    // class; there is none here, so the schema build fails with "unable to
    // identify the query type of the schema" and never mentions federation.
    // AddApolloFederation puts _service and _entities on the query type. It
    // does not create one.
    //
    // Three of the six subgraphs are in this position - Pricing, Inventory and
    // Reviews - and it is the right shape rather than an omission. Everything
    // they contribute hangs off an entity somebody else owns, so the only way
    // in is a representation the router builds.
    .AddQueryType()
    .AddPricing()
    .RegisterDbContextFactory<PricingDbContext>();

// After AddGraphQL, never before: the cost analyzer inserts its middleware
// through a pipeline modifier and modifiers run in registration order.
builder.Services.AddMosaicPipelineReport();

var app = builder.Build();

// The lookup counter and /health.
app.UseMosaicServiceDefaults();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
