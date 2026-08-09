using Microsoft.EntityFrameworkCore;
using Mosaic.Accounts.Model;
using Mosaic.ServiceDefaults.Data;

namespace Mosaic.Accounts.Data;

/// <summary>
/// Accounts's database: one table, and nothing that belongs to anybody else.
/// </summary>
/// <remarks>
/// <para>
/// Accounts is the only one of the six that references nobody at all, which is what made it safe to extract without touching another service.
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
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options)
    : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AccountsDbContext).Assembly);

        modelBuilder.UseSnakeCaseNames();
    }
}