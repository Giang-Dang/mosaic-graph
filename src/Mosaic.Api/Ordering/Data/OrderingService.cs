using Microsoft.EntityFrameworkCore;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Infrastructure.Data;
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
public sealed class OrderingService(MosaicDbContext db, ServiceCallCounter counter)
{
    /// <summary>
    /// Every order one customer has placed, oldest first. A customer with no
    /// orders gets an empty list rather than null.
    /// </summary>
    /// <remarks>
    /// The lines come back with the order because they are owned by it, and an
    /// owned collection is part of the entity rather than a navigation you
    /// have to remember to <c>Include</c>. One statement, one round trip, and
    /// no way to forget it.
    /// </remarks>
    public async Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderBy(o => o.PlacedAt)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Several orders by their identifiers, keyed for the caller.</summary>
    /// <remarks>
    /// Added in chapter 5, and only because <c>Order</c> implements <c>Node</c>.
    /// Nothing in Mosaic had ever wanted an order by its own identifier: the
    /// only way to an order was through the customer who placed it. Making a
    /// type refetchable means promising that an identifier is enough to find
    /// it, and that promise has to be kept somewhere.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, Order>> GetOrdersByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Orders
            .AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);
    }
}
