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
    /// This is the field that cost a hundred and twenty lookups until chapter
    /// 4. It still runs a hundred and twenty times: the engine resolves one
    /// author per review and nothing about that changed. What changed is that
    /// each of those calls now hands a key to a DataLoader instead of asking
    /// Accounts a question, and the hundred and twenty keys collapse to the
    /// twelve distinct customers who wrote them.
    /// </remarks>
    public static async Task<Customer> GetAuthorAsync(
        [Parent] Review review,
        ICustomerByIdDataLoader customerById,
        CancellationToken cancellationToken)
    {
        var author = await customerById.LoadAsync(review.CustomerId, cancellationToken);

        // The field is non-nullable because a review cannot exist without the
        // customer who wrote it. An author we cannot find is a seed-data bug,
        // not something a caller should have to handle.
        return author
            ?? throw new InvalidOperationException(
                $"Review {review.Id} names customer {review.CustomerId}, who is not seeded.");
    }
}
