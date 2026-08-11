using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Reviews.Accounts.Model;

/// <summary>
/// Turns a customer's global object identifier back into the Guid Reviews
/// stores against a review.
/// </summary>
/// <remarks>
/// The eighth copy of this decode in the repository, and the second one chapter
/// 15 needed. Like Ordering's, it reads a token's <c>sub</c> claim rather than a
/// federation representation, and like every copy before it, it exists because
/// the format of an identifier is an agreement rather than a library. See
/// <c>Mosaic.Accounts.Model.CustomerKey</c> for the original argument and the
/// cost it admits to.
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
