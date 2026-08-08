namespace Mosaic.Sample.CodeFirst;

/// <summary>
/// The entry into the graph, described field by field. Nothing is inferred:
/// the field name, its argument, its type and its resolver are all spelled out.
/// </summary>
public sealed class QueryType : ObjectType<Query>
{
    protected override void Configure(IObjectTypeDescriptor<Query> descriptor)
    {
        descriptor.Name("Query");

        descriptor
            .Field(q => q.GetProductById(default))
            .Name("productById")
            .Argument("id", a => a.Type<NonNullType<IdType>>())
            .Type<ProductType>();
    }
}

/// <summary>The object the Query type's fields resolve against.</summary>
public sealed class Query
{
    public Product? GetProductById(Guid id) => Catalog.ById(id);
}

/// <summary>
/// The Product type. Every field is declared against the record's properties,
/// with the GraphQL type written out rather than guessed from the CLR type.
/// </summary>
public sealed class ProductType : ObjectType<Product>
{
    protected override void Configure(IObjectTypeDescriptor<Product> descriptor)
    {
        descriptor.Name("Product");

        descriptor.Field(p => p.Id).Type<NonNullType<IdType>>();
        descriptor.Field(p => p.Sku).Type<NonNullType<StringType>>();
        descriptor.Field(p => p.Title).Type<NonNullType<StringType>>();
    }
}
