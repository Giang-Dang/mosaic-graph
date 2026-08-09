using Microsoft.EntityFrameworkCore;
using Mosaic.Inventory.Model;
using Mosaic.ServiceDefaults.Data;

namespace Mosaic.Inventory.Data;

/// <summary>
/// Inventory's database: one table, and nothing that belongs to anybody else.
/// </summary>
/// <remarks>
/// <para>
/// Inventory stores a product identifier and a count. It has never held a <c>Product</c>.
/// </para>
/// <para>
/// The seam this cuts along was drawn in chapter 2 and has not moved since: no
/// entity in Mosaic holds a navigation property to an entity another domain
/// owns. Six databases out of one cost no <c>Include</c> and no dropped
/// constraint, which is the whole return on a decision made four hundred pages
/// earlier.
/// </para>
/// <para>
/// The naming convention comes from <c>Mosaic.ServiceDefaults</c>. Chapter 4
/// wrote it once and chapter 8 copied it into the second service; copying it
/// four more times would have been six chances for two of the six logs to quote
/// their identifiers differently.
/// </para>
/// </remarks>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : DbContext(options)
{
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(InventoryDbContext).Assembly);

        modelBuilder.UseSnakeCaseNames();
    }
}