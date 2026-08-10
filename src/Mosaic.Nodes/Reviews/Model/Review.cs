using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Nodes.Reviews.Model;

/// <summary>
/// A review, as the node service knows one: an identifier, and nothing else.
/// </summary>
/// <remarks>
/// One of the two types this chapter had to change on the other side before
/// this stub could mean anything. Until chapter 13 <c>Review</c> had no
/// <c>@key</c> at all, so a stub here would have named a type the router has no
/// way of fetching, and every field asked of it would have come back null.
/// </remarks>
[Key("id", resolvable: false)]
public sealed class Review
{
    /// <summary>The review's global identifier, and the federation key.</summary>
    [ID]
    public required Guid Id { get; init; }
}
