using System.Collections.Concurrent;
using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Sample.InterfaceObject.Ratings;

/// <summary>
/// A field contributed to every implementation of an interface this subgraph
/// cannot name.
/// </summary>
/// <remarks>
/// <para>
/// <c>Media</c> is an interface in the other subgraph and an ordinary object
/// here, and <c>@interfaceObject</c> is the declaration that makes the two the
/// same type to the composer. What that buys is the whole point: this project
/// contains no <c>Book</c> and no <c>Film</c>, has never heard of either, and
/// nevertheless adds <c>averageRating</c> to both. Add a third implementation
/// to the library tomorrow and it gets the field with no change here.
/// </para>
/// <para>
/// The price is on the wire. The router sends representations whose
/// <c>__typename</c> is <c>Media</c>, a name no subgraph has a concrete type
/// for except this one, so this reference resolver is asked about a book
/// without being told it is a book. <see cref="ResolutionLog"/> records every
/// key it is handed, which is how the chapter prints what arrived instead of
/// describing it.
/// </para>
/// </remarks>
[Key("id")]
[InterfaceObject]
public sealed class Media
{
    [ID]
    public required string Id { get; init; }

    /// <summary>What everybody thought of it, or null if nobody has said.</summary>
    public double? AverageRating
        => RatingStore.Scores.TryGetValue(Id, out var scores) && scores.Count > 0
            ? scores.Average()
            : null;

    /// <summary>How many people said it.</summary>
    public int RatingCount
        => RatingStore.Scores.TryGetValue(Id, out var scores) ? scores.Count : 0;

    [ReferenceResolver]
    public static Media ResolveById(string id)
    {
        ResolutionLog.Write($"_entities  Media/{id}");
        return new Media { Id = id };
    }
}

/// <summary>Ratings for three of the library's four items.</summary>
/// <remarks>
/// <c>f2</c> is deliberately absent. A subgraph contributing a field to an
/// interface has no way to know which implementations exist, so "I have no row
/// for this key" has to be an ordinary answer rather than an error, and the
/// field is nullable for that reason.
/// </remarks>
public static class RatingStore
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<int>> Scores =
        new Dictionary<string, IReadOnlyList<int>>
        {
            ["b1"] = [5, 4, 5],
            ["b2"] = [5, 5],
            ["f1"] = [4, 5, 5, 4]
        };
}

/// <summary>
/// Every reference-resolver call this process has served, in order.
/// </summary>
/// <remarks>
/// The same device chapter 11's sample used, and for the same reason: an
/// assertion about what the router asked for is worth more than a sentence
/// about it. Static and process-wide, which is fine for a sample nobody runs
/// concurrently and would be wrong anywhere else.
/// </remarks>
public static class ResolutionLog
{
    private static readonly ConcurrentQueue<string> s_entries = new();

    public static void Write(string entry) => s_entries.Enqueue(entry);

    public static IReadOnlyList<string> Entries => [.. s_entries];

    public static void Clear() => s_entries.Clear();
}

[QueryType]
public static class RatingsQueries
{
    /// <summary>What the router has asked this subgraph for, in order.</summary>
    /// <remarks>
    /// A root field on a subgraph that otherwise has none, which changes what
    /// composes: the field is reachable through the router as well as
    /// directly, and that is deliberate here so the lab can read the log
    /// without leaving port 3004.
    /// </remarks>
    public static IReadOnlyList<string> GetResolutionLog() => ResolutionLog.Entries;

    /// <summary>Empties the log, so a run can start from a known state.</summary>
    public static bool ClearResolutionLog()
    {
        ResolutionLog.Clear();
        return true;
    }
}
