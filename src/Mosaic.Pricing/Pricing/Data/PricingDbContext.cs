using Microsoft.EntityFrameworkCore;
using Mosaic.Pricing.Model;
using Mosaic.ServiceDefaults.Data;

namespace Mosaic.Pricing.Data;

/// <summary>
/// Pricing's database: one table, and nothing that belongs to anybody else.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>MosaicDbContext</c> with four of its five <c>DbSet</c>s removed,
/// and the removal cost nothing for the reason chapter 8's did: no entity in
/// Mosaic holds a navigation property to an entity another domain owns.
/// <c>ProductPrice</c> stores a product identifier and never a
/// <c>Product</c>, so there was no <c>Include</c> to unpick and no foreign key
/// to drop.
/// </para>
/// <para>
/// The naming convention comes from <c>Mosaic.ServiceDefaults</c> rather than
/// from a private method here. Chapter 4 wrote it once, chapter 8 copied it
/// into the second service, and copying it a further four times would have been
/// six chances for two of the six logs to quote their identifiers differently.
/// </para>
/// </remarks>
public sealed class PricingDbContext(DbContextOptions<PricingDbContext> options)
    : DbContext(options)
{
    public DbSet<ProductPrice> Prices => Set<ProductPrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(PricingDbContext).Assembly);

        modelBuilder.UseSnakeCaseNames();
    }
}
