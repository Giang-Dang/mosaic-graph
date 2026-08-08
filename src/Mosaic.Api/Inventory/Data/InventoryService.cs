using Mosaic.Api.Infrastructure;
using Mosaic.Api.Inventory.Model;

namespace Mosaic.Api.Inventory.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Inventory domain.
/// </summary>
/// <remarks>
/// One key in, one answer out. There is no overload here that takes a list of
/// product identifiers, which is deliberate: it is what makes a query that
/// walks a page of products call this service once per product.
/// </remarks>
public sealed class InventoryService(InMemoryInventoryData data, ServiceCallCounter counter)
{
    /// <summary>
    /// The stock row for one product, or <c>null</c> if Inventory has never
    /// been told about it.
    /// </summary>
    public async Task<StockLevel?> GetStockLevelAsync(Guid productId, CancellationToken cancellationToken)
    {
        await counter.RecordLookupAsync(cancellationToken);
        return data.StockLevels.FirstOrDefault(s => s.ProductId == productId);
    }
}
