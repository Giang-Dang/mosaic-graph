using Microsoft.EntityFrameworkCore;
using Mosaic.Catalog.Model;
using Mosaic.ServiceDefaults.Data;

namespace Mosaic.Catalog.Data;

/// <summary>
/// Catalog's database: one table, and nothing that belongs to anybody else.
/// </summary>
/// <remarks>
/// <para>
/// This was <c>MosaicDbContext</c> with five of its six <c>DbSet</c>s removed,
/// and the removal cost nothing because chapter 2 drew the seam here on
/// purpose. No entity in Mosaic holds a navigation property to an entity
/// another domain owns: Reviews stores a product identifier, not a
/// <c>Product</c>. There was no <c>Include</c> to unpick, so there is none
/// here, and chapter 12 collected the same dividend five more times.
/// </para>
/// <para>
/// It points at its own database on the PostgreSQL server all six services
/// share. Entity Framework Core's <c>EnsureCreatedAsync</c> creates a database
/// that is not there, so nothing in <c>docker-compose.yml</c> had to change for
/// any of them to get one. Six services sharing a server is a deployment
/// detail; two services sharing a table would undo the whole exercise.
/// </para>
/// <para>
/// The snake_case convention used to be a private method in this file and an
/// identical private method in Mosaic.Api's. It is one method in
/// <c>Mosaic.ServiceDefaults</c> now, because six copies of it would be six
/// chances for two of the six logs to quote their identifiers differently, and
/// a reader comparing two logs should not have to work out which service did
/// the quoting.
/// </para>
/// </remarks>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CatalogDbContext).Assembly);

        modelBuilder.UseSnakeCaseNames();
    }
}
