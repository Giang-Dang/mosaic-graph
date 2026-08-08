using Mosaic.Api.Inventory.Model;

namespace Mosaic.Api.Inventory.Data;

/// <summary>
/// Mosaic's stock levels. One row per product, so in this version of Mosaic a
/// product sits in exactly one warehouse. Since chapter 4 this list is the
/// seed for a PostgreSQL table rather than the store itself.
/// </summary>
public sealed class InventorySeedData
{
    /// <summary>
    /// Inventory's own copy of the pattern Catalog uses to generate product
    /// identifiers. The duplication is deliberate. Later in the book Catalog
    /// and Inventory become separate services and the product identifier is
    /// the only contract left between them, so neither domain reaches into the
    /// other's seed data to find one.
    /// </summary>
    private static Guid ProductId(int n) => new($"a0000000-0000-4000-8000-{n:D12}");

    public IReadOnlyList<StockLevel> StockLevels { get; } =
    [
        // Furniture. Bulky, slow moving, all of it in Amsterdam.
        // The ottoman is sold out: five units on the shelf, all five claimed.
        new(ProductId(1), "AMS-1", 12, 3),
        new(ProductId(2), "AMS-1", 148, 24),
        new(ProductId(3), "AMS-1", 7, 2),
        new(ProductId(4), "AMS-1", 0, 5),
        new(ProductId(5), "AMS-1", 23, 4),
        new(ProductId(6), "AMS-1", 9, 1),

        // Lighting. Small enough to keep in the London warehouse.
        new(ProductId(7), "LDN-2", 64, 11),
        new(ProductId(8), "LDN-2", 31, 6),
        new(ProductId(9), "LDN-2", 87, 9),
        new(ProductId(10), "LDN-2", 42, 0),
        new(ProductId(11), "LDN-2", 210, 35),

        // Kitchen. Shares London with lighting.
        // The kettle is sold out outright: nothing on hand, nothing reserved.
        new(ProductId(12), "LDN-2", 56, 8),
        new(ProductId(13), "LDN-2", 73, 5),
        new(ProductId(14), "LDN-2", 18, 2),
        new(ProductId(15), "LDN-2", 95, 12),
        new(ProductId(16), "LDN-2", 61, 7),
        new(ProductId(17), "LDN-2", 0, 0),

        // Textiles. Berlin, close to the mill.
        // The jute runner is sold out with fourteen units held for open orders.
        new(ProductId(18), "BER-1", 44, 6),
        new(ProductId(19), "BER-1", 132, 18),
        new(ProductId(20), "BER-1", 27, 9),
        new(ProductId(21), "BER-1", 186, 22),
        new(ProductId(22), "BER-1", 0, 14),

        // Storage. Flat-packed, back in Amsterdam.
        new(ProductId(23), "AMS-1", 15, 3),
        new(ProductId(24), "AMS-1", 38, 0),
        new(ProductId(25), "AMS-1", 104, 16)
    ];
}
