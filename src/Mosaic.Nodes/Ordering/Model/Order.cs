using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Nodes.Ordering.Model;

/// <summary>
/// An order, as the node service knows one: an identifier, and nothing else.
/// The second of the two types chapter 13 had to make into an entity before
/// this stub could mean anything.
/// </summary>
[Key("id", resolvable: false)]
public sealed class Order
{
    /// <summary>The order's global identifier, and the federation key.</summary>
    [ID]
    public required Guid Id { get; init; }
}
