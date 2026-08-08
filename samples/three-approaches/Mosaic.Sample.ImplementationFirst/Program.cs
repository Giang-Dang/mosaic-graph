var builder = WebApplication.CreateBuilder(args);

// Nothing here names Query or Product. The source generator found them from the
// [QueryType] and [ObjectType<Product>] attributes and wrote AddCatalog() for us.
builder.AddGraphQL().AddCatalog();

var app = builder.Build();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
