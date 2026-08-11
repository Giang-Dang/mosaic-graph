using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Ordering.Accounts.Model;

/// <summary>
/// Turns a customer's global object identifier back into the Guid Ordering
/// stores against an order.
/// </summary>
/// <remarks>
/// <para>
/// The seventh copy of this decode in the repository, and the first one that
/// does not exist to read a federation representation. Chapter 15 puts a
/// customer's global object identifier in a token's <c>sub</c> claim, so the
/// string this service has to decode arrives from an identity provider rather
/// than from the router, and the format is the same format.
/// </para>
/// <para>
/// That is the argument for the copy restated in a place it was not written
/// for. The shape of an identifier is a contract, and it now has one more party
/// to it: whatever mints tokens has to encode a customer the way Accounts
/// encodes one. Nothing compiles that agreement either.
/// </para>
/// </remarks>
public static class CustomerKey
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

            if (nodeId.TypeName != nameof(Customer) || nodeId.InternalId is not Guid decoded)
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
