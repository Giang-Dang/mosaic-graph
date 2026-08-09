var builder = WebApplication.CreateBuilder(args);

builder.AddGraphQL()
    .AddApolloFederation()
    .AddEntityAttributePlacement();

var app = builder.Build();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
