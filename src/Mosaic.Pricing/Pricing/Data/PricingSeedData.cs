using Mosaic.Pricing.Model;

namespace Mosaic.Pricing.Data;

/// <summary>
/// Mosaic's price list. One price for every product in the catalog, all in
/// euros. Since chapter 4 this list is the seed for a PostgreSQL table rather
/// than the store itself.
/// </summary>
public sealed class PricingSeedData
{
    private const string Currency = "EUR";

    /// <summary>
    /// Pricing's own copy of Catalog's identifier pattern. Deliberately a
    /// duplicate: later in the book these two domains become separate services
    /// and the identifier is the only contract left between them, so Pricing
    /// derives its keys the same way Catalog does rather than reaching into
    /// Catalog's seed data for them.
    /// </summary>
    private static Guid ProductId(int n) => new($"a0000000-0000-4000-8000-{n:D12}");

    public IReadOnlyList<ProductPrice> Prices { get; } =
    [
        // Furniture
        new(ProductId(1)) { Amount = new Money(1249.00m, Currency) },
        new(ProductId(2)) { Amount = new Money(229.00m, Currency) },
        new(ProductId(3)) { Amount = new Money(1690.00m, Currency) },
        new(ProductId(4)) { Amount = new Money(349.00m, Currency) },
        new(ProductId(5)) { Amount = new Money(579.00m, Currency) },
        new(ProductId(6)) { Amount = new Money(899.00m, Currency) },

        // Lighting
        new(ProductId(7)) { Amount = new Money(189.00m, Currency) },
        new(ProductId(8)) { Amount = new Money(259.00m, Currency) },
        new(ProductId(9)) { Amount = new Money(89.50m, Currency) },
        new(ProductId(10)) { Amount = new Money(129.00m, Currency) },
        new(ProductId(11)) { Amount = new Money(24.00m, Currency) },

        // Kitchen
        new(ProductId(12)) { Amount = new Money(159.00m, Currency) },
        new(ProductId(13)) { Amount = new Money(79.00m, Currency) },
        new(ProductId(14)) { Amount = new Money(245.00m, Currency) },
        new(ProductId(15)) { Amount = new Money(44.50m, Currency) },
        new(ProductId(16)) { Amount = new Money(69.00m, Currency) },
        new(ProductId(17)) { Amount = new Money(54.00m, Currency) },

        // Textiles
        new(ProductId(18)) { Amount = new Money(119.00m, Currency) },
        new(ProductId(19)) { Amount = new Money(39.00m, Currency) },
        new(ProductId(20)) { Amount = new Money(189.00m, Currency) },
        new(ProductId(21)) { Amount = new Money(29.50m, Currency) },
        new(ProductId(22)) { Amount = new Money(149.00m, Currency) },

        // Storage
        new(ProductId(23)) { Amount = new Money(429.00m, Currency) },
        new(ProductId(24)) { Amount = new Money(89.00m, Currency) },
        new(ProductId(25)) { Amount = new Money(34.00m, Currency) }
    ];
}
