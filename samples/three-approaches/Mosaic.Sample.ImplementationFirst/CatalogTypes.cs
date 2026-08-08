namespace Mosaic.Sample.ImplementationFirst;

/// <summary>
/// The entry into the graph. [QueryType] tells the source generator to fold
/// every public static method on this class into the Query type; the method
/// name minus the Get prefix becomes the field name.
/// </summary>
[QueryType]
public static partial class CatalogQueries
{
    public static Product? GetProductById([ID] Guid id) => Catalog.ById(id);
}

/// <summary>
/// The Product type. The record's own properties supply sku and title; this
/// class only exists to restate Id as an ID rather than a UUID.
/// </summary>
[ObjectType<Product>]
public static partial class ProductNode
{
    [ID]
    public static Guid GetId([Parent] Product product) => product.Id;
}
