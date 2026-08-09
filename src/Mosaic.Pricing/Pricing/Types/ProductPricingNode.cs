using HotChocolate.ApolloFederation.Types;
using Mosaic.Pricing.Catalog.Model;
using Mosaic.Pricing.Data;
using Mosaic.Pricing.Model;

namespace Mosaic.Pricing.Types;

/// <summary>
/// Pricing's contribution to the shared <c>Product</c> type. Catalog owns the
/// type; this class only hangs one more field off it, from its own folder.
/// </summary>
[ObjectType<Product>]
public static partial class ProductPricingNode
{
    /// <summary>What the product costs.</summary>
    public static async Task<Money> GetPriceAsync(
        [Parent("Id")] Product product,
        IPriceByProductIdDataLoader priceByProductId,
        CancellationToken cancellationToken)
    {
        var price = await priceByProductId.LoadAsync(product.Id, cancellationToken);

        // The field is non-nullable because every catalogued product is priced.
        // If that ever stops being true it is a seed-data bug, not something a
        // caller should have to handle.
        return price?.Amount
            ?? throw new InvalidOperationException(
                $"No price is seeded for product {product.Id}.");
    }

    /// <summary>What it costs to deliver one of these.</summary>
    /// <remarks>
    /// <para>
    /// The first field in Mosaic that cannot be answered out of Mosaic. The
    /// carrier's rate is a property of what the thing is - a wardrobe goes on a
    /// pallet and a tea towel goes in a bag - and what the thing is belongs to
    /// Catalog. <c>@requires</c> is the declaration that makes the router fetch
    /// <c>category</c> from there and put it in the representation it sends
    /// here, whether or not the client asked to see it.
    /// </para>
    /// <para>
    /// It is a computed field in the exact sense the specification means: half
    /// its input is remote and half is local. The category picks the rate; the
    /// price, which this service does own, decides whether the rate is waived.
    /// A field that only echoed the remote value back would not need
    /// <c>@requires</c> at all, because the router could read it from Catalog
    /// and never call this service.
    /// </para>
    /// <para>
    /// The throw is the honest branch rather than a defensive one. A category
    /// this resolver did not get is a category the router did not send, and the
    /// alternatives are to guess a rate or to answer a wrong one. Through the
    /// router it cannot happen; called directly with a representation carrying
    /// only a key, it happens immediately, and chapter 11 measures what a
    /// client sees when it does.
    /// </para>
    /// </remarks>
    [Requires("category")]
    public static async Task<Money> GetShippingCostAsync(
        [Parent] Product product,
        IPriceByProductIdDataLoader priceByProductId,
        CancellationToken cancellationToken)
    {
        var category = product.Category
            ?? throw new InvalidOperationException(
                $"Product {product.Id} arrived without a category, so its shipping "
                + "cost cannot be worked out. The field requires 'category' and the "
                + "representation did not carry one.");

        var rate = ShippingRates.For(category);

        // The same DataLoader Price uses, so asking for both fields on the same
        // page of products costs one statement rather than two: the second
        // LoadAsync finds the key already in the batch's cache.
        var price = await priceByProductId.LoadAsync(product.Id, cancellationToken);
        var amount = price?.Amount
            ?? throw new InvalidOperationException(
                $"No price is seeded for product {product.Id}.");

        return ShippingRates.IsWaived(category, amount.Amount)
            ? new Money(0m, amount.Currency)
            : new Money(rate, amount.Currency);
    }
}
