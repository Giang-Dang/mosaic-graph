using Mosaic.Api.Pricing.Model;

namespace Mosaic.Api.Pricing.Data;

/// <summary>
/// Mosaic's price list, seeded in memory. One price for every product in the
/// catalog, all in euros.
/// </summary>
public sealed class InMemoryPricingData
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
        new(ProductId(1), new Money(1249.00m, Currency)),
        new(ProductId(2), new Money(229.00m, Currency)),
        new(ProductId(3), new Money(1690.00m, Currency)),
        new(ProductId(4), new Money(349.00m, Currency)),
        new(ProductId(5), new Money(579.00m, Currency)),
        new(ProductId(6), new Money(899.00m, Currency)),

        // Lighting
        new(ProductId(7), new Money(189.00m, Currency)),
        new(ProductId(8), new Money(259.00m, Currency)),
        new(ProductId(9), new Money(89.50m, Currency)),
        new(ProductId(10), new Money(129.00m, Currency)),
        new(ProductId(11), new Money(24.00m, Currency)),

        // Kitchen
        new(ProductId(12), new Money(159.00m, Currency)),
        new(ProductId(13), new Money(79.00m, Currency)),
        new(ProductId(14), new Money(245.00m, Currency)),
        new(ProductId(15), new Money(44.50m, Currency)),
        new(ProductId(16), new Money(69.00m, Currency)),
        new(ProductId(17), new Money(54.00m, Currency)),

        // Textiles
        new(ProductId(18), new Money(119.00m, Currency)),
        new(ProductId(19), new Money(39.00m, Currency)),
        new(ProductId(20), new Money(189.00m, Currency)),
        new(ProductId(21), new Money(29.50m, Currency)),
        new(ProductId(22), new Money(149.00m, Currency)),

        // Storage
        new(ProductId(23), new Money(429.00m, Currency)),
        new(ProductId(24), new Money(89.00m, Currency)),
        new(ProductId(25), new Money(34.00m, Currency))
    ];
}
