using System.Security.Claims;
using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Resolvers;
using Mosaic.Ordering.Data;
using Mosaic.Ordering.Security;

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
    /// Answers one representation: given an order's key, hand back the order,
    /// if it belongs to whoever is asking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Behind the DataLoader the node resolver already used, which loads the
    /// lines with the order. <c>Order.total</c> throws for an order with no
    /// lines, so a reference resolver that returned a lines-less order would
    /// turn a <c>node</c> query into an execution error rather than an order.
    /// </para>
    /// <para>
    /// Chapter 15 added the second half, and the reason is the most useful
    /// thing in that chapter. Guarding <c>Query.ordersByCustomer</c> guards one
    /// door. This is the other one: <c>Query.node</c> lives in
    /// <c>Mosaic.Nodes</c>, takes an identifier, decodes it far enough to know
    /// it names an <c>Order</c>, and hands the router a stub - and the router
    /// then comes here, with a representation, through a code path that has
    /// never heard of <c>ordersByCustomer</c> or its rules. Measured before
    /// this check existed: any client could read any order by identifier while
    /// the query field beside it refused them.
    /// </para>
    /// <para>
    /// Null rather than an error, unlike the query field. A representation the
    /// caller may not have is indistinguishable, from outside, from one that
    /// does not exist, and that is the answer to give: an error here would tell
    /// somebody guessing identifiers which of their guesses were real.
    /// <c>[_Entity]</c> is nullable in the specification for the
    /// not-found case, and this borrows it.
    /// </para>
    /// <para>
    /// This resolver can only do any of it if the caller's token reached this
    /// process, which is a router configuration rather than anything visible
    /// here. Take the <c>headers</c> block out of <c>router/config.yaml</c> and
    /// every line below still runs, sees nobody, and refuses the owner their
    /// own order.
    /// </para>
    /// </remarks>
    [ReferenceResolver]
    public static async Task<Order?> ResolveReferenceAsync(
        string id,
        IResolverContext context,
        ClaimsPrincipal? user,
        IOrderByIdDataLoader orderById,
        CancellationToken cancellationToken)
    {
        if (!OrderKey.TryDecode(id, context, out var orderId))
        {
            return null;
        }

        var order = await orderById.LoadAsync(orderId, cancellationToken);

        if (order is null || !OrderAccess.IsSelf(user, context, order.CustomerId))
        {
            return null;
        }

        return order;
    }
}
