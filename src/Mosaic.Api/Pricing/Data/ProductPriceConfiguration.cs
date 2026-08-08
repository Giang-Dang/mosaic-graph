using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mosaic.Api.Pricing.Model;

namespace Mosaic.Api.Pricing.Data;

/// <summary>
/// How Pricing stores what a product costs.
/// </summary>
/// <remarks>
/// The key is the product identifier, and there is no foreign key to the
/// products table. Pricing knows an identifier; it does not know Catalog. That
/// is what lets the two become separate services with separate databases later
/// without a migration that drops constraints.
/// </remarks>
public sealed class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> builder)
    {
        builder.HasKey(p => p.ProductId);

        // Money is a complex type, not an owned entity. Both would put amount
        // and currency in this table, and only one of them can be a
        // constructor parameter: an owned type is an entity without a key, so
        // it arrives as a navigation, and EF Core will not bind a navigation
        // to a constructor parameter. A complex type is a value, so it binds
        // like any other property and ProductPrice stays a positional record.
        builder.ComplexProperty(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasPrecision(12, 2).IsRequired();
            money.Property(m => m.Currency).HasMaxLength(3).IsRequired();
        });
    }
}
