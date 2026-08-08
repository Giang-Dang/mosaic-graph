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
/// Both fields are plain lists and plain numbers. There is no connection type
/// and no page size here, which is a decision rather than an oversight: a
/// later chapter turns <c>reviews</c> into a Relay connection and needs this
/// version to compare against.
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
    /// <summary>Everything customers have said about this product.</summary>
    public static async Task<IReadOnlyList<Review>> GetReviewsAsync(
        [Parent("Id")] Product product,
        IReviewsByProductIdDataLoader reviewsByProductId,
        CancellationToken cancellationToken)
        => await reviewsByProductId.LoadAsync(product.Id, cancellationToken) ?? [];

    /// <summary>
    /// The mean of this product's ratings, or <c>null</c> if nobody has rated it.
    /// </summary>
    public static Task<double?> GetAverageRatingAsync(
        [Parent("Id")] Product product,
        IAverageRatingByProductIdDataLoader averageRatingByProductId,
        CancellationToken cancellationToken)
        => averageRatingByProductId.LoadAsync(product.Id, cancellationToken);
}
