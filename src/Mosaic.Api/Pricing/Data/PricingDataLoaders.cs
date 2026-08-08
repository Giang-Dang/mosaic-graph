using GreenDonut;
using Mosaic.Api.Pricing.Model;

namespace Mosaic.Api.Pricing.Data;

/// <summary>
/// Pricing's DataLoaders.
/// </summary>
public static class PricingDataLoaders
{
    /// <summary>Prices by product identifier, in one batch.</summary>
    [DataLoader]
    public static Task<IReadOnlyDictionary<Guid, ProductPrice>> GetPriceByProductIdAsync(
        IReadOnlyList<Guid> productIds,
        PricingService pricing,
        CancellationToken cancellationToken)
        => pricing.GetPricesByProductIdsAsync(productIds, cancellationToken);
}
