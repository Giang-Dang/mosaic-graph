using Microsoft.EntityFrameworkCore;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Infrastructure.Data;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Reviews.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Reviews domain.
/// </summary>
/// <remarks>
/// One product identifier in, one answer out, as in every other domain here.
/// Nothing on this class takes a list of identifiers, so a query that walks a
/// page of products asks Reviews about each of them separately. Behind a list
/// in memory that cost nothing; behind PostgreSQL it is one statement, one
/// network round trip and one query plan per product.
/// </remarks>
public sealed class ReviewsService(MosaicDbContext db, ServiceCallCounter counter)
{
    /// <summary>
    /// Every review written about one product, oldest first. A product nobody
    /// has reviewed gets an empty list rather than <c>null</c>: "no reviews
    /// yet" is a normal state, not a missing answer.
    /// </summary>
    public async Task<IReadOnlyList<Review>> GetReviewsByProductIdAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The mean rating for one product, or <c>null</c> when it has no reviews.
    /// There is no honest number to return in that case, and zero is not it.
    /// </summary>
    /// <remarks>
    /// The average is computed by PostgreSQL rather than in C#, so the rows
    /// never leave the database. <c>AverageAsync</c> on an empty sequence
    /// throws, which is why this asks for a nullable average over a projection
    /// instead: <c>avg()</c> over no rows is SQL null, and that is exactly the
    /// answer wanted.
    /// </remarks>
    public async Task<double?> GetAverageRatingByProductIdAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId)
            .Select(r => (double?)r.Rating)
            .AverageAsync(cancellationToken);
    }
}
