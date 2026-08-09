using HotChocolate.Subscriptions;
using Mosaic.Api.Accounts.Data;
using Mosaic.Api.Accounts.Model;
using Mosaic.Api.Catalog.Model;
using Mosaic.Api.Reviews.Data;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Reviews.Types;

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
/// <c>ResolverTask</c> and chapter 4 spent on <c>DbContext</c> lifetimes. The
/// <c>AccountsService</c> that checks the customer and the
/// <c>ReviewsService</c> that writes the row are holding the same
/// <c>MosaicDbContext</c>. Top-level mutation fields run serially, so nothing
/// races them for it.
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
    /// There were four until chapter 8, and the one that left is the debt this
    /// comment used to say was coming. Reviews could ask Catalog whether a
    /// product existed for as long as Catalog was a folder in the same process.
    /// It is a separate service now, so the honest answer is that this domain
    /// cannot know, and the choice was between a network call on the write path
    /// and an accepted risk. This takes the risk: an unknown product
    /// identifier is now stored without complaint, and
    /// <c>ProductNotFoundError</c> is gone from the payload union - a breaking
    /// change for any client that was handling it. Chapter 11 has
    /// <c>@requires</c>, which is the closest federation comes to giving the
    /// check back, and chapter 12 argues about whether it is worth taking.
    /// </para>
    /// </remarks>
    [Error<CustomerNotFoundError>]
    [Error<RatingOutOfRangeError>]
    [Error<DuplicateReviewError>]
    public static async Task<Review> SubmitReviewAsync(
        [ID<Product>] Guid productId,
        [ID<Customer>] Guid customerId,
        int rating,
        string? body,
        AccountsService accounts,
        ReviewsService reviews,
        ITopicEventSender sender,
        CancellationToken cancellationToken)
    {
        if (await accounts.GetCustomerByIdAsync(customerId, cancellationToken) is null)
        {
            throw new CustomerNotFoundException(customerId);
        }

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

        return review;
    }
}
