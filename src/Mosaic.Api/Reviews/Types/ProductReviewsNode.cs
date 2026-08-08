using Mosaic.Api.Catalog.Model;
using Mosaic.Api.Reviews.Data;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Reviews.Types;

/// <summary>
/// Reviews' contribution to the shared <c>Product</c> type. Catalog owns the
/// type; this class hangs two more fields off it, from its own folder.
/// </summary>
/// <remarks>
/// Both fields are plain lists and plain numbers. There is no connection type
/// and no page size here, which is a decision rather than an oversight: a
/// later chapter turns <c>reviews</c> into a Relay connection and needs this
/// version to compare against.
/// </remarks>
[ObjectType<Product>]
public static partial class ProductReviewsNode
{
    /// <summary>Everything customers have said about this product.</summary>
    public static Task<IReadOnlyList<Review>> GetReviewsAsync(
        [Parent] Product product,
        ReviewsService reviews,
        CancellationToken cancellationToken)
        => reviews.GetReviewsByProductIdAsync(product.Id, cancellationToken);

    /// <summary>
    /// The mean of this product's ratings, or <c>null</c> if nobody has rated it.
    /// </summary>
    public static Task<double?> GetAverageRatingAsync(
        [Parent] Product product,
        ReviewsService reviews,
        CancellationToken cancellationToken)
        => reviews.GetAverageRatingByProductIdAsync(product.Id, cancellationToken);
}
