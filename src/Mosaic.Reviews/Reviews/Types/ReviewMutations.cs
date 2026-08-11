using HotChocolate.Resolvers;
using HotChocolate.Subscriptions;
using Mosaic.Reviews.Accounts.Model;
using Mosaic.Reviews.Catalog.Model;
using Mosaic.Reviews.Data;
using Mosaic.Reviews.Model;
using Mosaic.Reviews.Streams;

namespace Mosaic.Reviews.Types;

/// <summary>
/// Mosaic's first write.
/// </summary>
/// <remarks>
/// <para>
/// Four chapters of a read-only graph, and the first mutation arrives here
/// rather than earlier for a reason: a mutation is where a schema has to answer
/// what happens when the answer is no, and that question is this chapter's.
/// </para>
/// <para>
/// The method takes loose parameters and returns a <c>Review</c>. Mutation
/// conventions turn that into <c>submitReview(input: SubmitReviewInput!):
/// SubmitReviewPayload!</c>, which is the shape every mutation in this book
/// will have from here on: one input argument, one payload, and the payload
/// carrying the errors.
/// </para>
/// <para>
/// A mutation field shares the request's dependency injection scope, where a
/// query field gets one of its own - the distinction chapter 3 read out of
/// <c>ResolverTask</c> and chapter 4 spent on <c>DbContext</c> lifetimes. There
/// is only one service left in the signature to benefit from that, which is
/// itself the story of chapter 12.
/// </para>
/// </remarks>
[MutationType]
public static partial class ReviewMutations
{
    /// <summary>Records one customer's review of one product.</summary>
    /// <remarks>
    /// <para>
    /// The three <c>[Error]</c> attributes are the whole error model. Each
    /// names an error class, each of those declares a factory taking the
    /// exception it represents, and between them they generate the union
    /// <c>SubmitReviewError</c> on the payload. An exception that is not one of
    /// the three is not a domain error: it goes to the <c>errors</c> array as
    /// an unexpected execution error, which is exactly what should happen to a
    /// bug.
    /// </para>
    /// <para>
    /// There were four until chapter 8 and there are two now, and the two that
    /// left are the same loss twice. Reviews could ask Catalog whether a product
    /// existed for as long as Catalog was a folder in this process, and it could
    /// ask Accounts whether a customer existed for as long as Accounts was.
    /// Chapter 8 took the first away and this chapter takes the second, so
    /// <c>ProductNotFoundError</c> and <c>CustomerNotFoundError</c> are both
    /// gone from the payload union. Each removal is a breaking change for any
    /// client that was handling it.
    /// </para>
    /// <para>
    /// Chapter 8 called that a decision. Doing it a second time is closer to a
    /// consequence, and it is worth naming the alternatives that were rejected
    /// rather than pretending there were none. A call to Accounts on the write
    /// path would give the check back and put a second service's availability
    /// in front of every review anybody submits. <c>@requires</c> cannot help
    /// here: it feeds a field resolver on an entity the router has already
    /// located, and this mutation is handed a customer identifier by a client
    /// that may have invented it. Leaving <c>CustomerNotFoundError</c> in the
    /// union with nothing able to raise it would be the worst of the three - a
    /// branch a client will write and never reach.
    /// </para>
    /// <para>
    /// What is actually lost is smaller than it looks and larger than it
    /// sounds. A review whose author does not exist is now storable, and
    /// nothing here will refuse it. What surfaces instead is
    /// <c>Review.author</c> answering null at the router, which is why that
    /// field stopped being non-nullable in this chapter. The check moved from
    /// write time to read time, and from an error a client can handle to a null
    /// it has to.
    /// </para>
    /// </remarks>
    [Error<RatingOutOfRangeError>]
    [Error<DuplicateReviewError>]
    public static async Task<Review> SubmitReviewAsync(
        [ID<Product>] Guid productId,
        [ID<Customer>] Guid customerId,
        int rating,
        string? body,
        ReviewsService reviews,
        ITopicEventSender sender,
        ReviewStreamPublisher stream,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var review = await reviews.SubmitReviewAsync(
            productId,
            customerId,
            rating,
            body,
            cancellationToken);

        // Published after the write has committed, not before. A subscriber
        // that is told about a review which then fails to save has been lied
        // to, and there is no message to take it back.
        await sender.SendAsync(
            ReviewTopics.ReviewAdded(productId),
            review,
            cancellationToken);

        // The same fact, announced a second time and to a different audience.
        // Chapter 14 added this line and did not remove the one above, because
        // the two arrangements are the chapter's argument rather than a
        // migration: one subscription is a field of this service and one is a
        // field of no service. Both fire from here so that a reader can watch
        // them answer the same write.
        await stream.PublishAsync(review, productId, context, cancellationToken);

        return review;
    }
}
