using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Catalog.Model;

/// <summary>
/// Turns the <c>id</c> a representation carries back into the Guid this service
/// stores.
/// </summary>
/// <remarks>
/// <para>
/// Chapter 5 made <c>Product.id</c> a global object identifier, so it answers
/// with a base64 string that carries the type name beside the key. Chapter 8
/// made that same field a federation <c>@key</c>, which means the opaque form
/// is what travels in every representation the router sends.
/// </para>
/// <para>
/// Nothing decodes it for you. <c>HotChocolate.ApolloFederation</c> 16.6.0
/// mentions relay identifiers nowhere in its source; a reference resolver's key
/// parameter is read straight out of the representation and converted to the
/// parameter's type, and <c>[ID]</c> on that parameter does nothing. Declare it
/// as a <c>Guid</c> and every lookup silently misses.
/// </para>
/// <para>
/// So the decode is explicit, and it is the same one HotChocolate performs for
/// an <c>[ID]</c> argument on an ordinary field: parse against the type of the
/// internal identifier, not against the entity type. The serializer lives in
/// the schema's services rather than the request's, which is why this takes a
/// resolver context instead of the accessor.
/// </para>
/// <para>
/// Mosaic.Api carries a copy of this file. That is deliberate and it is the
/// point of the chapter: the format of the key is a contract between two
/// services now, not a helper they share.
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
            // A representation carrying something that is not one of our
            // identifiers is a bad request, not a bug in this service. The
            // caller turns a false into a null entity, which is what the
            // specification's nullable [_Entity] return type is for.
            id = Guid.Empty;
            return false;
        }
    }
}
