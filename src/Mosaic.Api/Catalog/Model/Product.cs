namespace Mosaic.Api.Catalog.Model;

/// <summary>
/// A product as the Catalog domain understands it: what the thing is, not what
/// it costs, whether you can have one, or what anybody thought of it. Those
/// fields exist on the GraphQL type, but they are contributed by Pricing,
/// Inventory and Reviews from their own folders.
/// </summary>
public sealed record Product(
    Guid Id,
    string Sku,
    string Title,
    string? Description,
    ProductCategory Category);
