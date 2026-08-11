using System.Security.Claims;
using HotChocolate.Types.Relay;
using Mosaic.Nodes.Ordering.Model;

namespace Mosaic.Nodes.Ordering.Types;

/// <summary>
/// What makes <c>Order</c> a <c>Node</c> here. See
/// <c>Mosaic.Nodes.Catalog.Types.ProductNode</c> for the mechanism.
/// </summary>
/// <remarks>
/// <c>OrderLine</c> has no stub in this project and never will, which is the
/// distinction the <c>Node</c> interface is for. A line has no identifier
/// outside the order it belongs to, so there is nothing for a client to hold
/// and nothing for this service to hand back.
/// </remarks>
[ObjectType<Order>]
public static partial class OrderNode
{
    /// <summary>The order's global identifier.</summary>
    [ID]
    public static Guid GetId([Parent] Order order) => order.Id;

    /// <summary>
    /// Wraps a decoded identifier so the router can resolve the rest, for a
    /// caller who is at least somebody.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chapter 15, and the only authorization decision this service is capable
    /// of making. It knows the type, because decoding the identifier is the
    /// whole of its job and the type name is inside the identifier. It does not
    /// know whose order this is and never will: there is no database here, no
    /// <c>_entities</c>, and the record below is two lines with an identifier
    /// in it.
    /// </para>
    /// <para>
    /// So the rule is coarse on purpose. Orders are not public, and this is the
    /// last point at which that can be said before the identifier turns into a
    /// stub the router will happily answer <c>id</c> and <c>__typename</c> from
    /// without asking anybody anything. Measured before this existed: an
    /// anonymous <c>node(id:)</c> on somebody's order returned the identifier
    /// and the type name, because both are carried by the argument and no
    /// subgraph was ever called.
    /// </para>
    /// <para>
    /// Whose order it is stays with Ordering, in the reference resolver on
    /// <c>Mosaic.Ordering.Model.Order</c>. The two checks are not redundant and
    /// neither can be moved: this one refuses a type to a stranger, that one
    /// refuses an instance to the wrong person, and only the second one has the
    /// row in front of it.
    /// </para>
    /// <para>
    /// <c>ClaimsPrincipal?</c> and not <c>ClaimsPrincipal</c>. The nullable form
    /// hands a resolver null for an anonymous caller; the non-nullable form
    /// throws <c>ArgumentException</c> out of the parameter binder instead,
    /// which turns "nobody is signed in" into an execution error.
    /// </para>
    /// </remarks>
    [NodeResolver]
    public static Order? ResolveOrder(Guid id, ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true
            ? new Order { Id = id }
            : null;
}
