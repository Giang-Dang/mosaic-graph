using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Ordering.Model;

/// <summary>
/// Turns the <c>id</c> a representation carries back into the Guid this service
/// stores.
/// </summary>
/// <remarks>
/// The sixth copy of this decode in the repository, and the same argument holds
/// as for the other five: the format of a key is a contract between services,
/// and a shared class would turn an agreement into a project reference. The
/// only line that differs between any two of these files is the type name being
/// checked. See <c>Mosaic.Reviews.Model.ReviewKey</c> for why chapter 13 needed
/// two more of them.
/// </remarks>
public static class OrderKey
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

            if (nodeId.TypeName != nameof(Order) || nodeId.InternalId is not Guid decoded)
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
