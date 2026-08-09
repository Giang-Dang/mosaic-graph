using Mosaic.Api.Accounts;
using Mosaic.Api.Catalog;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Infrastructure.Data;
using Mosaic.Api.Infrastructure.Diagnostics;
using Mosaic.Api.Infrastructure.Errors;
using Mosaic.Api.Inventory;
using Mosaic.Api.Ordering;
using Mosaic.Api.Pricing;
using Mosaic.Api.Reviews;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ServiceCallCounter>();

// One timeline per request, read back through RequestServices. Scoped, not
// singleton: the pipeline phases, the resolvers and the command interceptor all
// have to write to the same instance, and only for as long as the request lives.
builder.Services.AddScoped<RequestTimeline>();

// PostgreSQL, a pooled context factory, and the start-up seeder. The
// connection string is the only piece of Mosaic that differs between running it
// with `dotnet run` and running it under docker compose.
builder.Services.AddMosaicDatabase(
    builder.Configuration.GetConnectionString("Mosaic")
        ?? throw new InvalidOperationException(
            "No connection string named 'Mosaic'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Mosaic in the environment."));

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
    // RegisterDbContextFactory only teaches the resolver compiler how to build
    // a MosaicDbContext parameter. AddMosaicDatabase above is what registers
    // the factory itself; without it this line compiles and fails at runtime.
    .RegisterDbContextFactory<MosaicDbContext>()
    // The where and order arguments on browseProducts. Between them these two
    // also register QueryContext<T>, which is what carries the projection, so
    // there is no separate AddProjections() call.
    .AddFiltering()
    .AddSorting()
    // first / after / last / before, and the PagingArguments parameter that
    // hands them to a resolver.
    .AddPagingArguments()
    // The Node interface and the node / nodes root fields. At least one type
    // has to implement Node or the schema fails to build; four do, each
    // through a [NodeResolver] method in its own domain folder.
    .AddGlobalObjectIdentification()
    // One input argument and one payload type per mutation, generated.
    // applyToAllMutations: true is the choice that matters - the alternative
    // is a per-mutation attribute, and a convention nobody can opt out of
    // silently is worth more than one that is applied by hand.
    .AddMutationConventions(applyToAllMutations: true)
    // Replaces the built-in `Error` interface, which carries only a message,
    // with Mosaic's, which also carries a code. Every type on a payload's
    // error union has to satisfy it.
    .AddErrorInterfaceType<IMosaicError>()
    // Subscriptions, delivered through an in-process pub/sub. Nothing outside
    // this process can publish to it and nothing outside this process can hear
    // it, which is exactly as far as a single service goes.
    .AddInMemorySubscriptions()
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

// Subscriptions need a connection that stays open. Server-sent events work
// through MapGraphQL with nothing added; graphql-ws needs this line, and
// leaving it out fails at the handshake rather than at start-up.
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapGraphQL();

app.RunWithGraphQLCommands(args);
