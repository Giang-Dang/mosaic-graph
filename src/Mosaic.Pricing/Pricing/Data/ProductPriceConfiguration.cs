using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mosaic.Pricing.Model;

namespace Mosaic.Pricing.Data;

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

        // Money is a complex type, not an owned entity. Both put amount and
        // currency in this table; the difference is what EF Core thinks it is
        // looking at. An owned type is an entity without a key of its own, so
        // it is tracked and it arrives as a navigation. A complex type is a
        // value, with no identity and no tracking, which is what Money is.
        //
        // Neither can be a constructor parameter. EF Core's error names only
        // navigations, but measured against 10.0.10 a complex property is
        // refused the same way, which is why ProductPrice carries its Amount
        // as an init property rather than as a positional record parameter.
        builder.ComplexProperty(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasPrecision(12, 2).IsRequired();
            money.Property(m => m.Currency).HasMaxLength(3).IsRequired();
        });
    }
}
