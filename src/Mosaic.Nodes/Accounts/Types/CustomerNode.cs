using HotChocolate.Types.Relay;
using Mosaic.Nodes.Accounts.Model;

namespace Mosaic.Nodes.Accounts.Types;

/// <summary>
/// What makes <c>Customer</c> a <c>Node</c> here. See
/// <c>Mosaic.Nodes.Catalog.Types.ProductNode</c> for the mechanism.
/// </summary>
[ObjectType<Customer>]
public static partial class CustomerNode
{
    /// <summary>The customer's global identifier.</summary>
    [ID]
    public static Guid GetId([Parent] Customer customer) => customer.Id;

    /// <summary>Wraps a decoded identifier so the router can resolve the rest.</summary>
    [NodeResolver]
    public static Customer ResolveCustomer(Guid id) => new() { Id = id };
}
