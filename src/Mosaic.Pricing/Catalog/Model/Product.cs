using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Pricing.Catalog.Model;

/// <summary>
/// A product, as Pricing knows one: an identifier, a category it is handed, and
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The fourth copy of this class in the repository, and counting them is the
/// honest way to describe what chapter 12 cost. Catalog owns the real
/// <c>Product</c>; Pricing, Inventory and Reviews each carry a stub like this
/// one so that they have somewhere to hang their own fields, and Ordering
/// carries a smaller one still because it only points at products. They are the
/// same GraphQL type because they carry the same name and the same
/// <c>@key</c>, and for no other reason.
/// </para>
/// <para>
/// A shared project holding one <c>Product</c> would remove the duplication and
/// replace it with something worse: a build dependency between five teams on a
/// class whose whole purpose is to describe a contract they negotiate over a
/// wire. The duplication is the decoupling. What is genuinely shared -
/// diagnostics, counters, the block of builder calls that makes a subgraph
/// composable - lives in <c>Mosaic.ServiceDefaults</c>, and the test for
/// whether something belongs there is whether two services have to agree about
/// it.
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
    /// from. The attribute still encodes, because
    /// <c>AddGlobalObjectIdentification</c> is part of the subgraph defaults
    /// every service calls, and it has to: what this field answers is what the
    /// router will hand back to Catalog as a representation.
    /// </remarks>
    [ID]
    public required Guid Id { get; init; }

    /// <summary>
    /// Catalog's category, which this service does not own and cannot look up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one field that survived the move out of <c>Mosaic.Api</c> unchanged
    /// and still needs all three of its attributes. <c>[External]</c> publishes
    /// the field and disclaims it: the type needs it in this subgraph's schema
    /// so that <c>@requires(fields: "category")</c> has something to name, and
    /// the directive tells the composer this is not a place the value can be
    /// got from.
    /// </para>
    /// <para>
    /// The setter is the mechanism rather than an oversight - nothing in this
    /// service ever assigns it, and HotChocolate does, after the reference
    /// resolver returns. The two nullabilities disagree on purpose: the schema
    /// says what Catalog says, and the property says what a representation
    /// actually carries. Chapter 11 is where both were measured.
    /// </para>
    /// <para>
    /// Only Pricing carries this. Inventory and Reviews have a <c>Product</c>
    /// stub with an identifier and nothing else, because neither of them has a
    /// field that needs to know what a product is.
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
    /// Three lines and no lookup, because there is nothing to look up. This
    /// service does not know which products exist; Catalog is the authority on
    /// that and Pricing has no way to ask. Handed a key, it builds an object
    /// and lets its own fields answer for it.
    /// </para>
    /// <para>
    /// On the record rather than on the <c>[ObjectType&lt;Product&gt;]</c>
    /// class: chapter 8 measured what happens when it is the other way round,
    /// and the answer is that it compiles, publishes a field nobody asked for,
    /// and registers no reference resolver.
    /// </para>
    /// </remarks>
    [ReferenceResolver]
    public static Product? ResolveReference(string id, IResolverContext context)
        => ProductKey.TryDecode(id, context, out var productId)
            ? new Product { Id = productId }
            : null;
}
