namespace Mosaic.Inventory.Model;

/// <summary>
/// What Inventory knows about one product: where its stock sits, how much of
/// it a customer can buy today, and how much is already spoken for.
/// </summary>
/// <param name="ProductId">
/// The Catalog product this row counts. Inventory stores the identifier and
/// nothing else about the product; it never holds Catalog's <c>Product</c>.
/// </param>
/// <param name="WarehouseCode">The warehouse holding this product's stock.</param>
/// <param name="AvailableQuantity">Units a customer can buy right now.</param>
/// <param name="ReservedQuantity">Units on hand that open orders have already claimed.</param>
public sealed record StockLevel(
    Guid ProductId,
    string WarehouseCode,
    int AvailableQuantity,
    int ReservedQuantity);
