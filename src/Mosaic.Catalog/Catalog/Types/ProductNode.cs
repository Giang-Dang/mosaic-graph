using HotChocolate.Types.Relay;
using Mosaic.Catalog.Data;
using Mosaic.Catalog.Model;

namespace Mosaic.Catalog.Types;

/// <summary>
/// Catalog's own view of the shared <c>Product</c> type. Until chapter 8 the
/// other domains attached their fields to this same class from their own
/// folders in the same assembly. They still attach them to the same GraphQL
/// type; they now do it from another service.
/// </summary>
/// <remarks>
/// What is <em>not</em> here is the interesting part. <c>[Key]</c> and
/// <c>[ReferenceResolver]</c> are on the <c>Product</c> record itself, not on
/// this class. See <see cref="Product"/> for why the second of those has no
/// choice.
/// </remarks>
[ObjectType<Product>]
public static partial class ProductNode
{
    /// <summary>The product's global identifier.</summary>
    /// <remarks>
    /// <para>
    /// The <c>Requires</c> on <c>[Parent]</c> is not decoration. This field is
    /// backed by a resolver rather than by the property, because <c>[ID]</c>
    /// encodes the raw Guid, and a projection builds its <c>SELECT</c> list
    /// from properties. Without the declaration, asking <c>browseProducts</c>
    /// for <c>id</c> hands the resolver a product whose <c>Id</c> was never
    /// selected: every product answers with an all-zero Guid, and the cursors
    /// built from the sort tiebreaker go with it.
    /// </para>
    /// <para>
    /// Since chapter 8 this field is also the federation key, which raises the
    /// stakes on what it answers. The encoded form carries the GraphQL type
    /// name, <c>Product</c>, beside the Guid, and both services name the type
    /// that - so both encode the same identifier to the same string. Rename the
    /// type in one of them and every representation stops matching, without a
    /// single composition error to warn you.
    /// </para>
    /// </remarks>
    [ID]
    public static Guid GetId([Parent("Id")] Product product) => product.Id;

    /// <summary>Fetches one product from its global identifier.</summary>
    /// <remarks>
    /// <para>
    /// This is what makes <c>Product</c> a <c>Node</c>. The source generator
    /// sees <c>[NodeResolver]</c> and emits
    /// <c>descriptor.ImplementsNode().ResolveNode(...)</c>, so there is no
    /// <c>[Node]</c> attribute on the class and no method-naming convention to
    /// obey.
    /// </para>
    /// <para>
    /// The rules the generator does enforce are worth knowing, because they are
    /// compile-time errors rather than runtime surprises. The first parameter
    /// must be named <c>id</c> exactly (HC0104); it must not carry <c>[ID]</c>,
    /// because a node resolver already declares it as one (HC0092); the method
    /// must be public (HC0093); and <c>id</c> must be the only field argument
    /// (HC0083).
    /// </para>
    /// <para>
    /// Nothing calls it at this tag. <c>Query.node</c> and <c>Query.nodes</c>
    /// left the schema in chapter 8, for the reason Program.cs gives, and until
    /// chapter 13 gives the graph a federated node field this resolver is a
    /// promise rather than a code path. It stays because
    /// <c>Product implements Node</c> is the promise, and taking it out would
    /// be a second breaking change in a chapter that already made one.
    /// </para>
    /// </remarks>
    [NodeResolver]
    public static async Task<Product?> ResolveProductAsync(
        Guid id,
        IProductByIdDataLoader productById,
        CancellationToken cancellationToken)
        => await productById.LoadAsync(id, cancellationToken);
}
