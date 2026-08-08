using Mosaic.Api.Ordering.Model;
using Mosaic.Api.Pricing.Model;

namespace Mosaic.Api.Ordering.Data;

/// <summary>
/// Mosaic's order history. Eight orders spread over seven of the twelve
/// customers, so that one customer has two orders and most have none. Since
/// chapter 4 this list is the seed for two PostgreSQL tables rather than the
/// store itself.
/// </summary>
public sealed class OrderingSeedData
{
    private const string Currency = "EUR";

    /// <summary>
    /// Ordering's own copy of Catalog's identifier pattern. The duplication is
    /// deliberate: later in the book these domains become separate services and
    /// the identifier is the only contract left between them, so Ordering
    /// derives its keys the same way Catalog does rather than reaching into
    /// Catalog's seed data for them. The same goes for
    /// <see cref="CustomerId"/> and Accounts.
    /// </summary>
    private static Guid ProductId(int n) => new($"a0000000-0000-4000-8000-{n:D12}");

    /// <summary>Accounts' customer identifier pattern, duplicated on purpose.</summary>
    private static Guid CustomerId(int n) => new($"c0000000-0000-4000-8000-{n:D12}");

    /// <summary>
    /// Ordering's own identifiers, in the same shape with a different first
    /// character so a misrouted identifier is obvious on sight.
    /// </summary>
    private static Guid OrderId(int n) => new($"d0000000-0000-4000-8000-{n:D12}");

    /// <remarks>
    /// The dates are fixed literals rather than offsets from today, because a
    /// book that prints a response cannot have the response change overnight.
    /// Unit prices are what the customer paid on the day, which is usually but
    /// not always today's list price in Pricing.
    /// </remarks>
    public IReadOnlyList<Order> Orders { get; } =
    [
        new(OrderId(1), CustomerId(1), new DateTimeOffset(2026, 1, 12, 9, 15, 0, TimeSpan.Zero))
        {
            Lines =
            [
                new(ProductId(1), 1) { UnitPrice = new Money(1249.00m, Currency) },
                new(ProductId(2), 6) { UnitPrice = new Money(229.00m, Currency) },
                new(ProductId(7), 1) { UnitPrice = new Money(189.00m, Currency) }
            ]
        },

        new(OrderId(2), CustomerId(1), new DateTimeOffset(2026, 2, 3, 17, 40, 0, TimeSpan.Zero))
        {
            Lines =
            [
                new(ProductId(18), 2) { UnitPrice = new Money(119.00m, Currency) }
            ]
        },

        new(OrderId(3), CustomerId(3), new DateTimeOffset(2026, 2, 18, 11, 5, 0, TimeSpan.Zero))
        {
            Lines =
            [
                new(ProductId(12), 1) { UnitPrice = new Money(159.00m, Currency) },
                new(ProductId(13), 1) { UnitPrice = new Money(79.00m, Currency) },
                new(ProductId(16), 1) { UnitPrice = new Money(69.00m, Currency) },
                // Bought during a January clearance, below today's list price.
                new(ProductId(17), 1) { UnitPrice = new Money(49.00m, Currency) }
            ]
        },

        new(OrderId(4), CustomerId(4), new DateTimeOffset(2026, 3, 2, 8, 30, 0, TimeSpan.Zero))
        {
            Lines =
            [
                new(ProductId(3), 1) { UnitPrice = new Money(1690.00m, Currency) },
                new(ProductId(4), 1) { UnitPrice = new Money(349.00m, Currency) }
            ]
        },

        new(OrderId(5), CustomerId(7), new DateTimeOffset(2026, 3, 21, 14, 55, 0, TimeSpan.Zero))
        {
            Lines =
            [
                new(ProductId(20), 2) { UnitPrice = new Money(189.00m, Currency) },
                new(ProductId(21), 4) { UnitPrice = new Money(29.50m, Currency) }
            ]
        },

        new(OrderId(6), CustomerId(9), new DateTimeOffset(2026, 4, 7, 19, 20, 0, TimeSpan.Zero))
        {
            Lines =
            [
                new(ProductId(23), 2) { UnitPrice = new Money(429.00m, Currency) }
            ]
        },

        new(OrderId(7), CustomerId(11), new DateTimeOffset(2026, 4, 26, 10, 10, 0, TimeSpan.Zero))
        {
            Lines =
            [
                // The desk was on offer that week.
                new(ProductId(5), 1) { UnitPrice = new Money(549.00m, Currency) },
                new(ProductId(9), 1) { UnitPrice = new Money(89.50m, Currency) },
                new(ProductId(25), 3) { UnitPrice = new Money(34.00m, Currency) }
            ]
        },

        new(OrderId(8), CustomerId(12), new DateTimeOffset(2026, 5, 15, 16, 0, 0, TimeSpan.Zero))
        {
            Lines =
            [
                new(ProductId(6), 1) { UnitPrice = new Money(899.00m, Currency) },
                new(ProductId(10), 2) { UnitPrice = new Money(129.00m, Currency) },
                new(ProductId(11), 3) { UnitPrice = new Money(24.00m, Currency) },
                new(ProductId(22), 1) { UnitPrice = new Money(149.00m, Currency) }
            ]
        }
    ];
}
