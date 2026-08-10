using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Resolvers;
using Mosaic.Catalog.Data;

namespace Mosaic.Catalog.Model;

/// <summary>
/// A product as the Catalog domain understands it: what the thing is, not what
/// it costs, whether you can have one, or what anybody thought of it. Those
/// fields still exist on the GraphQL type; since chapter 8 they are contributed
/// by another service.
/// </summary>
/// <remarks>
/// <para>
/// <c>[Key("id")]</c> is what makes this an entity: it tells every other
/// subgraph how to name one of these, and it is what puts <c>Product</c> into
/// this service's <c>_Entity</c> union.
/// </para>
/// <para>
/// Both federation attributes are on the record rather than on
/// <c>ProductNode</c>, the <c>[ObjectType&lt;Product&gt;]</c> class next door,
/// and that is not a style choice. <c>[Key]</c> works in either place;
/// <c>[ReferenceResolver]</c> does not. Put it on a method in the type
/// extension class and the source generator turns it into an ordinary field,
/// which lands in the published schema as <c>resolveByReference(...)</c> while
/// no reference resolver is registered at all. The schema still composes, the
/// router still calls <c>_entities</c>, and the answer is
/// <c>Unexpected Execution Error</c>. Nothing warns.
/// </para>
/// </remarks>
[Key("id")]
public sealed record Product(
    Guid Id,
    string Sku,
    string Title,
    string? Description,
    ProductCategory Category)
{
    /// <summary>
    /// A parameterless constructor, which exists for one caller and is not the
    /// one a reader would guess.
    /// </summary>
    /// <remarks>
    /// <c>QueryContext&lt;T&gt;.Include</c> builds its selector as
    /// <c>Expression.MemberInit(Expression.New(typeof(T)), ...)</c>, and
    /// <c>Expression.New(Type)</c> wants a parameterless constructor. A
    /// positional record has none, so <c>Include</c> throws for one and the
    /// only symptom is <c>Unexpected Execution Error</c> on the field that
    /// called it. This constructor is what lets
    /// <c>CatalogService.BrowseProductsAsync</c> put the cursor's tiebreaker
    /// back into the projection; the long comment there says what goes wrong
    /// without it.
    /// </remarks>
    public Product()
        : this(Guid.Empty, string.Empty, string.Empty, null, default)
    {
    }

    /// <summary>
    /// Answers one representation: given the key another subgraph holds, hand
    /// back the product it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key is taken as a <c>string</c> because that is what arrives. See
    /// <see cref="ProductKey"/> for why it cannot be a <c>Guid</c>.
    /// </para>
    /// <para>
    /// Behind a DataLoader, so a batch of twenty-five representations in one
    /// <c>_entities</c> call costs one statement rather than twenty-five. That
    /// is the same fix chapter 4 applied to a nested field, arriving at the
    /// entry point the router uses, and it is why this service can be asked
    /// about a whole page of products at once without going back to chapter 2's
    /// numbers.
    /// </para>
    /// <para>
    /// A key that decodes to nothing, or to some other type's identifier,
    /// returns null rather than throwing. The specification's
    /// <c>[_Entity]</c> is nullable for exactly this.
    /// </para>
    /// </remarks>
    [ReferenceResolver]
    public static async Task<Product?> ResolveReferenceAsync(
        string id,
        IResolverContext context,
        IProductByIdDataLoader productById,
        CancellationToken cancellationToken)
        => ProductKey.TryDecode(id, context, out var productId)
            ? await productById.LoadAsync(productId, cancellationToken)
            : null;
}
