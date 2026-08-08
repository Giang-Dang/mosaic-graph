using Mosaic.Api.Catalog.Model;
using Mosaic.Api.Pricing.Data;
using Mosaic.Api.Pricing.Model;

namespace Mosaic.Api.Pricing.Types;

/// <summary>
/// Pricing's contribution to the shared <c>Product</c> type. Catalog owns the
/// type; this class only hangs one more field off it, from its own folder.
/// </summary>
[ObjectType<Product>]
public static partial class ProductPricingNode
{
    /// <summary>What the product costs.</summary>
    public static async Task<Money> GetPriceAsync(
        [Parent] Product product,
        PricingService pricing,
        CancellationToken cancellationToken)
    {
        var price = await pricing.GetPriceByProductIdAsync(product.Id, cancellationToken);

        // The field is non-nullable because every catalogued product is priced.
        // If that ever stops being true it is a seed-data bug, not something a
        // caller should have to handle.
        return price?.Amount
            ?? throw new InvalidOperationException(
                $"No price is seeded for product {product.Id}.");
    }
}
