using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Reviews.Model;

/// <summary>
/// Turns the <c>id</c> a representation carries back into the Guid this service
/// stores.
/// </summary>
/// <remarks>
/// <para>
/// The fifth copy of this file in the repository and the third shape of it.
/// Catalog, Pricing, Inventory and Reviews all carry one that decodes a
/// <c>Product</c> key; this one decodes a <c>Review</c> key, and the only
/// difference between them is the type name being checked. Chapter 12 argued
/// that this duplication is the price of not turning a wire contract into a
/// build dependency, and chapter 13 pays it again rather than reopening it.
/// </para>
/// <para>
/// What is new here is why the file exists at all. Until chapter 13 a review
/// was not an entity: it had a <c>Node</c> identifier and no <c>@key</c>, so
/// the only way to reach one was through the product it was written about. A
/// federated <c>node</c> field needs the opposite of that. The router has to be
/// able to take a review's identifier on its own and find the review, which is
/// what a resolvable key means and what the reference resolver on
/// <see cref="Review"/> now does.
/// </para>
/// </remarks>
public static class ReviewKey
{
    public static bool TryDecode(string formattedId, IResolverContext context, out Guid id)
    {
        var services = context.Schema.Services;
        var accessor = services?.GetService(typeof(INodeIdSerializerAccessor))
            as INodeIdSerializerAccessor;

        if (accessor is null)
        {
            id = Guid.Empty;
            return false;
        }

        try
        {
            var nodeId = accessor.Serializer.Parse(formattedId, typeof(Guid));

            if (nodeId.TypeName != nameof(Review) || nodeId.InternalId is not Guid decoded)
            {
                id = Guid.Empty;
                return false;
            }

            id = decoded;
            return true;
        }
        catch (Exception)
        {
            // A representation carrying something that is not one of our
            // identifiers is a bad request, not a bug in this service. The
            // caller turns a false into a null entity, which is what the
            // specification's nullable [_Entity] return type is for.
            id = Guid.Empty;
            return false;
        }
    }
}
