using Mosaic.Api.Infrastructure;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Reviews.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Reviews domain.
/// </summary>
/// <remarks>
/// One product identifier in, one answer out, as in every other domain here.
/// Nothing on this class takes a list of identifiers, so a query that walks a
/// page of products asks Reviews about each of them separately. That is the
/// behaviour this version of Mosaic is meant to show.
/// </remarks>
public sealed class ReviewsService(InMemoryReviewsData data, ServiceCallCounter counter)
{
    /// <summary>
    /// Every review written about one product, in the order Reviews stored them,
    /// which for the seeded data means oldest first. A product nobody has
    /// reviewed gets an empty list rather than <c>null</c>: "no reviews yet" is a
    /// normal state, not a missing answer.
    /// </summary>
    public async Task<IReadOnlyList<Review>> GetReviewsByProductIdAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        await counter.RecordLookupAsync(cancellationToken);
        return [.. data.Reviews.Where(r => r.ProductId == productId)];
    }

    /// <summary>
    /// The mean rating for one product, or <c>null</c> when it has no reviews.
    /// There is no honest number to return in that case, and zero is not it.
    /// </summary>
    public async Task<double?> GetAverageRatingByProductIdAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        await counter.RecordLookupAsync(cancellationToken);

        var ratings = data.Reviews
            .Where(r => r.ProductId == productId)
            .Select(r => r.Rating)
            .ToList();

        if (ratings.Count == 0)
        {
            return null;
        }

        return ratings.Average();
    }
}
