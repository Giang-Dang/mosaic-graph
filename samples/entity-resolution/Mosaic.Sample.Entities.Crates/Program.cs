var builder = WebApplication.CreateBuilder(args);

builder.AddGraphQL()
    .AddApolloFederation()
    .AddEntitiesCrates();

var app = builder.Build();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
