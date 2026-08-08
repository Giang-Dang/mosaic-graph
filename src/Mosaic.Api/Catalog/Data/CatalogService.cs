using Mosaic.Api.Catalog.Model;
using Mosaic.Api.Infrastructure;

namespace Mosaic.Api.Catalog.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Catalog domain.
/// </summary>
/// <remarks>
/// Every method takes one key and answers about one thing. There is no
/// overload here that accepts a list of identifiers, which is deliberate and
/// is what makes this version of Mosaic behave the way it does under a nested
/// query.
/// </remarks>
public sealed class CatalogService(InMemoryCatalogData data, ServiceCallCounter counter)
{
    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken)
    {
        await counter.RecordLookupAsync(cancellationToken);
        return data.Products;
    }

    public async Task<Product?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await counter.RecordLookupAsync(cancellationToken);
        return data.Products.FirstOrDefault(p => p.Id == id);
    }

    public async Task<Product?> GetProductBySkuAsync(string sku, CancellationToken cancellationToken)
    {
        await counter.RecordLookupAsync(cancellationToken);
        return data.Products.FirstOrDefault(p => p.Sku == sku);
    }
}
