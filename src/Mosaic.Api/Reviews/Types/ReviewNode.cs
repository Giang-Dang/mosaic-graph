using Mosaic.Api.Accounts.Data;
using Mosaic.Api.Accounts.Model;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Reviews.Types;

/// <summary>
/// Reviews' own view of the <c>Review</c> type.
/// </summary>
[ObjectType<Review>]
public static partial class ReviewNode
{
    /// <summary>The review's global identifier.</summary>
    [ID]
    public static Guid GetId([Parent] Review review) => review.Id;

    /// <summary>The customer who wrote the review.</summary>
    /// <remarks>
    /// One customer, fetched one at a time, per review. Ask a product for its
    /// reviews and every author on the page costs an Accounts lookup of its own.
    /// The shape is deliberate and a later chapter is about fixing it.
    /// </remarks>
    public static async Task<Customer> GetAuthorAsync(
        [Parent] Review review,
        AccountsService accounts,
        CancellationToken cancellationToken)
    {
        var author = await accounts.GetCustomerByIdAsync(review.CustomerId, cancellationToken);

        // The field is non-nullable because a review cannot exist without the
        // customer who wrote it. An author we cannot find is a seed-data bug,
        // not something a caller should have to handle.
        return author
            ?? throw new InvalidOperationException(
                $"Review {review.Id} names customer {review.CustomerId}, who is not seeded.");
    }
}
