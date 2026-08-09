namespace Mosaic.Ordering.Model;

/// <summary>
/// One order a customer placed, with the lines it was made of.
/// </summary>
/// <remarks>
/// The lines left the primary constructor when chapter 4 moved Ordering onto
/// Entity Framework Core, and they left it twice over. A constructor parameter
/// has to bind to a mapped property, and a collection of owned entities is a
/// navigation rather than a property, so EF Core refused the type outright:
/// "only mapped properties can be bound to constructor parameters". The
/// collection also has to be one EF Core can add to while it materialises a
/// row, which rules out <c>IReadOnlyList</c>. Neither change touches the
/// GraphQL field.
/// </remarks>
/// <param name="Id">The order's identifier.</param>
/// <param name="CustomerId">
/// The Accounts customer who placed the order. Ordering stores the identifier
/// and nothing else about the customer. The field is hidden from the schema
/// because callers reach the customer through <c>Order.customer</c> instead.
/// </param>
/// <param name="PlacedAt">When the order was placed.</param>
public sealed record Order(
    Guid Id,
    [property: GraphQLIgnore] Guid CustomerId,
    DateTimeOffset PlacedAt)
{
    /// <summary>The products on the order. Never empty.</summary>
    public List<OrderLine> Lines { get; init; } = [];
}
