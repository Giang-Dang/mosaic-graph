using Mosaic.Api.Catalog.Data;
using Mosaic.Api.Catalog.Model;

namespace Mosaic.Api.Catalog.Types;

/// <summary>
/// The Catalog domain's entries into the graph.
/// </summary>
[QueryType]
public static partial class CatalogQueries
{
    /// <summary>Every product Mosaic sells.</summary>
    public static Task<IReadOnlyList<Product>> GetProductsAsync(
        CatalogService catalog,
        CancellationToken cancellationToken)
        => catalog.GetProductsAsync(cancellationToken);

    /// <summary>One product by its identifier.</summary>
    public static Task<Product?> GetProductByIdAsync(
        [ID] Guid id,
        CatalogService catalog,
        CancellationToken cancellationToken)
        => catalog.GetProductByIdAsync(id, cancellationToken);

    /// <summary>One product by its stock keeping unit.</summary>
    public static Task<Product?> GetProductBySkuAsync(
        string sku,
        CatalogService catalog,
        CancellationToken cancellationToken)
        => catalog.GetProductBySkuAsync(sku, cancellationToken);
}
