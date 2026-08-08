using Microsoft.EntityFrameworkCore;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Infrastructure.Data;
using Mosaic.Api.Pricing.Model;

namespace Mosaic.Api.Pricing.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Pricing domain.
/// </summary>
/// <remarks>
/// As in Catalog, every method takes one key and answers about one thing.
/// There is no overload accepting a list of identifiers, which is deliberate
/// and is what makes a nested query pay for one lookup per product.
/// </remarks>
public sealed class PricingService(MosaicDbContext db, ServiceCallCounter counter)
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
}
