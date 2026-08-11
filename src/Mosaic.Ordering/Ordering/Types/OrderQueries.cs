using System.Security.Claims;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using Mosaic.Ordering.Data;
using Mosaic.Ordering.Model;
using Mosaic.Ordering.Security;
using Mosaic.ServiceDefaults.Security;

namespace Mosaic.Ordering.Types;

/// <summary>
/// The Ordering domain's entry into the graph. Orders are reached through the
/// customer who placed them; there is no field that lists every order.
/// </summary>
[QueryType]
public static partial class OrderQueries
{
    /// <summary>Every order one customer has placed.</summary>
    /// <remarks>
    /// <para>
    /// Chapter 15, and three rules on one field, each of which the previous one
    /// cannot express.
    /// </para>
    /// <para>
    /// <c>[RequiresScopes]</c> is answered by the router before any subgraph is
    /// called. It asks whether the token was granted <c>orders:read</c> and
    /// knows nothing about which orders.
    /// </para>
    /// <para>
    /// <c>[Authorize]</c> asks the same question again inside this process,
    /// because this process is reachable on port 5106 without going through the
    /// router at all. It is the same rule written a second time in a second
    /// vocabulary, and nothing in the build checks that the scope named in the
    /// attribute above matches the scope the policy below tests for.
    /// </para>
    /// <para>
    /// The <c>if</c> is the rule neither of them can state. A scope says what
    /// kind of thing you may do; it cannot say whose. This field takes the
    /// customer as an argument, so without the check any holder of
    /// <c>orders:read</c> could read anybody's order history, and the two
    /// declarations above would both be satisfied while it happened.
    /// </para>
    /// <para>
    /// The argument stays rather than being replaced by the token's subject,
    /// which would be the tighter design for a graph that only ever serves
    /// people. Keeping it is what leaves room for a caller acting on somebody
    /// else's behalf - a support tool, a data export - to be allowed later by
    /// widening this one condition, rather than by adding a second field that
    /// does the same thing without the check.
    /// </para>
    /// </remarks>
    [RequiresScopes([MosaicTokens.Scopes.OrdersRead])]
    [Authorize(MosaicTokens.Policies.OrdersRead)]
    public static Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(
        [ID] Guid customerId,
        OrderingService ordering,
        ClaimsPrincipal? user,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        if (!OrderAccess.IsSelf(user, context, customerId))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You may only read your own orders.")
                    .SetCode("AUTH_NOT_AUTHORIZED")
                    .Build());
        }

        return ordering.GetOrdersByCustomerAsync(customerId, cancellationToken);
    }
}
