using Mosaic.Api.Catalog.Data;
using Mosaic.Api.Catalog.Model;
using Mosaic.Api.Ordering.Model;

namespace Mosaic.Api.Ordering.Types;

/// <summary>
/// Ordering's own view of the <c>OrderLine</c> type. The line stores a product
/// identifier; this class turns it into Catalog's <c>Product</c>.
/// </summary>
[ObjectType<OrderLine>]
public static partial class OrderLineNode
{
    /// <summary>The product this line sold.</summary>
    public static async Task<Product> GetProductAsync(
        [Parent] OrderLine line,
        IProductByIdDataLoader productById,
        CancellationToken cancellationToken)
    {
        var product = await productById.LoadAsync(line.ProductId, cancellationToken);

        // Non-nullable for the same reason as Order.customer: a line that sold
        // a product Catalog has never heard of is a seed-data bug.
        return product
            ?? throw new InvalidOperationException(
                $"An order line sold product {line.ProductId}, which is not in the catalog.");
    }
}
