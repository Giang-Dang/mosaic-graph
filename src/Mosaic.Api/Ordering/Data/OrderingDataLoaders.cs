using GreenDonut;
using Mosaic.Api.Ordering.Model;

namespace Mosaic.Api.Ordering.Data;

/// <summary>
/// Ordering's DataLoaders.
/// </summary>
/// <remarks>
/// One loader, added in chapter 5 to back <c>Order</c>'s node resolver. The
/// <c>node</c> field can be asked for several identifiers at once through
/// <c>nodes(ids:)</c>, so resolving them one at a time would reintroduce the
/// problem chapter 4 spent itself removing.
/// </remarks>
public static class OrderingDataLoaders
{
    /// <summary>Orders by identifier, in one batch.</summary>
    [DataLoader]
    public static Task<IReadOnlyDictionary<Guid, Order>> GetOrderByIdAsync(
        IReadOnlyList<Guid> ids,
        OrderingService ordering,
        CancellationToken cancellationToken)
        => ordering.GetOrdersByIdsAsync(ids, cancellationToken);
}
