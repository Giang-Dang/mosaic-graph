using Microsoft.EntityFrameworkCore;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Infrastructure.Data;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Reviews.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Reviews domain.
/// </summary>
/// <remarks>
/// Two shapes of the same two questions. The single-key methods are the ones
/// chapters 2 and 3 measured, and they are still here because a root field that
/// genuinely wants one product's reviews should not have to pretend it wants
/// many. The list-taking methods below them are what the DataLoaders call, and
/// they are the only reason a page of twenty-five products costs one statement
/// rather than twenty-five.
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

    /// <summary>
    /// Every review written about any of several products, grouped by product
    /// and oldest first inside each group.
    /// </summary>
    /// <remarks>
    /// One statement, whatever the number of keys: <c>Contains</c> over a list
    /// becomes <c>WHERE product_id = ANY(@keys)</c> against PostgreSQL, which
    /// is a single parameter rather than one per key, so the plan is reusable
    /// no matter how many products the page holds.
    /// <para>
    /// The grouping happens in memory, on rows that have already arrived. Doing
    /// it in SQL would mean a <c>GROUP BY</c> that has to aggregate the review
    /// bodies into arrays, which is more work for the database and no less for
    /// the process.
    /// </para>
    /// </remarks>
    public async Task<ILookup<Guid, Review>> GetReviewsByProductIdsAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        var reviews = await db.Reviews
            .AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId))
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        return reviews.ToLookup(r => r.ProductId);
    }

    /// <summary>
    /// The mean rating for each of several products. A product with no reviews
    /// is absent from the dictionary rather than present with a zero.
    /// </summary>
    /// <remarks>
    /// The average is computed by PostgreSQL, one row per product, so the 120
    /// review rows never cross the wire at all.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, double?>> GetAverageRatingsByProductIdsAsync(
        IReadOnlyList<Guid> productIds,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        var averages = await db.Reviews
            .AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId))
            .GroupBy(r => r.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Average = group.Average(r => (double?)r.Rating)
            })
            .ToListAsync(cancellationToken);

        return averages.ToDictionary(a => a.ProductId, a => a.Average);
    }
}
