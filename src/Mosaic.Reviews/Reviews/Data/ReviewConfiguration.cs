using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mosaic.Reviews.Model;

namespace Mosaic.Reviews.Data;

/// <summary>
/// How Reviews stores a review.
/// </summary>
/// <remarks>
/// Two identifiers, no navigations. The composite index on product and posting
/// time is the one that matters for this chapter: the DataLoader that replaces
/// the N+1 fetches reviews for twenty-five products in one statement, and that
/// statement is a range scan over this index rather than a sequential scan of
/// the table.
/// </remarks>
public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Body).HasMaxLength(4000);

        builder.HasIndex(r => new { r.ProductId, r.CreatedAt });
        builder.HasIndex(r => r.CustomerId);
    }
}
