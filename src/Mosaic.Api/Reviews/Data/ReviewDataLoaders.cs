using GreenDonut;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Reviews.Data;

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
    /// <summary>Reviews for many products at once, grouped by product.</summary>
    /// <remarks>
    /// The return type is what makes this a <em>group</em> DataLoader rather
    /// than a batch one, and the difference is what a missing key produces. An
    /// <c>ILookup</c> answers an unknown key with an empty sequence, which is
    /// exactly right for a product nobody has reviewed. Had this returned
    /// <c>Dictionary&lt;Guid, Review[]&gt;</c> it would still have compiled and
    /// still have batched, and three of the twenty-five products would have
    /// come back null.
    /// </remarks>
    [DataLoader]
    public static Task<ILookup<Guid, Review>> GetReviewsByProductIdAsync(
        IReadOnlyList<Guid> productIds,
        ReviewsService reviews,
        CancellationToken cancellationToken)
        => reviews.GetReviewsByProductIdsAsync(productIds, cancellationToken);

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
