using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mosaic.Accounts.Model;

namespace Mosaic.Accounts.Data;

/// <summary>
/// How Accounts stores a customer.
/// </summary>
public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(254).IsRequired();

        builder.HasIndex(c => c.Email).IsUnique();
    }
}
