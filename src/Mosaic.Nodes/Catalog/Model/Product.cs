using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Nodes.Catalog.Model;

/// <summary>
/// A product, as the node service knows one: an identifier, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The same two-line stub Reviews carries for <c>Customer</c> and Ordering
/// carries for <c>Product</c>, and it is here for the same reason: this service
/// needs to be able to <em>name</em> the type without being able to answer any
/// question about it. <c>resolvable: false</c> is the half that makes the
/// second part true. The router will never send a representation here.
/// </para>
/// <para>
/// What is different is why there are four of these rather than one. A service
/// that resolves <c>node</c> has to be able to return any type the graph
/// considers globally addressable, and a GraphQL resolver can only return a
/// type its own schema declares. So the cost of a federated <c>node</c> field
/// is one file per addressable type in the whole graph, in the one service that
/// owns the field. That is real coupling and the chapter does not pretend
/// otherwise; what it buys is that the coupling is in a service whose only job
/// is to hold it, rather than smeared across six that have other work to do.
/// </para>
/// </remarks>
[Key("id", resolvable: false)]
public sealed class Product
{
    /// <summary>The product's global identifier, and the federation key.</summary>
    [ID]
    public required Guid Id { get; init; }
}
