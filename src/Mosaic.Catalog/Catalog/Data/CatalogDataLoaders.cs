using GreenDonut;
using Mosaic.Catalog.Model;

namespace Mosaic.Catalog.Data;

/// <summary>
/// Catalog's DataLoaders.
/// </summary>
public static class CatalogDataLoaders
{
    /// <summary>Products by identifier, in one batch.</summary>
    [DataLoader]
    public static Task<IReadOnlyDictionary<Guid, Product>> GetProductByIdAsync(
        IReadOnlyList<Guid> ids,
        CatalogService catalog,
        CancellationToken cancellationToken)
        => catalog.GetProductsByIdsAsync(ids, cancellationToken);
}
