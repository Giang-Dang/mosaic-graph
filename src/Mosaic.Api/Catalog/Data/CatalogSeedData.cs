using Mosaic.Api.Catalog.Model;

namespace Mosaic.Api.Catalog.Data;

/// <summary>
/// Mosaic's catalog.
/// <para>
/// Until chapter 4 this list was the store. It is now the seed: the same
/// twenty-five rows, written into PostgreSQL once at start-up, so that a
/// product identifier means the same thing at every tag in this repository.
/// </para>
/// </summary>
public sealed class CatalogSeedData
{
    /// <summary>
    /// Product identifiers are generated from a fixed pattern so that the other
    /// domains can refer to them without sharing a file with Catalog. After the
    /// book splits these domains into separate services, the identifier is the
    /// only contract left between them, so the seed data treats it that way
    /// from the start.
    /// </summary>
    public static Guid ProductId(int n) =>
        new($"a0000000-0000-4000-8000-{n:D12}");

    public IReadOnlyList<Product> Products { get; } =
    [
        new(ProductId(1), "MOS-FRN-0001", "Larsen Oak Dining Table", "Solid white oak, seats six, with a hand-rubbed matte finish.", ProductCategory.Furniture),
        new(ProductId(2), "MOS-FRN-0002", "Larsen Dining Chair", "Stackable oak chair with a woven paper cord seat.", ProductCategory.Furniture),
        new(ProductId(3), "MOS-FRN-0003", "Brant Two-Seat Sofa", "Feather-wrapped foam cushions on a beech frame.", ProductCategory.Furniture),
        new(ProductId(4), "MOS-FRN-0004", "Brant Ottoman", "Matching footstool with a removable cover.", ProductCategory.Furniture),
        new(ProductId(5), "MOS-FRN-0005", "Kessel Writing Desk", "Narrow desk with a single drawer and a cable channel.", ProductCategory.Furniture),
        new(ProductId(6), "MOS-FRN-0006", "Halden Bed Frame", "Slatted base, no box spring required.", ProductCategory.Furniture),

        new(ProductId(7), "MOS-LGT-0001", "Orbit Pendant Lamp", "Opal glass globe on a brushed brass stem.", ProductCategory.Lighting),
        new(ProductId(8), "MOS-LGT-0002", "Orbit Floor Lamp", "The pendant's taller sibling, with a weighted base.", ProductCategory.Lighting),
        new(ProductId(9), "MOS-LGT-0003", "Fen Reading Light", "Clamp-mounted task light with a warm dimmable bulb.", ProductCategory.Lighting),
        new(ProductId(10), "MOS-LGT-0004", "Fen Wall Sconce", "Hardwired sconce with an adjustable arm.", ProductCategory.Lighting),
        new(ProductId(11), "MOS-LGT-0005", "Tallow Candle Set", null, ProductCategory.Lighting),

        new(ProductId(12), "MOS-KIT-0001", "Ridge Chef's Knife", "Carbon steel, 20cm, with a stabilised walnut handle.", ProductCategory.Kitchen),
        new(ProductId(13), "MOS-KIT-0002", "Ridge Paring Knife", "The chef's knife at 9cm, for the work it is bad at.", ProductCategory.Kitchen),
        new(ProductId(14), "MOS-KIT-0003", "Copper Saute Pan", "Tin-lined copper, 28cm, with an iron handle.", ProductCategory.Kitchen),
        new(ProductId(15), "MOS-KIT-0004", "Stoneware Mixing Bowl", "Three-litre bowl with a pouring lip.", ProductCategory.Kitchen),
        new(ProductId(16), "MOS-KIT-0005", "Beech Cutting Board", "End-grain board with rubber feet.", ProductCategory.Kitchen),
        new(ProductId(17), "MOS-KIT-0006", "Enamel Kettle", "Two litres, stovetop, with a whistling spout.", ProductCategory.Kitchen),

        new(ProductId(18), "MOS-TXT-0001", "Marle Wool Throw", "Lambswool, herringbone weave, 130 by 180cm.", ProductCategory.Textiles),
        new(ProductId(19), "MOS-TXT-0002", "Marle Cushion Cover", "Matching cover, 50cm square, zip closure.", ProductCategory.Textiles),
        new(ProductId(20), "MOS-TXT-0003", "Linen Sheet Set", "Stonewashed flax, gets better after a dozen washes.", ProductCategory.Textiles),
        new(ProductId(21), "MOS-TXT-0004", "Waffle Bath Towel", "Fast-drying cotton waffle weave.", ProductCategory.Textiles),
        new(ProductId(22), "MOS-TXT-0005", "Jute Floor Runner", null, ProductCategory.Textiles),

        new(ProductId(23), "MOS-STO-0001", "Corbel Shelf Unit", "Powder-coated steel uprights with oak shelves.", ProductCategory.Storage),
        new(ProductId(24), "MOS-STO-0002", "Corbel Wall Shelf", "Single floating shelf, 90cm.", ProductCategory.Storage),
        new(ProductId(25), "MOS-STO-0003", "Canvas Storage Bin", "Collapsible bin with leather handles.", ProductCategory.Storage)
    ];
}
