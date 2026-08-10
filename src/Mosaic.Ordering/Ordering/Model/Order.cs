using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Resolvers;
using Mosaic.Ordering.Data;

namespace Mosaic.Ordering.Model;

/// <summary>
/// One order a customer placed, with the lines it was made of.
/// </summary>
/// <remarks>
/// <para>
/// The lines left the primary constructor when chapter 4 moved Ordering onto
/// Entity Framework Core, and they left it twice over. A constructor parameter
/// has to bind to a mapped property, and a collection of owned entities is a
/// navigation rather than a property, so EF Core refused the type outright:
/// "only mapped properties can be bound to constructor parameters". The
/// collection also has to be one EF Core can add to while it materialises a
/// row, which rules out <c>IReadOnlyList</c>. Neither change touches the
/// GraphQL field.
/// </para>
/// <para>
/// A federation entity since chapter 13, for the same reason
/// <c>Mosaic.Reviews.Model.Review</c> became one: implementing <c>Node</c> was
/// never enough to let a router find one. This is also the first resolvable key
/// Ordering has ever declared, so <c>_entities</c> appears in its published
/// schema for the first time. Its two other types, <c>Customer</c> and
/// <c>Product</c>, carry <c>resolvable: false</c> keys and are references this
/// service makes rather than entities it can answer for.
/// </para>
/// </remarks>
/// <param name="Id">The order's identifier.</param>
/// <param name="CustomerId">
/// The Accounts customer who placed the order. Ordering stores the identifier
/// and nothing else about the customer. The field is hidden from the schema
/// because callers reach the customer through <c>Order.customer</c> instead.
/// </param>
/// <param name="PlacedAt">When the order was placed.</param>
[Key("id")]
public sealed record Order(
    Guid Id,
    [property: GraphQLIgnore] Guid CustomerId,
    DateTimeOffset PlacedAt)
{
    /// <summary>The products on the order. Never empty.</summary>
    public List<OrderLine> Lines { get; init; } = [];

    /// <summary>
    /// Answers one representation: given an order's key, hand back the order.
    /// </summary>
    /// <remarks>
    /// Behind the DataLoader the node resolver already used, which loads the
    /// lines with the order. <c>Order.total</c> throws for an order with no
    /// lines, so a reference resolver that returned a lines-less order would
    /// turn a <c>node</c> query into an execution error rather than an order.
    /// </remarks>
    [ReferenceResolver]
    public static async Task<Order?> ResolveReferenceAsync(
        string id,
        IResolverContext context,
        IOrderByIdDataLoader orderById,
        CancellationToken cancellationToken)
        => OrderKey.TryDecode(id, context, out var orderId)
            ? await orderById.LoadAsync(orderId, cancellationToken)
            : null;
}
