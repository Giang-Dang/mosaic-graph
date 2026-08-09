using Microsoft.EntityFrameworkCore;
using Mosaic.ServiceDefaults.Counting;
using Mosaic.Pricing.Data;
using Mosaic.Pricing.Model;

namespace Mosaic.Pricing.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Pricing domain.
/// </summary>
/// <remarks>
/// As in Catalog, every method takes one key and answers about one thing.
/// There is no overload accepting a list of identifiers, which is deliberate
/// and is what makes a nested query pay for one lookup per product.
/// </remarks>
public sealed class PricingService(PricingDbContext db, ServiceCallCounter counter)
{
    public async Task<ProductPrice?> GetPriceByProductIdAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Prices
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId, cancellationToken);
    }

    /// <summary>The prices of several products, keyed by product identifier.</summary>
    public async Task<IReadOnlyDictionary<Guid, ProductPrice>> GetPricesByProductIdsAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Prices
            .AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, cancellationToken);
    }
}
