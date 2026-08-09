namespace Mosaic.Pricing.Catalog.Model;

/// <summary>
/// What kind of thing a product is. Catalog's enum, declared here a second time
/// because this service has a field that needs to read one.
/// </summary>
/// <remarks>
/// <para>
/// A copy, like <see cref="ProductKey"/> and for the same reason: the values
/// are a contract between two services, and a shared project would turn an
/// agreement about a wire format into a compile-time dependency. What is
/// different from <c>ProductKey</c> is that this copy is visible in the schema,
/// so the composer checks it. Drop a value here that Catalog declares and the
/// composed enum is the one Catalog wrote; add one Catalog does not have and
/// composition has something to say about it.
/// </para>
/// <para>
/// Pricing is the only one of the five new services that needs this. That is
/// worth noticing: of the four subgraphs that hang a field off <c>Product</c>,
/// exactly one needs to know anything about what a product <em>is</em>, and
/// that one field is the reason <c>@requires</c> exists.
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
