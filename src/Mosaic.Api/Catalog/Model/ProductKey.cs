using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Api.Catalog.Model;

/// <summary>
/// Turns the <c>id</c> a representation carries back into the Guid this service
/// stores against prices, stock levels, reviews and order lines.
/// </summary>
/// <remarks>
/// <para>
/// A copy of <c>Mosaic.Catalog</c>'s file of the same name, and the duplication
/// is the chapter's point rather than an oversight. The encoded form of a
/// product identifier is a contract between two services now. Sharing a class
/// between them would make it a compile-time dependency and hide the fact that
/// the two have to agree about a string.
/// </para>
/// <para>
/// The parse is against the type of the internal identifier, <c>Guid</c>, not
/// against the entity type: that is the same call HotChocolate makes for an
/// <c>[ID]</c> argument on an ordinary field, and passing the entity type
/// throws <c>NodeIdMissingSerializerException</c>. The serializer lives in the
/// schema's services rather than the request's, which is why this takes a
/// resolver context rather than the accessor.
/// </para>
/// <para>
/// The type name check is what stops a customer identifier from being read as a
/// product one. Both encode to base64 and only the prefix says which is which.
/// </para>
/// </remarks>
public static class ProductKey
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

            if (nodeId.TypeName != nameof(Product) || nodeId.InternalId is not Guid decoded)
            {
                id = Guid.Empty;
                return false;
            }

            id = decoded;
            return true;
        }
        catch (Exception)
        {
            id = Guid.Empty;
            return false;
        }
    }
}
