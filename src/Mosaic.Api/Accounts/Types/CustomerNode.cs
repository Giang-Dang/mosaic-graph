using Mosaic.Api.Accounts.Model;

namespace Mosaic.Api.Accounts.Types;

/// <summary>
/// Accounts' own view of the shared <c>Customer</c> type. Reviews and Ordering
/// attach their fields to this same type from their own folders.
/// </summary>
[ObjectType<Customer>]
public static partial class CustomerNode
{
    /// <summary>The customer's global identifier.</summary>
    [ID]
    public static Guid GetId([Parent] Customer customer) => customer.Id;
}
