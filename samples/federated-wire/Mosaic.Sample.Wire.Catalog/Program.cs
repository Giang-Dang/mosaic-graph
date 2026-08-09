using Mosaic.Sample.Wire;

var builder = WebApplication.CreateBuilder(args);

// Everything the router sends this subgraph, and everything it gets back,
// written to the console. See WireLogging.cs for why the built-in middleware
// rather than a proxy.
builder.AddWireLogging();

builder.AddGraphQL()
    .AddApolloFederation()
    .AddWireCatalog();

var app = builder.Build();

app.UseHttpLogging();
app.MapGraphQL();

app.RunWithGraphQLCommands(args);
