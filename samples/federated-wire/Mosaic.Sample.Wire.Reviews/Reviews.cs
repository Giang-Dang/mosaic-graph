using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Sample.Wire.Reviews;

/// <summary>
/// A review. Owned by this subgraph outright: no other service defines it, so
/// it carries no key and the router can never ask for one by identity.
/// </summary>
public sealed class Review
{
    [ID]
    public required string Id { get; init; }

    public required int Rating { get; init; }

    public required string Body { get; init; }
}

/// <summary>
/// The same <c>Product</c> the Catalog subgraph owns, declared again here with
/// the same key and one field. This subgraph knows nothing about titles or
/// prices and never will; what it knows is which reviews belong to which
/// product identifier.
/// </summary>
/// <remarks>
/// No root field returns this type, so nothing pulls it into the schema.
/// Program.cs registers it with AddType&lt;Product&gt;(); without that line the
/// subgraph starts, answers, and publishes a schema with no Product in it and
/// no _entities field at all.
/// </remarks>
[Key("id")]
public sealed class Product
{
    [ID]
    public required string Id { get; init; }

    public IReadOnlyList<Review> Reviews
        => ReviewsData.ByProduct.TryGetValue(Id, out var reviews) ? reviews : [];

    /// <summary>
    /// The reference resolver, and the reason this subgraph can contribute to
    /// a type it does not own. It is handed a key and returns an object; it
    /// does not check that the product exists, because Catalog is the
    /// authority on that and this subgraph has no way to ask.
    /// </summary>
    [ReferenceResolver]
    public static Product ResolveById(string id)
        => new() { Id = id };
}

public static class ReviewsData
{
    /// <summary>
    /// Product 3 is missing on purpose: a product nobody has reviewed is the
    /// interesting row on the wire, because the router still asks about it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<Review>> ByProduct =
        new Dictionary<string, IReadOnlyList<Review>>
        {
            ["1"] =
            [
                new() { Id = "r1", Rating = 5, Body = "Survived a house move." },
                new() { Id = "r2", Rating = 3, Body = "Arrived with a chipped leg." }
            ],
            ["2"] =
            [
                new() { Id = "r3", Rating = 4, Body = "Firmer than it looks." }
            ]
        };
}

[QueryType]
public static partial class ReviewQueries
{
    /// <summary>
    /// Every schema needs a query root, and the router never calls this one:
    /// it reaches reviews through <c>Product</c>. It is here so the subgraph
    /// answers something on its own, which is how the chapter tests it before
    /// a router exists.
    /// </summary>
    public static IReadOnlyList<Review> GetReviews()
        => [.. ReviewsData.ByProduct.Values.SelectMany(r => r)];
}
