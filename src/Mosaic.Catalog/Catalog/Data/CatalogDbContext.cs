using Microsoft.EntityFrameworkCore;
using Mosaic.Catalog.Model;

namespace Mosaic.Catalog.Data;

/// <summary>
/// Catalog's database: one table, and nothing that belongs to anybody else.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>MosaicDbContext</c> with five of its six <c>DbSet</c>s removed,
/// and the removal cost nothing because chapter 2 drew the seam here on
/// purpose. No entity in Mosaic holds a navigation property to an entity
/// another domain owns: Reviews stores a product identifier, not a
/// <c>Product</c>. There was no <c>Include</c> to unpick, so there is none
/// here.
/// </para>
/// <para>
/// It points at its own database on the same PostgreSQL server. Entity
/// Framework Core's <c>EnsureCreatedAsync</c> creates a database that is not
/// there, so nothing in <c>docker-compose.yml</c> had to change for Catalog to
/// get one. Two services sharing a server is a deployment detail; two services
/// sharing a table is a design failure, and this is the first of those, not the
/// second.
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

        UseSnakeCaseNames(modelBuilder);
    }

    /// <summary>
    /// Renames every table and column to snake_case.
    /// </summary>
    /// <remarks>
    /// PostgreSQL folds unquoted identifiers to lower case, so a table EF Core
    /// calls <c>StockLevels</c> has to be quoted in every hand-written query.
    /// Renaming once here means the SQL in the logs is the SQL you would type.
    /// Both services do this the same way, which matters more now than it did:
    /// a reader comparing two logs should not have to work out which service
    /// quoted its identifiers.
    /// </remarks>
    private static void UseSnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table)
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (char.IsUpper(c) && i > 0 && name[i - 1] != '_')
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
