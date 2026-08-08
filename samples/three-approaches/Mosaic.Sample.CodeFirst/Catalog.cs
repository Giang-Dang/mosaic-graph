namespace Mosaic.Sample.CodeFirst;

/// <summary>
/// The slice of Mosaic's product that all three samples serve. Same shape, same
/// data, three different ways of telling HotChocolate about it.
/// </summary>
public sealed record Product(Guid Id, string Sku, string Title);

/// <summary>
/// Three hard-coded products. No database, no dependency on Mosaic.Api: these
/// samples have to keep compiling while the real service moves on.
/// </summary>
public static class Catalog
{
    public static Guid ProductId(int n) => new($"a0000000-0000-4000-8000-{n:D12}");

    public static IReadOnlyList<Product> Products { get; } =
    [
        new(ProductId(1), "MOS-FRN-0001", "Larsen Oak Dining Table"),
        new(ProductId(2), "MOS-FRN-0002", "Larsen Dining Chair"),
        new(ProductId(3), "MOS-FRN-0003", "Brant Two-Seat Sofa")
    ];

    public static Product? ById(Guid id) => Products.FirstOrDefault(p => p.Id == id);
}
