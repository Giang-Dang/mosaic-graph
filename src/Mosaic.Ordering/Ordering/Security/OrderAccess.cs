using System.Security.Claims;
using HotChocolate.Resolvers;
using Mosaic.Ordering.Accounts.Model;
using Mosaic.ServiceDefaults.Security;

namespace Mosaic.Ordering.Security;

/// <summary>
/// Who is asking, and whether the order they asked for is theirs.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of chapter 15 that no directive can do. <c>@authenticated</c>
/// and <c>@requiresScopes</c> are answered by the router from the token alone,
/// before anything has been fetched, which is exactly why they are cheap and
/// exactly why they cannot express the rule that matters most here: an order
/// belongs to one customer, and the question is not whether you have a scope
/// but whether you are that customer.
/// </para>
/// <para>
/// Answering it needs two things a router does not have. It needs the order,
/// which means the fetch has already happened. And it needs to know that
/// <c>Order.CustomerId</c> is the field that decides, which is domain knowledge
/// living in exactly one service. So the check is code, in the service that
/// owns the data, and the graph has no way to advertise that it exists.
/// </para>
/// </remarks>
public static class OrderAccess
{
    /// <summary>
    /// The customer behind the caller's token, or null if there is no token or
    /// its subject is not a customer identifier this service can read.
    /// </summary>
    /// <remarks>
    /// Two ways to get null and they are worth keeping apart in your head even
    /// though the caller treats them the same: nobody presented a token, or
    /// somebody presented a valid token whose subject is not a Mosaic customer.
    /// The second is a correctly signed token from a correctly configured
    /// issuer that this graph still cannot place, which is what happens the
    /// first time a service account calls a field written for people.
    /// </remarks>
    public static Guid? CallerCustomerId(ClaimsPrincipal? user, IResolverContext context)
    {
        var subject = MosaicSecurityDefaults.SubjectOrNull(user);

        if (subject is null)
        {
            return null;
        }

        return CustomerKey.TryDecode(subject, context, out var customerId)
            ? customerId
            : null;
    }

    /// <summary>
    /// Whether the caller is the customer named.
    /// </summary>
    public static bool IsSelf(ClaimsPrincipal? user, IResolverContext context, Guid customerId)
        => CallerCustomerId(user, context) is { } caller && caller == customerId;
}
