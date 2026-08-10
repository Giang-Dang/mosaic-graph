using HotChocolate.Types.Relay;
using Mosaic.Nodes.Reviews.Model;

namespace Mosaic.Nodes.Reviews.Types;

/// <summary>
/// What makes <c>Review</c> a <c>Node</c> here. See
/// <c>Mosaic.Nodes.Catalog.Types.ProductNode</c> for the mechanism.
/// </summary>
[ObjectType<Review>]
public static partial class ReviewNode
{
    /// <summary>The review's global identifier.</summary>
    [ID]
    public static Guid GetId([Parent] Review review) => review.Id;

    /// <summary>Wraps a decoded identifier so the router can resolve the rest.</summary>
    [NodeResolver]
    public static Review ResolveReview(Guid id) => new() { Id = id };
}
