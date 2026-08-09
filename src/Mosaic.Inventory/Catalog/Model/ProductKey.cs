using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Inventory.Catalog.Model;

/// <summary>
/// Turns the <c>id</c> a representation carries back into the Guid this service
/// stores against.
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
/// Chapter 8 duplicated this file once, between two services, and said the
/// duplication was the point. Chapter 12 duplicated it three more times, which
/// is where a reader is entitled to ask whether the point still holds. It does,
/// and the reason is in the name of the type it parses against: this file
/// encodes one subgraph's belief about how another subgraph spells a product.
/// A shared class would make that belief a compile-time fact, and the whole
/// value of a federated graph is that it is not one. What a shared library can
/// hold is the things no two services have to agree about, which is why
/// <c>Mosaic.ServiceDefaults</c> holds a request timeline and not this.
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