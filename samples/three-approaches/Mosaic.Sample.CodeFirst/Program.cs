using Mosaic.Sample.CodeFirst;

var builder = WebApplication.CreateBuilder(args);

// Every type is named here. Nothing is discovered: a type that is not reachable
// from AddQueryType through AddType is not in the schema.
builder.AddGraphQL()
    .AddQueryType<QueryType>()
    .AddType<ProductType>();

var app = builder.Build();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
