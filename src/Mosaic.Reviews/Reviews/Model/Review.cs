using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Resolvers;
using Mosaic.Reviews.Data;

namespace Mosaic.Reviews.Model;

/// <summary>
/// One customer's opinion of one product. Reviews stores the two identifiers
/// and nothing else about either side: it never holds Catalog's <c>Product</c>
/// or Accounts' <c>Customer</c>.
/// </summary>
/// <remarks>
/// <para>
/// An entity since chapter 13, and it was not one for five chapters despite
/// implementing <c>Node</c> the whole time. The two are different promises and
/// the difference only bites in a federated graph. <c>Node</c> says a client
/// may hold this identifier and come back for the thing; <c>@key</c> says
/// <em>another service</em> may hand this identifier to the router and have the
/// thing found. A type with the first and not the second is one no router can
/// fetch, which is why a federated <c>node</c> field could not return a review
/// until this attribute existed.
/// </para>
/// <para>
/// Both federation attributes sit on the record rather than on
/// <c>ReviewNode</c>, for the reason chapter 8 measured:
/// <c>[ReferenceResolver]</c> inside an <c>[ObjectType&lt;T&gt;]</c> class
/// compiles to an ordinary public field, registers no reference resolver, and
/// leaves <c>_entities</c> answering <c>Unexpected Execution Error</c> with
/// nothing warning.
/// </para>
/// </remarks>
/// <param name="Id">The review's own identifier.</param>
/// <param name="ProductId">
/// The Catalog product being reviewed. Kept off the graph: a caller who has a
/// <c>Review</c> reached it through a product already, and the field would only
/// invite clients to re-fetch what they are holding.
/// </param>
/// <param name="CustomerId">
/// The Accounts customer who wrote it. Also kept off the graph; the
/// <c>author</c> field resolves the whole customer instead.
/// </param>
/// <param name="Rating">Stars, from 1 to 5.</param>
/// <param name="Body">
/// What the customer wrote, or <c>null</c> when they left a rating and no text.
/// </param>
/// <param name="CreatedAt">When the review was posted.</param>
[Key("id")]
public sealed record Review(
    Guid Id,
    [property: GraphQLIgnore] Guid ProductId,
    [property: GraphQLIgnore] Guid CustomerId,
    int Rating,
    string? Body,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Answers one representation: given a review's key, hand back the review.
    /// </summary>
    /// <remarks>
    /// The same shape Catalog's has, behind the same DataLoader the node
    /// resolver already used, which is the point worth noticing about making a
    /// type an entity late. Nothing about how a review is fetched changed. What
    /// changed is who is allowed to ask.
    /// </remarks>
    [ReferenceResolver]
    public static async Task<Review?> ResolveReferenceAsync(
        string id,
        IResolverContext context,
        IReviewByIdDataLoader reviewById,
        CancellationToken cancellationToken)
        => ReviewKey.TryDecode(id, context, out var reviewId)
            ? await reviewById.LoadAsync(reviewId, cancellationToken)
            : null;
}
