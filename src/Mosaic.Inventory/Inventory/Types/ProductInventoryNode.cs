using Mosaic.Inventory.Catalog.Model;
using Mosaic.Inventory.Data;

namespace Mosaic.Inventory.Types;

/// <summary>
/// Inventory's contribution to the shared <c>Product</c> type. Catalog owns the
/// type; this class only hangs one more field off it, from its own folder and
/// without Catalog knowing.
/// </summary>
[ObjectType<Product>]
public static partial class ProductInventoryNode
{
    /// <summary>How many units a customer can buy right now.</summary>
    /// <remarks>
    /// A product Inventory has no row for reads as zero rather than as an
    /// error. The field is non-nullable, and "we have never counted this" is
    /// not something a storefront can render.
    /// </remarks>
    public static async Task<int> GetAvailableQuantityAsync(
        [Parent("Id")] Product product,
        IStockLevelByProductIdDataLoader stockLevelByProductId,
        CancellationToken cancellationToken)
    {
        var stock = await stockLevelByProductId.LoadAsync(product.Id, cancellationToken);
        return stock?.AvailableQuantity ?? 0;
    }
}
