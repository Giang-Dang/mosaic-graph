using Mosaic.Sample.SchemaFirst;

const string Sdl =
    """
    type Query {
      productById(id: ID!): Product
    }

    type Product {
      id: ID!
      sku: String!
      title: String!
    }
    """;

var builder = WebApplication.CreateBuilder(args);

// The SDL above is the schema; C# never adds a field to it. The two calls after
// it only say which CLR type stands behind Product and which class holds the
// Query resolvers. Names are matched as strings, so a typo here is a startup
// error rather than a compile error.
builder.AddGraphQL()
    .AddDocumentFromString(Sdl)
    .BindRuntimeType<Product>("Product")
    .AddResolver<QueryResolvers>("Query");

var app = builder.Build();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);
