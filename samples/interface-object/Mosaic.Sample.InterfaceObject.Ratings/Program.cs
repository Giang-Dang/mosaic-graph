using Mosaic.Sample.InterfaceObject.Ratings;

var builder = WebApplication.CreateBuilder(args);

builder.AddGraphQL()
    .AddApolloFederation()
    // No root field returns a Media, because this subgraph cannot enumerate
    // them. Nothing would put the type in the schema, so it is registered by
    // hand; without this line the service starts, composes and contributes
    // nothing, which is a quieter failure than it deserves to be.
    .AddType<Media>()
    .ModifyCostOptions(options =>
    {
        options.ApplyCostDefaults = false;
        options.ApplySlicingArgumentDefaultValue = false;
    })
    .AddInterfaceObjectRatings();

var app = builder.Build();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
