using HotChocolate.Types.Relay;
using Mosaic.Reviews.Accounts.Model;
using Mosaic.Reviews.Data;
using Mosaic.Reviews.Model;

namespace Mosaic.Reviews.Types;

/// <summary>
/// Reviews' own view of the <c>Review</c> type.
/// </summary>
[ObjectType<Review>]
public static partial class ReviewNode
{
    /// <summary>The review's global identifier.</summary>
    [ID]
    public static Guid GetId([Parent] Review review) => review.Id;

    /// <summary>Fetches one review from its global identifier.</summary>
    /// <remarks>
    /// A review is worth refetching on its own account. A client that has just
    /// submitted one, or that is showing a permalink to it, holds an identifier
    /// and nothing else. That was not possible before chapter 5: the only route
    /// to a review was through the product it was written about.
    /// </remarks>
    [NodeResolver]
    public static async Task<Review?> ResolveReviewAsync(
        Guid id,
        IReviewByIdDataLoader reviewById,
        CancellationToken cancellationToken)
        => await reviewById.LoadAsync(id, cancellationToken);

    /// <summary>The customer who wrote the review.</summary>
    /// <remarks>
    /// <para>
    /// This is the field that cost a hundred and twenty lookups until chapter 4
    /// and twelve lines of code until chapter 12. It used to hand a key to a
    /// DataLoader and get a whole customer back out of the same database. Now it
    /// wraps the key it is already holding and lets Accounts answer.
    /// </para>
    /// <para>
    /// Two things are worth counting about that. It got cheaper here: no
    /// DataLoader, no batch, no statement, so a page of a hundred and twenty
    /// reviews costs this service nothing at all for its authors. And it got
    /// more expensive somewhere else: the router makes a second call, and the
    /// twelve distinct customers behind those hundred and twenty reviews arrive
    /// at Accounts as twelve representations in one batch. The work did not go
    /// away. It moved, and grew a network hop on the way.
    /// </para>
    /// <para>
    /// The field is nullable, and until this chapter it was not. Chapter 11
    /// measured what a non-null field does when the entity behind it cannot be
    /// resolved: the null walks outwards through every non-null link until it
    /// finds one that will hold it, and through
    /// <c>reviews { nodes { author } }</c> that is the whole response. A review
    /// naming a customer nobody has heard of used to be a seed-data bug that
    /// threw inside one process. It is now a dangling reference across a
    /// network, which is a thing a distributed system has rather than a bug it
    /// has, and the schema is where that gets admitted.
    /// </para>
    /// </remarks>
    public static Customer? GetAuthor([Parent] Review review)
        => new() { Id = review.CustomerId };
}
