using Mosaic.Sample.Wire;
using Mosaic.Sample.Wire.Reviews;

var builder = WebApplication.CreateBuilder(args);

// Everything the router sends this subgraph, and everything it gets back,
// written to the console. See WireLogging.cs for why the built-in middleware
// rather than a proxy.
builder.AddWireLogging();

builder.AddGraphQL()
    .AddApolloFederation()
    .AddWireReviews()
    // Product is not reachable from any root field of this subgraph, and
    // HotChocolate only builds types it can reach. Without this line the
    // service starts and publishes a schema with no Product and no _entities.
    .AddType<Product>();

var app = builder.Build();

app.UseHttpLogging();
app.MapGraphQL();

app.RunWithGraphQLCommands(args);
