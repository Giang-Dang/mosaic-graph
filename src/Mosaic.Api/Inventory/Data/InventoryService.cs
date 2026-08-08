using Microsoft.EntityFrameworkCore;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Infrastructure.Data;
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
public sealed class InventoryService(MosaicDbContext db, ServiceCallCounter counter)
{
    /// <summary>
    /// The stock row for one product, or <c>null</c> if Inventory has never
    /// been told about it.
    /// </summary>
    public async Task<StockLevel?> GetStockLevelAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.StockLevels
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);
    }

    /// <summary>The stock rows for several products, keyed by product identifier.</summary>
    public async Task<IReadOnlyDictionary<Guid, StockLevel>> GetStockLevelsByProductIdsAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.StockLevels
            .AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, cancellationToken);
    }
}
