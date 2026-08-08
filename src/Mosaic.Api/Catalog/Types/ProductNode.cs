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
    /// <remarks>
    /// The <c>Requires</c> on <c>[Parent]</c> is not decoration. This field is
    /// backed by a resolver rather than by the property, because <c>[ID]</c>
    /// encodes the raw Guid, and a projection builds its <c>SELECT</c> list
    /// from properties. Without the declaration, asking <c>browseProducts</c>
    /// for <c>id</c> hands the resolver a product whose <c>Id</c> was never
    /// selected: every product answers with an all-zero Guid, and the cursors
    /// built from the sort tiebreaker go with it.
    /// </remarks>
    [ID]
    public static Guid GetId([Parent("Id")] Product product) => product.Id;
}
