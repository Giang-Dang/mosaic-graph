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

    /// <summary>Wraps a decoded identifier so the router can resolve the rest.</summary>
    [NodeResolver]
    public static Order ResolveOrder(Guid id) => new() { Id = id };
}
