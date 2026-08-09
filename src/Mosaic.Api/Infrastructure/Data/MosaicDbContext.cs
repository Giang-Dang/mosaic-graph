using Microsoft.EntityFrameworkCore;
using Mosaic.Api.Accounts.Model;
using Mosaic.Api.Inventory.Model;
using Mosaic.Api.Ordering.Model;
using Mosaic.Api.Pricing.Model;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Infrastructure.Data;

/// <summary>
/// The one database context for the five domains Mosaic still runs.
/// </summary>
/// <remarks>
/// <para>
/// One context, five domains, one connection string. It was six until chapter
/// 8, and the seam it cut along was drawn here in chapter 2: no entity holds a
/// navigation property to an entity another domain owns, so Reviews stores a
/// product identifier and not a <c>Product</c>. Taking Catalog out was deleting
/// one <c>DbSet</c> and moving one <c>IEntityTypeConfiguration</c>, because
/// there was no <c>Include</c> to unpick.
/// </para>
/// <para>
/// The mapping itself lives in the domains. Each folder carries its own
/// <see cref="IEntityTypeConfiguration{TEntity}"/> and this class only collects
/// them, so a domain's table shape is described in the same folder as its
/// resolvers.
/// </para>
/// </remarks>
public sealed class MosaicDbContext(DbContextOptions<MosaicDbContext> options)
    : DbContext(options)
{
    public DbSet<ProductPrice> Prices => Set<ProductPrice>();

    public DbSet<StockLevel> StockLevels => Set<StockLevel>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(MosaicDbContext).Assembly);

        UseSnakeCaseNames(modelBuilder);
    }

    /// <summary>
    /// Renames every table and column to snake_case.
    /// </summary>
    /// <remarks>
    /// PostgreSQL folds unquoted identifiers to lower case, so a table EF Core
    /// calls <c>StockLevels</c> has to be quoted in every hand-written query.
    /// Renaming once here means the SQL in the logs is the SQL you would type,
    /// which matters a great deal in a chapter that spends its time reading
    /// generated queries.
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

            // Complex properties are not in GetProperties, and forgetting them
            // is only visible in the SQL: Money's two columns arrive as
            // "Amount_Amount" and "Amount_Currency", quoted, in the middle of
            // an otherwise lower-case statement.
            foreach (var complex in entity.GetComplexProperties())
            {
                foreach (var property in complex.ComplexType.GetProperties())
                {
                    property.SetColumnName(ToSnakeCase(property.GetColumnName()));
                }
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
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
