using HotChocolate.Types.Relay;
using Mosaic.Ordering.Accounts.Model;
using Mosaic.Ordering.Data;
using Mosaic.Ordering.Model;

namespace Mosaic.Ordering.Types;

/// <summary>
/// Ordering's own view of the <c>Order</c> type: the identifier, the customer
/// behind the stored identifier, and the order's total.
/// </summary>
[ObjectType<Order>]
public static partial class OrderNode
{
    /// <summary>The order's global identifier.</summary>
    [ID]
    public static Guid GetId([Parent] Order order) => order.Id;

    /// <summary>Fetches one order from its global identifier.</summary>
    /// <remarks>
    /// <c>Order</c> implements <c>Node</c> and <c>OrderLine</c> does not, which
    /// is the distinction the interface is for. An order has an identity a
    /// client can hold on to and come back for. A line has no identifier at all
    /// outside the order it belongs to - chapter 4 gave it a shadow key
    /// precisely because the domain had none - so promising that one could be
    /// refetched would be promising something that does not exist.
    /// </remarks>
    [NodeResolver]
    public static async Task<Order?> ResolveOrderAsync(
        Guid id,
        IOrderByIdDataLoader orderById,
        CancellationToken cancellationToken)
        => await orderById.LoadAsync(id, cancellationToken);

    /// <summary>The customer who placed the order.</summary>
    /// <remarks>
    /// <para>
    /// The same shape <c>Review.author</c> now has, and the two used to share
    /// more than a shape: they called the same DataLoader instance, so a query
    /// that reached a customer through their orders and again through their
    /// reviews fetched them once and handed both fields the same object.
    /// </para>
    /// <para>
    /// That is gone, and it is the clearest thing this chapter cost. Reviews and
    /// Ordering are separate processes, so there is no shared request scope and
    /// no shared cache. What replaces it is the router's own batching: both
    /// fields hand back keys, both sets of keys end up in entity fetches against
    /// Accounts, and whether they end up in the <em>same</em> fetch is the
    /// router's decision rather than ours. Chapter 17 is where the planner's
    /// side of that is read properly.
    /// </para>
    /// <para>
    /// Nullable since chapter 12, for the reason every reference across a
    /// boundary is: this service cannot check that the customer exists and
    /// cannot stop the router from failing to find one.
    /// </para>
    /// </remarks>
    public static Customer? GetCustomer([Parent] Order order)
        => new() { Id = order.CustomerId };

    /// <summary>What the order came to: every line's quantity times its unit price.</summary>
    /// <remarks>
    /// Computed from the lines already in hand, so no service is asked
    /// anything. Ordering seeds a single currency, so the total takes its
    /// currency from the first line.
    /// </remarks>
    public static Money GetTotal([Parent] Order order)
    {
        if (order.Lines.Count == 0)
        {
            throw new InvalidOperationException($"Order {order.Id} has no lines.");
        }

        var amount = order.Lines.Sum(line => line.Quantity * line.UnitPrice.Amount);
        return new Money(amount, order.Lines[0].UnitPrice.Currency);
    }
}
