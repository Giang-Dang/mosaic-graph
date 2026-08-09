using Microsoft.EntityFrameworkCore;
using Mosaic.Reviews.Model;
using Mosaic.ServiceDefaults.Data;

namespace Mosaic.Reviews.Data;

/// <summary>
/// Reviews's database: one table, and nothing that belongs to anybody else.
/// </summary>
/// <remarks>
/// <para>
/// A review stores two identifiers - the product and the author - and neither is a foreign key. That was chapter 2's decision and it is what makes this a move.
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
public sealed class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options)
    : DbContext(options)
{
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ReviewsDbContext).Assembly);

        modelBuilder.UseSnakeCaseNames();
    }
}