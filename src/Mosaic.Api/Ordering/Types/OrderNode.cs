using Mosaic.Api.Accounts.Data;
using Mosaic.Api.Accounts.Model;
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

    /// <summary>The customer who placed the order.</summary>
    /// <remarks>
    /// One Accounts lookup per order, every time. Ask for ten orders and
    /// Accounts is asked ten questions, even when nine of them are about the
    /// same customer. That is the shape this chapter is here to show; the fix
    /// comes later and does not change this field's signature.
    /// </remarks>
    public static async Task<Customer> GetCustomerAsync(
        [Parent] Order order,
        AccountsService accounts,
        CancellationToken cancellationToken)
    {
        var customer = await accounts.GetCustomerByIdAsync(order.CustomerId, cancellationToken);

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
