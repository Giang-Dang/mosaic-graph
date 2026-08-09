using HotChocolate.Types.Relay;
using Mosaic.Api.Accounts.Data;
using Mosaic.Api.Accounts.Model;
using Mosaic.Api.Ordering.Data;
using Mosaic.Api.Ordering.Model;
using Mosaic.Api.Pricing.Model;

namespace Mosaic.Api.Ordering.Types;

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
    /// It is the same DataLoader <c>Review.author</c> uses, and that is worth
    /// noticing rather than glossing over. One instance per request means one
    /// cache per request, so a query that reaches a customer through their
    /// orders and again through their reviews fetches them once and hands both
    /// fields the same object.
    /// </remarks>
    public static async Task<Customer> GetCustomerAsync(
        [Parent] Order order,
        ICustomerByIdDataLoader customerById,
        CancellationToken cancellationToken)
    {
        var customer = await customerById.LoadAsync(order.CustomerId, cancellationToken);

        // The field is non-nullable because an order cannot exist without the
        // customer who placed it. A missing one is a seed-data bug, not
        // something a caller should have to handle.
        return customer
            ?? throw new InvalidOperationException(
                $"Order {order.Id} was placed by customer {order.CustomerId}, who is not seeded.");
    }

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
