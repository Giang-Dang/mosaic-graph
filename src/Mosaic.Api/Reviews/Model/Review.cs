namespace Mosaic.Api.Reviews.Model;

/// <summary>
/// One customer's opinion of one product. Reviews stores the two identifiers
/// and nothing else about either side: it never holds Catalog's <c>Product</c>
/// or Accounts' <c>Customer</c>.
/// </summary>
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
public sealed record Review(
    Guid Id,
    [property: GraphQLIgnore] Guid ProductId,
    [property: GraphQLIgnore] Guid CustomerId,
    int Rating,
    string? Body,
    DateTimeOffset CreatedAt);
