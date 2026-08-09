using GreenDonut.Data;
using Microsoft.EntityFrameworkCore;
using Mosaic.ServiceDefaults.Counting;
using Mosaic.Reviews.Data;
using Mosaic.Reviews.Model;

namespace Mosaic.Reviews.Data;

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
public sealed class ReviewsService(ReviewsDbContext db, ServiceCallCounter counter)
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
    /// One page of reviews for each of several products, oldest first inside
    /// each page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaced the <c>ILookup</c> version in chapter 5, when
    /// <c>Product.reviews</c> became a connection. Fetching every review for
    /// every product on the page and then slicing in memory would have made the
    /// connection a lie: the point of paging a child collection is that the
    /// rows nobody asked for never leave the database.
    /// </para>
    /// <para>
    /// <c>ToBatchPageAsync</c> is what makes that possible in one statement. It
    /// rewrites the query into a window over the parent key, so PostgreSQL
    /// numbers each product's reviews and returns only the first <c>n</c> of
    /// each. The <c>OrderBy</c> here is not decoration either: keyset
    /// pagination reads the cursor keys off the ordering, and the tiebreaker on
    /// the identifier is what makes that ordering total.
    /// </para>
    /// <para>
    /// The gap-filling loop at the end is the part worth remembering. A
    /// dictionary answers an unknown key with null, and three of Mosaic's
    /// twenty-five products have never been reviewed. Chapter 4 solved exactly
    /// this by returning an <c>ILookup</c>, which is not available here because
    /// the value is a page rather than a sequence, so the empty answer has to
    /// be supplied deliberately. Delete these two lines and three products
    /// answer null for a non-nullable field.
    /// </para>
    /// </remarks>
    public async Task<Dictionary<Guid, Page<Review>>> GetReviewPagesByProductIdsAsync(
        IReadOnlyList<Guid> productIds,
        PagingArguments pagingArguments,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        var pages = await db.Reviews
            .AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId))
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToBatchPageAsync(r => r.ProductId, pagingArguments, cancellationToken);

        foreach (var productId in productIds)
        {
            pages.TryAdd(productId, Page<Review>.Empty);
        }

        return pages;
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

    /// <summary>Several reviews by their identifiers, keyed for the caller.</summary>
    /// <remarks>
    /// Added in chapter 5 for the same reason as Ordering's: <c>Review</c>
    /// implements <c>Node</c>, and a refetchable type owes the graph a way to
    /// find it by identifier alone.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, Review>> GetReviewsByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Reviews
            .AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);
    }

    /// <summary>
    /// Records one customer's review of one product, or refuses with the reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mosaic's first write. Two rules are enforced here because Reviews owns
    /// them: a rating is between one and five, and a customer reviews a product
    /// once. Whether the product and the customer exist is checked by the
    /// caller, which is a boundary that matters later - once Catalog is its own
    /// service, this domain cannot answer that question at all.
    /// </para>
    /// <para>
    /// The rules are checked before anything is added, and each one throws
    /// rather than returning a result union. The exception is not what the
    /// client sees: <c>submitReview</c> declares these as domain errors, so
    /// each one arrives as a type on the payload.
    /// </para>
    /// <para>
    /// No <c>AsNoTracking</c> on the duplicate check, and it does not need one:
    /// <c>AnyAsync</c> materialises no entity. The insert itself is the only
    /// thing in Mosaic that the change tracker has ever been asked to hold.
    /// </para>
    /// </remarks>
    public async Task<Review> SubmitReviewAsync(
        Guid productId,
        Guid customerId,
        int rating,
        string? body,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        if (rating is < 1 or > 5)
        {
            throw new RatingOutOfRangeException(rating);
        }

        var alreadyReviewed = await db.Reviews
            .AnyAsync(
                r => r.ProductId == productId && r.CustomerId == customerId,
                cancellationToken);

        if (alreadyReviewed)
        {
            throw new DuplicateReviewException(productId, customerId);
        }

        var review = new Review(
            Guid.CreateVersion7(),
            productId,
            customerId,
            rating,
            string.IsNullOrWhiteSpace(body) ? null : body.Trim(),
            DateTimeOffset.UtcNow);

        db.Reviews.Add(review);
        await db.SaveChangesAsync(cancellationToken);

        return review;
    }
}
