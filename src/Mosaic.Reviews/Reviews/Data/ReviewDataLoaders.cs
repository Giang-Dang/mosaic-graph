using GreenDonut;
using GreenDonut.Data;
using Mosaic.Reviews.Model;

namespace Mosaic.Reviews.Data;

/// <summary>
/// Reviews' DataLoaders. The source generator turns each method here into a
/// class and an interface named after it, with <c>Get</c> and <c>Async</c>
/// stripped: <c>GetReviewsByProductIdAsync</c> becomes
/// <c>ReviewsByProductIdDataLoader</c> and <c>IReviewsByProductIdDataLoader</c>.
/// </summary>
/// <remarks>
/// <para>
/// The class does not have to be <c>partial</c>, unlike a type marked
/// <c>[QueryType]</c> or <c>[ObjectType&lt;T&gt;]</c>. Nothing is added to it;
/// the generated code is a separate class that calls into it.
/// </para>
/// <para>
/// Registration is already done. <c>[assembly: Module("Mosaic")]</c> makes the
/// generator emit an <c>AddDataLoader</c> call for each of these into the same
/// <c>AddMosaic()</c> that registers the types, so there is no second
/// <c>[DataLoaderModule]</c> attribute and no list to keep in step.
/// </para>
/// </remarks>
public static class ReviewDataLoaders
{
    /// <summary>One page of reviews for many products at once.</summary>
    /// <remarks>
    /// <para>
    /// Chapter 4's version returned an <c>ILookup&lt;Guid, Review&gt;</c> and was
    /// a <em>group</em> DataLoader. Chapter 5 turned <c>Product.reviews</c> into
    /// a connection, and a page is not a sequence, so this is a batch DataLoader
    /// again - which brings back the null-for-a-missing-key behaviour the
    /// <c>ILookup</c> was chosen to avoid. The empty page for an unreviewed
    /// product is supplied by the service instead.
    /// </para>
    /// <para>
    /// <c>PagingArguments</c> is not an ordinary service parameter. The
    /// generator recognises it and reads it out of the DataLoader's own state,
    /// which is where the <c>.With(pagingArguments)</c> call in the resolver
    /// puts it. That call also branches the DataLoader: a distinct page shape
    /// gets a distinct branch with its own cache and its own batch, so two
    /// fields asking for different page sizes in one request cannot be handed
    /// each other's answers.
    /// </para>
    /// </remarks>
    [DataLoader]
    public static Task<Dictionary<Guid, Page<Review>>> GetReviewsByProductIdAsync(
        IReadOnlyList<Guid> productIds,
        PagingArguments pagingArguments,
        ReviewsService reviews,
        CancellationToken cancellationToken)
        => reviews.GetReviewPagesByProductIdsAsync(productIds, pagingArguments, cancellationToken);

    /// <summary>Reviews by identifier, in one batch.</summary>
    /// <remarks>
    /// Backs <c>Review</c>'s node resolver. <c>nodes(ids:)</c> can ask for
    /// several at once, so this is a batch rather than a single lookup.
    /// </remarks>
    [DataLoader]
    public static Task<IReadOnlyDictionary<Guid, Review>> GetReviewByIdAsync(
        IReadOnlyList<Guid> ids,
        ReviewsService reviews,
        CancellationToken cancellationToken)
        => reviews.GetReviewsByIdsAsync(ids, cancellationToken);

    /// <summary>Mean ratings for many products at once.</summary>
    /// <remarks>
    /// A batch DataLoader, because one product has one average. The value type
    /// is nullable so that "nobody has rated this" survives the trip: a key
    /// absent from the dictionary and a key present with a null both arrive at
    /// the resolver as null, and both mean the same thing here.
    /// </remarks>
    [DataLoader]
    public static Task<IReadOnlyDictionary<Guid, double?>> GetAverageRatingByProductIdAsync(
        IReadOnlyList<Guid> productIds,
        ReviewsService reviews,
        CancellationToken cancellationToken)
        => reviews.GetAverageRatingsByProductIdsAsync(productIds, cancellationToken);
}
