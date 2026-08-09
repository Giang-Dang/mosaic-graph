using Mosaic.Sample.Entities.Catalog;

var builder = WebApplication.CreateBuilder(args);

// The store is a singleton so its counter survives between requests, which is
// the whole reason the log is readable at all.
builder.Services.AddSingleton<WidgetStore>();

builder.AddGraphQL()
    .AddApolloFederation()
    .AddEntitiesCatalog()
    // Gadget has no root field pointing at it, and a type nothing reaches is a
    // type that never enters the schema. Chapter 7 met the same rule from the
    // other side: a subgraph with no reachable entity publishes no _entities
    // field at all.
    .AddType<Gadget>();

var app = builder.Build();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
