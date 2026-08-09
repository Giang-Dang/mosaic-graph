using Mosaic.Pricing.Catalog.Model;

namespace Mosaic.Pricing.Model;

/// <summary>
/// What the carrier charges for one item, by what kind of item it is.
/// </summary>
/// <remarks>
/// Invented rates for an invented storefront, kept in one place so that
/// <c>Product.shippingCost</c> reads as a rule rather than as a switch
/// statement. What matters for the book is not the numbers but that they are a
/// function of a value this service does not hold.
/// </remarks>
public static class ShippingRates
{
    /// <summary>
    /// Above this, in the product's own currency, the rate is waived. Furniture
    /// is the exception: a pallet costs what a pallet costs.
    /// </summary>
    public const decimal FreeAbove = 100m;

    public static decimal For(ProductCategory category)
        => category switch
        {
            ProductCategory.Furniture => 39.00m,
            ProductCategory.Lighting => 9.90m,
            ProductCategory.Storage => 7.90m,
            ProductCategory.Kitchen => 5.90m,
            ProductCategory.Textiles => 4.90m,
            _ => throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "No shipping rate is defined for this category.")
        };

    public static bool IsWaived(ProductCategory category, decimal price)
        => category is not ProductCategory.Furniture && price >= FreeAbove;
}
