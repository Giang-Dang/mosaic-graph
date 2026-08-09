using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;

namespace Mosaic.Accounts.Model;

/// <summary>
/// Turns the <c>id</c> a representation carries back into the Guid Accounts
/// stores.
/// </summary>
/// <remarks>
/// <para>
/// <c>ProductKey</c> with one type name changed, and the fifth file in this
/// repository to do this job. The reason it is a copy rather than a shared
/// helper is the same one chapter 8 gave and chapter 12 restates at four times
/// the scale: the format of a key is an agreement between services, and a
/// shared class turns an agreement into a build dependency.
/// </para>
/// <para>
/// What the copies do share is a mistake they would all make together. Every
/// one of them parses against <c>typeof(Guid)</c> and checks the type name in
/// the decoded identifier. If Mosaic ever changed how it encodes identifiers,
/// five files would have to change in five repositories owned by five teams,
/// and there is no build that would fail if one of them did not. That is the
/// real cost of the decoupling, and it is paid in coordination rather than in
/// code.
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
