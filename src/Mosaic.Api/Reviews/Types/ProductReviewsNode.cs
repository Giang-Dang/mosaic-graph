using GreenDonut.Data;
using HotChocolate.Types.Pagination;
using Mosaic.Api.Catalog.Model;
using Mosaic.Api.Reviews.Data;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Reviews.Types;

/// <summary>
/// Reviews' contribution to the shared <c>Product</c> type. Catalog owns the
/// type; this class hangs two more fields off it, from its own folder.
/// </summary>
/// <remarks>
/// <para>
/// <c>reviews</c> became a connection in chapter 5. It was a plain
/// <c>[Review!]!</c> for three chapters, which was always going to end: a field
/// that returns every row of a growing table has no upper bound on what it
/// costs to answer. Changing it was a breaking change and the chapter says so.
/// </para>
/// <para>
/// The signatures changed in chapter 4 and the schema did not. A resolver that
/// took a service now takes a DataLoader, and the field it produces is the same
/// field: same name, same type, same nullability. That is the property worth
/// noticing about DataLoaders. They are an implementation detail of a resolver,
/// invisible from the outside, which is why the fix does not need a schema
/// change and why it can be applied one field at a time.
/// </para>
/// <para>
/// <c>[Parent("Id")]</c> is new too, and it is the price of projection. When a
/// field is projected, HotChocolate builds the <c>SELECT</c> list from the
/// selection set, and nothing in <c>{ browseProducts { nodes { reviews { ... } } } }</c>
/// mentions a product's identifier. Without the declaration the projection
/// leaves <c>Id</c> out, the parent arrives with an empty Guid, and every
/// product looks unreviewed.
/// </para>
/// </remarks>
[ObjectType<Product>]
public static partial class ProductReviewsNode
{
    /// <summary>What customers have said about this product, newest page first.</summary>
    /// <remarks>
    /// <para>
    /// Three moving parts, and only one of them is the DataLoader.
    /// <c>[UseConnection]</c> puts <c>first</c>, <c>after</c>, <c>last</c> and
    /// <c>before</c> on the field and declares the connection type.
    /// <c>PagingArguments</c> is those four arguments coerced.
    /// <c>.With(pagingArguments)</c> hands them to the DataLoader as state,
    /// and returns a <em>branch</em> of it keyed on a hash of the arguments.
    /// </para>
    /// <para>
    /// The branch is what makes this safe. Twenty-five products asking for
    /// their first three reviews share one branch, so their keys land in one
    /// batch and one statement. A field elsewhere in the same request asking
    /// for a different page gets a different branch, its own cache and its own
    /// statement, because the two answers are not interchangeable and a shared
    /// promise cache would hand one of them the other's page.
    /// </para>
    /// <para>
    /// The return type is what makes this field a connection, and that is not
    /// obvious from the attribute. <c>[UseConnection]</c> only overrides paging
    /// options for a field that already is one; it is
    /// <c>PageConnection&lt;Review&gt;</c> that rewrites the schema. Put the
    /// attribute on a resolver that returns a list and the field stays a list,
    /// with no error anywhere.
    /// </para>
    /// <para>
    /// The null coalesce is not defensive programming. A batch DataLoader
    /// answers an unknown key with null, and the service fills every requested
    /// key with an empty page for exactly that reason, so this should be
    /// unreachable. It stays because <c>reviews</c> is non-nullable and the
    /// cost of being wrong is a failed request rather than an empty page.
    /// </para>
    /// </remarks>
    [UseConnection(IncludeTotalCount = true)]
    public static async Task<PageConnection<Review>> GetReviewsAsync(
        [Parent("Id")] Product product,
        IReviewsByProductIdDataLoader reviewsByProductId,
        PagingArguments pagingArguments,
        CancellationToken cancellationToken)
    {
        var page = await reviewsByProductId
            .With(pagingArguments)
            .LoadAsync(product.Id, cancellationToken);

        return page ?? Page<Review>.Empty;
    }

    /// <summary>
    /// The mean of this product's ratings, or <c>null</c> if nobody has rated it.
    /// </summary>
    public static Task<double?> GetAverageRatingAsync(
        [Parent("Id")] Product product,
        IAverageRatingByProductIdDataLoader averageRatingByProductId,
        CancellationToken cancellationToken)
        => averageRatingByProductId.LoadAsync(product.Id, cancellationToken);
}
