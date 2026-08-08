using Mosaic.Sample.ResolverScopes;

var builder = WebApplication.CreateBuilder(args);

// Scoped, so each service scope constructs its own probe and the id changes
// whenever the scope does.
builder.Services.AddScoped<ScopeProbe>();

builder.AddGraphQL().AddScopes();

var app = builder.Build();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
