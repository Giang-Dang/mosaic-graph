using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mosaic.Api.Catalog.Model;

namespace Mosaic.Api.Catalog.Data;

/// <summary>
/// How Catalog stores a product.
/// </summary>
/// <remarks>
/// The configuration lives beside the resolvers that read it rather than in a
/// central mapping file, for the same reason the fields do: when Catalog leaves
/// this service it takes its table definition with it.
/// </remarks>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);

        // The enum is stored as text rather than as an integer. A category is
        // read far more often by a human debugging a query than by anything
        // that cares about four bytes, and renumbering an enum is a silent way
        // to corrupt a table.
        builder.Property(p => p.Category)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // Chapter 4's browseProducts field sorts on title by default and
        // filters on category, and the seeded catalog is small enough that
        // Postgres would ignore both indexes. They are here because the shape
        // of the query is what earns an index, not the row count on a laptop.
        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Title);
        builder.HasIndex(p => p.Category);
    }
}
