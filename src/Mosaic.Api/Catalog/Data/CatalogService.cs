using Microsoft.EntityFrameworkCore;
using Mosaic.Api.Catalog.Model;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Infrastructure.Data;

namespace Mosaic.Api.Catalog.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Catalog domain.
/// </summary>
/// <remarks>
/// <para>
/// Every method takes one key and answers about one thing. There is no
/// overload here that accepts a list of identifiers, which is deliberate and
/// is what makes this version of Mosaic behave the way it does under a nested
/// query. Chapter 4 leaves that shape alone and changes only what sits behind
/// it: the same methods, over PostgreSQL instead of a list in memory.
/// </para>
/// <para>
/// Everything is read <c>AsNoTracking</c>. Nothing in Mosaic writes yet, and
/// the change tracker is a per-context identity map that costs memory and a
/// snapshot per row for a benefit a read-only request never collects.
/// </para>
/// <para>
/// The ordering is by identifier rather than by title. The seed data numbers
/// products from one upwards inside a fixed identifier pattern, so ordering by
/// the key reproduces the order the earlier chapters printed.
/// </para>
/// </remarks>
public sealed class CatalogService(MosaicDbContext db, ServiceCallCounter counter)
{
    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetProductByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Product?> GetProductBySkuAsync(
        string sku,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);
    }
}
