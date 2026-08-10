using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Nodes.Accounts.Model;

/// <summary>
/// A customer, as the node service knows one: an identifier, and nothing else.
/// See <c>Mosaic.Nodes.Catalog.Model.Product</c> for why there are four of
/// these.
/// </summary>
[Key("id", resolvable: false)]
public sealed class Customer
{
    /// <summary>The customer's global identifier, and the federation key.</summary>
    [ID]
    public required Guid Id { get; init; }
}
