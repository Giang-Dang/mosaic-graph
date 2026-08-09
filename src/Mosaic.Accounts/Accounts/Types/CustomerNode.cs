using HotChocolate.Types.Relay;
using Mosaic.Accounts.Data;
using Mosaic.Accounts.Model;

namespace Mosaic.Accounts.Types;

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

    /// <summary>Fetches one customer from their global identifier.</summary>
    /// <remarks>
    /// The same DataLoader <c>Review.author</c> and <c>Order.customer</c> use,
    /// which means a request that reaches a customer through the <c>node</c>
    /// field and again through a review fetches them once.
    /// </remarks>
    [NodeResolver]
    public static async Task<Customer?> ResolveCustomerAsync(
        Guid id,
        ICustomerByIdDataLoader customerById,
        CancellationToken cancellationToken)
        => await customerById.LoadAsync(id, cancellationToken);
}
