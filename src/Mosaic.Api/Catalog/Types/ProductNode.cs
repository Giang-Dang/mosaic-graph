using HotChocolate.Types.Relay;
using Mosaic.Api.Catalog.Data;
using Mosaic.Api.Catalog.Model;

namespace Mosaic.Api.Catalog.Types;

/// <summary>
/// Catalog's own view of the shared <c>Product</c> type. The other domains
/// attach their fields to this same type from their own folders.
/// </summary>
[ObjectType<Product>]
public static partial class ProductNode
{
    /// <summary>The product's global identifier.</summary>
    /// <remarks>
    /// The <c>Requires</c> on <c>[Parent]</c> is not decoration. This field is
    /// backed by a resolver rather than by the property, because <c>[ID]</c>
    /// encodes the raw Guid, and a projection builds its <c>SELECT</c> list
    /// from properties. Without the declaration, asking <c>browseProducts</c>
    /// for <c>id</c> hands the resolver a product whose <c>Id</c> was never
    /// selected: every product answers with an all-zero Guid, and the cursors
    /// built from the sort tiebreaker go with it.
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
    /// A DataLoader rather than a service call, because <c>nodes(ids: [...])</c>
    /// resolves many identifiers in one request and each one arrives here
    /// separately.
    /// </para>
    /// </remarks>
    [NodeResolver]
    public static async Task<Product?> ResolveProductAsync(
        Guid id,
        IProductByIdDataLoader productById,
        CancellationToken cancellationToken)
        => await productById.LoadAsync(id, cancellationToken);
}
