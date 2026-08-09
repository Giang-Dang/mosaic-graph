using GreenDonut.Data;
using Microsoft.EntityFrameworkCore;
using Mosaic.Catalog.Model;

namespace Mosaic.Catalog.Data;

/// <summary>
/// Everything the rest of the world is allowed to ask the Catalog domain.
/// </summary>
/// <remarks>
/// <para>
/// This class arrived in chapter 8 by being moved, not rewritten. Two things
/// changed on the way out of Mosaic and both are visible in the constructor:
/// the context is Catalog's own rather than the shared one, and the
/// <c>ServiceCallCounter</c> is gone. That counter was the monolith's
/// instrumentation, added in chapter 3 to count what one process was doing to
/// itself, and a service that answers one domain's questions has nothing to
/// count with it. Chapter 23 gives both services something better.
/// </para>
/// <para>
/// Everything is read <c>AsNoTracking</c>. Nothing here writes, and the change
/// tracker is a per-context identity map that costs memory and a snapshot per
/// row for a benefit a read-only request never collects.
/// </para>
/// <para>
/// The ordering is by identifier rather than by title. The seed data numbers
/// products from one upwards inside a fixed identifier pattern, so ordering by
/// the key reproduces the order the earlier chapters printed.
/// </para>
/// </remarks>
public sealed class CatalogService(CatalogDbContext db)
{
    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        CancellationToken cancellationToken)
        => await db.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

    public async Task<Product?> GetProductByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
        => await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Product?> GetProductBySkuAsync(
        string sku,
        CancellationToken cancellationToken)
        => await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);

    /// <summary>
    /// One page of the catalog, filtered and sorted as the caller asked, with
    /// only the columns the caller's selection set actually needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three arguments the resolver did not have to build. <c>PagingArguments</c>
    /// carries <c>first</c>/<c>after</c>/<c>last</c>/<c>before</c>;
    /// <c>QueryContext&lt;Product&gt;</c> carries a predicate built from the
    /// <c>where</c> argument, a sort definition built from <c>order</c>, and a
    /// selector built from the selection set. <c>With</c> applies all three in
    /// the order that keeps the query cheap: filter, then sort, then project.
    /// </para>
    /// <para>
    /// The default order is not decoration. Keyset pagination needs a total
    /// order or the cursor cannot say where it points, so the tiebreaker on the
    /// identifier is appended whether or not the caller sorted. <c>IfEmpty</c>
    /// supplies the title ordering only when the caller did not ask for one.
    /// </para>
    /// </remarks>
    public async Task<Page<Product>> BrowseProductsAsync(
        PagingArguments pagingArguments,
        QueryContext<Product>? query,
        CancellationToken cancellationToken)
        => await db.Products
            .AsNoTracking()
            .With(query, DefaultOrder)
            .ToPageAsync(pagingArguments, cancellationToken);

    private static SortDefinition<Product> DefaultOrder(SortDefinition<Product> sort)
        => sort.IfEmpty(order => order.AddAscending(p => p.Title))
            .AddAscending(p => p.Id);

    /// <summary>Several products by their identifiers, keyed for the caller.</summary>
    /// <remarks>
    /// The batch method the DataLoader sits on, and since chapter 8 also the
    /// method behind <c>_entities</c>. Every representation the router sends is
    /// one key into this dictionary, which is why a reference resolver behind a
    /// DataLoader costs one statement for a batch of any size.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, Product>> GetProductsByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
        => await db.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
}
