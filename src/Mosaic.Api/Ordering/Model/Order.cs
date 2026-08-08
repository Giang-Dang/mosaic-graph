namespace Mosaic.Api.Ordering.Model;

/// <summary>
/// One order a customer placed, with the lines it was made of.
/// </summary>
/// <param name="Id">The order's identifier.</param>
/// <param name="CustomerId">
/// The Accounts customer who placed the order. Ordering stores the identifier
/// and nothing else about the customer. The field is hidden from the schema
/// because callers reach the customer through <c>Order.customer</c> instead.
/// </param>
/// <param name="PlacedAt">When the order was placed.</param>
/// <param name="Lines">The products on the order. Never empty.</param>
public sealed record Order(
    Guid Id,
    [property: GraphQLIgnore] Guid CustomerId,
    DateTimeOffset PlacedAt,
    IReadOnlyList<OrderLine> Lines);
