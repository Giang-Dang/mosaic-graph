using Mosaic.Api.Infrastructure;
using Mosaic.Api.Ordering.Model;

namespace Mosaic.Api.Ordering.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Ordering domain.
/// </summary>
/// <remarks>
/// As in Catalog and Accounts, every method takes one key and answers about one
/// thing. Nothing here accepts a list of identifiers. That omission is what
/// makes an order feed walk back to Accounts once per order and to Catalog once
/// per line, which is the behaviour this chapter is about.
/// </remarks>
public sealed class OrderingService(InMemoryOrderingData data, ServiceCallCounter counter)
{
    /// <summary>
    /// Every order one customer has placed, oldest first. A customer with no
    /// orders gets an empty list rather than null.
    /// </summary>
    public async Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await counter.RecordLookupAsync(cancellationToken);
        return data.Orders.Where(o => o.CustomerId == customerId).ToList();
    }
}
