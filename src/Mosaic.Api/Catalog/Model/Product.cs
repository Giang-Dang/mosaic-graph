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
    /// Catalog's category, which this service does not own and cannot look up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[External]</c> publishes the field and disclaims it in the same
    /// breath: the type is part of this subgraph's schema so that
    /// <c>@requires(fields: "category")</c> has something to name, and the
    /// directive tells the composer this is not a place the value can be got
    /// from. Ask the router for <c>Product.category</c> and it goes to Catalog.
    /// Nothing routes here for it.
    /// </para>
    /// <para>
    /// The property has a setter, which is the part that is easy to get wrong.
    /// Nothing in this service ever assigns it. HotChocolate does, after the
    /// reference resolver has returned, by copying values out of the
    /// representation into the object that resolver built. A get-only property
    /// compiles, composes, and answers null for every product.
    /// </para>
    /// <para>
    /// The two nullabilities disagree on purpose, and each is right about a
    /// different thing. In the schema the field is <c>ProductCategory!</c>,
    /// because that is what Catalog says and an <c>@external</c> declaration is
    /// a copy of somebody else's contract: composition merges the two
    /// declarations and takes the more permissive nullability, so a nullable
    /// copy here would quietly make the field nullable for every client of the
    /// whole graph. In C# the property is nullable, because most
    /// representations carry no category at all - the router sends one only
    /// when the query reached a field that asked for it - and a non-nullable
    /// enum would report <c>FURNITURE</c> for absent, zero being a value of
    /// every C# enum.
    /// </para>
    /// </remarks>
    [External]
    [GraphQLNonNullType]
    public ProductCategory? Category { get; set; }

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
