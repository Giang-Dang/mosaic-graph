using HotChocolate.Types.Relay;
using Mosaic.Nodes.Catalog.Model;

namespace Mosaic.Nodes.Catalog.Types;

/// <summary>
/// What makes <c>Product</c> a <c>Node</c> here, and what <c>Query.node</c>
/// calls once it has decoded an identifier that names one.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole trick, and it is smaller than the problem made it sound.
/// HotChocolate's <c>node</c> field decodes the identifier itself, reads the
/// type name out of it, finds the node type of that name and calls its node
/// resolver with the internal identifier. Every service in Mosaic has had node
/// resolvers since chapter 5 and they all do a database lookup. This one does
/// not: it puts the key back in a wrapper and lets the router go and find the
/// product.
/// </para>
/// <para>
/// So nothing in this project parses a base64 string, and the six copies of
/// <c>ProductKey.TryDecode</c> elsewhere in the repository are not needed here.
/// Those exist because a reference resolver is handed the key raw and
/// <c>HotChocolate.ApolloFederation</c> does not know it is a node identifier.
/// The <c>node</c> field is the other side of the same coin: it is the one
/// place in HotChocolate that does know, because relay identifiers are its own
/// convention rather than federation's.
/// </para>
/// </remarks>
[ObjectType<Product>]
public static partial class ProductNode
{
    /// <summary>The product's global identifier.</summary>
    [ID]
    public static Guid GetId([Parent] Product product) => product.Id;

    /// <summary>Wraps a decoded identifier so the router can resolve the rest.</summary>
    [NodeResolver]
    public static Product ResolveProduct(Guid id) => new() { Id = id };
}
