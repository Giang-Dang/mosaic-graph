using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Reviews.Catalog.Model;

/// <summary>
/// A product, as Reviews knows one: an identifier, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Catalog owns <c>Product</c>. This service hangs two fields, <c>reviews</c> and <c>averageRating</c> off it,
/// and needs a class of that name with that key to hang them from. Pricing and
/// Reviews carry a stub like this one, Pricing's with an <c>@external</c> copy
/// of the category it requires, and Ordering carries one that resolves nothing
/// at all.
/// </para>
/// <para>
/// The key is the whole of the agreement between the two services. Everything
/// else about a product - what it is called, what it costs to ship, whether it
/// still exists - is somebody else's to answer.
/// </para>
/// </remarks>
[Key("id")]
public sealed class Product
{
    /// <summary>The product's global identifier, and the federation key.</summary>
    [ID]
    public required Guid Id { get; init; }

    /// <summary>Builds a product from the key another subgraph holds.</summary>
    /// <remarks>
    /// No lookup, because there is nothing to look up: this service does not
    /// know which products exist. A key naming a product deleted yesterday gets
    /// an answer, which is harmless only for as long as the router is the only
    /// caller. Chapter 25 is where a subgraph stops being reachable from
    /// outside the cluster.
    /// </remarks>
    [ReferenceResolver]
    public static Product? ResolveReference(string id, IResolverContext context)
        => ProductKey.TryDecode(id, context, out var productId)
            ? new Product { Id = productId }
            : null;
}