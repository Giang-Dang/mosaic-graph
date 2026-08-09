using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mosaic.Inventory.Model;

namespace Mosaic.Inventory.Data;

/// <summary>
/// How Inventory stores a stock level.
/// </summary>
/// <remarks>
/// One row per product, keyed by the product identifier, with no foreign key
/// to Catalog for the reason given in Pricing's configuration.
/// </remarks>
public sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.HasKey(s => s.ProductId);

        builder.Property(s => s.WarehouseCode).HasMaxLength(16).IsRequired();
    }
}
