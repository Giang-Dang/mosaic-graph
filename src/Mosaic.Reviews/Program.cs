using Mosaic.Reviews;
using Mosaic.Reviews.Data;
using Mosaic.Reviews.Errors;
using Mosaic.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMosaicServiceDefaults();

builder.Services.AddReviewsDatabase(
    builder.Configuration.GetConnectionString("Reviews")
        ?? throw new InvalidOperationException(
            "No connection string named 'Reviews'. Start the database with "
            + "`docker compose up -d mosaic-db`, or set "
            + "ConnectionStrings__Reviews in the environment."));

builder.Services.AddReviewsDomain();

// Chapter 14. The broker Reviews announces on, and the only configuration in
// this file that is not a database. It has a default because a reader running
// one service by hand should not have to set an environment variable to get a
// service that starts; a broker that is not there costs a warning per review
// and nothing else.
builder.Services.AddReviewStreams(
    builder.Configuration.GetConnectionString("Nats") ?? "nats://localhost:4222");

// The largest of the five Program.cs files chapter 12 wrote, and the reason is
// that Reviews is the only one of the six subgraphs that can be written to.
// Four of the calls below exist for the mutation and the subscription; the
// other four services need none of them.
builder.AddGraphQL()
    .AddMosaicSubgraph()
    // No root field, despite having a mutation and a subscription. A review is
    // reached through the product it is about, or through the identifier of one
    // a client has just submitted, and neither is a root field on this service.
    // See the longer comment in Mosaic.Pricing's Program.cs.
    .AddQueryType()
    .AddReviews()
    .RegisterDbContextFactory<ReviewsDbContext>()
    // Product.reviews is a connection, so this service needs the four paging
    // arguments and the PagingArguments parameter that hands them to a
    // resolver. Catalog is the only other subgraph with a connection.
    .AddPagingArguments()
    // One input argument and one payload type per mutation, generated.
    .AddMutationConventions(applyToAllMutations: true)
    // Replaces the built-in `Error` interface, which carries only a message,
    // with Mosaic's, which also carries a code.
    .AddErrorInterfaceType<IMosaicError>()
    // Subscriptions, delivered through an in-process pub/sub. Nothing outside
    // this process can publish to it and nothing outside this process can hear
    // it, and that was already the limit before the split. What the split adds
    // is that a subscription now lives behind a router that has to know how to
    // carry one. Chapter 14 owns that; at tag ch12 onReviewAdded still works
    // against this service directly and is not exercised through the router.
    .AddInMemorySubscriptions();

builder.Services.AddMosaicPipelineReport();

var app = builder.Build();

app.UseMosaicServiceDefaults();

// Server-sent events work through MapGraphQL with nothing added; graphql-ws
// needs this line, and leaving it out fails at the handshake rather than at
// start-up.
app.UseWebSockets();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
