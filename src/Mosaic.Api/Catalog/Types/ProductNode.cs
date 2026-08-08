using Mosaic.Api.Catalog.Model;

namespace Mosaic.Api.Catalog.Types;

/// <summary>
/// Catalog's own view of the shared <c>Product</c> type. The other domains
/// attach their fields to this same type from their own folders.
/// </summary>
[ObjectType<Product>]
public static partial class ProductNode
{
    /// <summary>The product's global identifier.</summary>
    [ID]
    public static Guid GetId([Parent] Product product) => product.Id;
}
