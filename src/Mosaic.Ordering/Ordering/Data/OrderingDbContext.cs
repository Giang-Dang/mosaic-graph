using Microsoft.EntityFrameworkCore;
using Mosaic.Ordering.Model;
using Mosaic.ServiceDefaults.Data;

namespace Mosaic.Ordering.Data;

/// <summary>
/// Ordering's database: two tables, and nothing that belongs to anybody else.
/// </summary>
/// <remarks>
/// <para>
/// Two tables rather than one: <c>OrderLine</c> is a related entity with a shadow key rather than an owned type, which chapter 8 found out the hard way. Both belong to Ordering, so the split did not touch the relationship between them.
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
public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options)
    : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(OrderingDbContext).Assembly);

        modelBuilder.UseSnakeCaseNames();
    }
}