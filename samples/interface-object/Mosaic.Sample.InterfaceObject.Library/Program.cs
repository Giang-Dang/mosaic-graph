using Mosaic.Sample.InterfaceObject.Library;

var builder = WebApplication.CreateBuilder(args);

builder.AddGraphQL()
    .AddApolloFederation()
    // Both implementations, by hand. Query.library returns the interface, and
    // an interface tells the schema builder nothing about who implements it:
    // leave these out and the build fails with "There is no object type
    // implementing interface `Media`", which is accurate and says nothing
    // about how to fix it. The rule is that every implementation of an
    // interface has to be reachable from a root field or registered.
    .AddType<Book>()
    .AddType<Film>()
    // The same two cost settings the six real subgraphs carry, for the reason
    // chapter 8 records: the composer's own @cost definition takes an Int and
    // HotChocolate's automatic weights are strings, so a schema carrying them
    // does not compose.
    .ModifyCostOptions(options =>
    {
        options.ApplyCostDefaults = false;
        options.ApplySlicingArgumentDefaultValue = false;
    })
    .AddInterfaceObjectLibrary();

var app = builder.Build();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
