using GreenDonut.Data;
using HotChocolate.Data;
using HotChocolate.Types.Pagination;
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
    /// <remarks>
    /// <para>
    /// Twenty-five rows, unpaged and unfiltered, and left exactly as chapters 2
    /// and 3 found it. Every number those chapters print is a measurement of
    /// this field, so paginating it would quietly retire two chapters of
    /// evidence. <see cref="GetBrowseProductsAsync"/> is where the data
    /// middleware lives instead.
    /// </para>
    /// <para>
    /// Deprecated in chapter 5, which is the whole of what deprecation costs:
    /// one attribute, and the field goes on working exactly as before.
    /// <c>@deprecated</c> is a message to the humans and the tooling reading
    /// the schema, not a switch that turns anything off. The field is still
    /// here, still answers, and still has every number chapters 2 to 4 measured
    /// riding on it. What has changed is that a client generating code from
    /// this schema now gets a warning, and that the day it is removed is a day
    /// the schema registry can see coming.
    /// </para>
    /// <para>
    /// The reason string names the replacement, and that is the part worth
    /// copying. A deprecation that says "use something else" without saying
    /// what leaves every consumer to work it out separately.
    /// </para>
    /// </remarks>
    [GraphQLDeprecated(
        "Returns the whole catalog with no upper bound on its size. "
        + "Use `browseProducts`, which pages, filters and sorts.")]
    public static Task<IReadOnlyList<Product>> GetProductsAsync(
        CatalogService catalog,
        CancellationToken cancellationToken)
        => catalog.GetProductsAsync(cancellationToken);

    /// <summary>
    /// The catalog as a storefront would page through it: a cursor-based
    /// connection, with optional filtering and sorting.
    /// </summary>
    /// <remarks>
    /// <c>[UseFiltering]</c> and <c>[UseSorting]</c> add the <c>where</c> and
    /// <c>order</c> arguments to the field and do nothing else; neither touches
    /// the query. What reads them is the <c>QueryContext&lt;Product&gt;</c>
    /// parameter, which HotChocolate builds per request from those arguments
    /// and from the selection set. Returning a <c>Page&lt;Product&gt;</c> is
    /// enough to get a connection back: <c>PageConnection</c> declares an
    /// implicit conversion from it.
    /// <para>
    /// <c>IncludeTotalCount</c> is set because the connection type carries a
    /// non-nullable <c>totalCount</c> field whether or not anyone asked for
    /// one. Leave it off and the field is still in the schema, and every query
    /// that selects it fails with HC0018: the page has no count to give.
    /// Turning it on costs less than it looks. The count arrives as a
    /// correlated subquery inside the page's own statement rather than as a
    /// second round trip, and only when a client actually selects the field.
    /// </para>
    /// </remarks>
    [UseConnection(IncludeTotalCount = true)]
    [UseFiltering]
    [UseSorting]
    public static async Task<PageConnection<Product>> GetBrowseProductsAsync(
        PagingArguments pagingArguments,
        QueryContext<Product>? query,
        CatalogService catalog,
        CancellationToken cancellationToken)
        => await catalog.BrowseProductsAsync(pagingArguments, query, cancellationToken);

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
