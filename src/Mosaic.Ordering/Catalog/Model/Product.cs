using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Ordering.Catalog.Model;

/// <summary>
/// A product, as Ordering knows one: the identifier a line was sold against.
/// </summary>
/// <remarks>
/// <para>
/// The smallest of the four <c>Product</c> stubs, and the only one with no
/// reference resolver. Pricing, Inventory and Reviews each contribute a field
/// to <c>Product</c>, so each of them has to be able to answer for one. Ordering
/// contributes nothing to <c>Product</c>; it only points at products, through
/// <c>OrderLine.product</c>.
/// </para>
/// <para>
/// <c>resolvable: false</c> says exactly that, and it is the difference between
/// a subgraph that participates in an entity and one that merely references it.
/// Without the flag this service would be declaring that it can turn a product
/// key into a product, the router would believe it, and the graph would answer
/// null for half its products.
/// </para>
/// </remarks>
[Key("id", resolvable: false)]
public sealed class Product
{
    /// <summary>The product's global identifier, and the federation key.</summary>
    [ID]
    public required Guid Id { get; init; }
}