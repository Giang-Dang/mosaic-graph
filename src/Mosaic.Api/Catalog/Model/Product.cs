using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Api.Catalog.Model;

/// <summary>
/// A product, as Mosaic now knows one: an identifier, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// This is all that is left of Catalog in this service. The record that used to
/// live here carried a sku, a title, a description and a category; those went
/// to <c>Mosaic.Catalog</c> in chapter 8 and this service has no business
/// holding a copy of them. What it does hold is the key, because four of its
/// own domains have something to say about a product and every one of them says
/// it against an identifier.
/// </para>
/// <para>
/// The class name and the namespace are the same as before on purpose, which is
/// why <c>Pricing/Types/ProductPricingNode.cs</c>,
/// <c>Inventory/Types/ProductInventoryNode.cs</c> and
/// <c>Reviews/Types/ProductReviewsNode.cs</c> did not change by a character
/// when Catalog left. Those three classes are <c>[ObjectType&lt;Product&gt;]</c>
/// partials hanging fields off a type another folder declared, which was the
/// repository's layout rule from chapter 2 and is the reason this chapter is a
/// move rather than a rewrite.
/// </para>
/// <para>
/// Two <c>Product</c> types now exist in this repository, in two assemblies,
/// with different fields. They are the same GraphQL type because they carry the
/// same name and the same <c>@key</c>, and for no other reason.
/// </para>
/// </remarks>
[Key("id")]
public sealed class Product
{
    /// <summary>
    /// The product's global identifier, and the federation key.
    /// </summary>
    /// <remarks>
    /// <c>[ID]</c> on the property rather than a resolver, because nothing here
    /// projects a product out of a database - there is no table to project
    /// from. The attribute still encodes, because Program.cs still registers
    /// the node id serializer, and it has to: what this field answers is what
    /// the router will hand back to Catalog as a representation.
    /// </remarks>
    [ID]
    public required Guid Id { get; init; }

    /// <summary>
    /// Builds a product from the key another subgraph holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three lines, and no lookup, because there is nothing to look up. This
    /// service does not know which products exist; Catalog is the authority on
    /// that and Mosaic has no way to ask. Handed a key, it builds an object and
    /// lets its own domains answer for it. A key naming a product that was
    /// deleted yesterday gets a price and an empty review list, which is
    /// exactly the behaviour chapter 7 measured on the sample and is harmless
    /// only for as long as the router is the only caller. Chapter 25 is where
    /// the subgraph stops being reachable from outside the cluster.
    /// </para>
    /// <para>
    /// On the record rather than on a type extension class: see
    /// <see cref="ProductKey"/> and the equivalent comment in
    /// <c>Mosaic.Catalog</c>. A <c>[ReferenceResolver]</c> in the wrong place
    /// compiles, publishes a field nobody asked for, and registers no reference
    /// resolver.
    /// </para>
    /// </remarks>
    [ReferenceResolver]
    public static Product? ResolveReference(string id, IResolverContext context)
        => ProductKey.TryDecode(id, context, out var productId)
            ? new Product { Id = productId }
            : null;
}
