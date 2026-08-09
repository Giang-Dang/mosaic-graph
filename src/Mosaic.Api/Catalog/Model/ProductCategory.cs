namespace Mosaic.Api.Catalog.Model;

/// <summary>
/// What kind of thing a product is. Catalog's enum, declared here a second
/// time because this service now has a field that needs to read one.
/// </summary>
/// <remarks>
/// <para>
/// A copy, like <see cref="ProductKey"/> and for the same reason: the values
/// are a contract between two services, and a shared project would turn an
/// agreement about a wire format into a compile-time dependency. What is
/// different from <c>ProductKey</c> is that this copy is visible in the
/// schema, so the composer checks it. Drop a value here that Catalog declares
/// and the composed enum is the one Catalog wrote; add one Catalog does not
/// have and composition has something to say about it.
/// </para>
/// <para>
/// Nothing in this service stores a category. The only value that ever reaches
/// one of these is the value the router copies out of Catalog's answer into
/// the representation it sends here, which is what
/// <c>Product.shippingCost</c>'s <c>@requires</c> asks for.
/// </para>
/// </remarks>
public enum ProductCategory
{
    Furniture,
    Lighting,
    Kitchen,
    Textiles,
    Storage
}
