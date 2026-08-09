using HotChocolate.Subscriptions;
using Mosaic.Api.Accounts.Data;
using Mosaic.Api.Accounts.Model;
using Mosaic.Api.Catalog.Data;
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
/// <c>ResolverTask</c> and chapter 4 spent on <c>DbContext</c> lifetimes. It
/// matters here for the first time: the <c>ReviewsService</c> that writes the
/// row and the <c>CatalogService</c> that checked the product a moment earlier
/// are holding the same <c>MosaicDbContext</c>. Top-level mutation fields run
/// serially, so nothing races them for it.
/// </para>
/// </remarks>
[MutationType]
public static partial class ReviewMutations
{
    /// <summary>Records one customer's review of one product.</summary>
    /// <remarks>
    /// <para>
    /// The four <c>[Error]</c> attributes are the whole error model. Each names
    /// an error class, each of those declares a factory taking the exception it
    /// represents, and between them they generate the union
    /// <c>SubmitReviewError</c> on the payload. An exception that is not one of
    /// the four is not a domain error: it goes to the <c>errors</c> array as an
    /// unexpected execution error, which is exactly what should happen to a
    /// bug.
    /// </para>
    /// <para>
    /// The two existence checks are on borrowed time and it is worth saying so
    /// in the code rather than only in the book. Reviews can ask Catalog
    /// whether a product exists because Catalog is a folder in the same
    /// process. Once it is a separate service the honest answer is that this
    /// domain cannot know, and the check becomes either a network call on the
    /// write path or an accepted risk.
    /// </para>
    /// </remarks>
    [Error<ProductNotFoundError>]
    [Error<CustomerNotFoundError>]
    [Error<RatingOutOfRangeError>]
    [Error<DuplicateReviewError>]
    public static async Task<Review> SubmitReviewAsync(
        [ID<Product>] Guid productId,
        [ID<Customer>] Guid customerId,
        int rating,
        string? body,
        CatalogService catalog,
        AccountsService accounts,
        ReviewsService reviews,
        ITopicEventSender sender,
        CancellationToken cancellationToken)
    {
        if (await catalog.GetProductByIdAsync(productId, cancellationToken) is null)
        {
            throw new ProductNotFoundException(productId);
        }

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
