using Mosaic.Api.Ordering.Data;
using Mosaic.Api.Ordering.Model;

namespace Mosaic.Api.Ordering.Types;

/// <summary>
/// The Ordering domain's entry into the graph. Orders are reached through the
/// customer who placed them; there is no field that lists every order.
/// </summary>
[QueryType]
public static partial class OrderQueries
{
    /// <summary>Every order one customer has placed.</summary>
    public static Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(
        [ID] Guid customerId,
        OrderingService ordering,
        CancellationToken cancellationToken)
        => ordering.GetOrdersByCustomerAsync(customerId, cancellationToken);
}
