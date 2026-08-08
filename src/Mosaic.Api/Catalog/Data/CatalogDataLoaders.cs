using GreenDonut;
using Mosaic.Api.Catalog.Model;

namespace Mosaic.Api.Catalog.Data;

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
