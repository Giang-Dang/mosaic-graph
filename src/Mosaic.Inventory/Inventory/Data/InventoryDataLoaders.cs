using GreenDonut;
using Mosaic.Inventory.Model;

namespace Mosaic.Inventory.Data;

/// <summary>
/// Inventory's DataLoaders.
/// </summary>
public static class InventoryDataLoaders
{
    /// <summary>Stock rows by product identifier, in one batch.</summary>
    [DataLoader]
    public static Task<IReadOnlyDictionary<Guid, StockLevel>> GetStockLevelByProductIdAsync(
        IReadOnlyList<Guid> productIds,
        InventoryService inventory,
        CancellationToken cancellationToken)
        => inventory.GetStockLevelsByProductIdsAsync(productIds, cancellationToken);
}
