using Mosaic.Ordering.Model;

namespace Mosaic.Ordering.Model;

/// <summary>
/// One product on one order, in the quantity ordered and at the price the
/// customer actually paid.
/// </summary>
/// <param name="ProductId">
/// The Catalog product this line sold. Ordering stores the identifier and
/// nothing else about the product; it never holds Catalog's <c>Product</c>.
/// The field is hidden from the schema because callers reach the product
/// through <c>OrderLine.product</c> instead.
/// </param>
/// <param name="Quantity">How many units of the product this line sold.</param>
public sealed record OrderLine(
    [property: GraphQLIgnore] Guid ProductId,
    int Quantity)
{
    /// <summary>
    /// What one unit cost on the day the order was placed. Ordering keeps its
    /// own copy rather than asking Pricing, because a price list changes and an
    /// order does not.
    /// </summary>
    public required Money UnitPrice { get; init; }
}
