using System.Collections.Concurrent;
using GreenDonut;
using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Sample.Entities.Catalog;

/// <summary>
/// An entity whose reference resolver is behind a DataLoader.
/// </summary>
/// <remarks>
/// Chapter 11's subject in one class. The router hands
/// <c>Query._entities</c> a list of representations; HotChocolate calls this
/// resolver once for each of them; the DataLoader collects the keys and asks
/// the store once. Every step of that is written to <see cref="ResolutionLog"/>
/// so the chapter can print the order rather than assert it.
/// </remarks>
[Key("id")]
public sealed class Widget
{
    [ID]
    public required string Id { get; init; }

    public required string Name { get; init; }

    [ReferenceResolver]
    public static async Task<Widget?> ResolveByIdAsync(
        string id,
        IWidgetByIdDataLoader widgetById,
        CancellationToken cancellationToken)
    {
        ResolutionLog.Write($"enter  Widget/{id}");
        var widget = await widgetById.LoadAsync(id, cancellationToken);
        ResolutionLog.Write($"leave  Widget/{id}");
        return widget;
    }
}

/// <summary>
/// The same entity with the naive resolver: one lookup per representation.
/// </summary>
/// <remarks>
/// The control. It is deliberately what a subgraph looks like before anybody
/// thinks about the batch, and it is here so the chapter can print the
/// difference rather than describe it. Nothing about the federation wiring
/// differs from <see cref="Widget"/>; the router cannot tell the two apart and
/// the answers are identical.
/// </remarks>
[Key("id")]
public sealed class Gadget
{
    [ID]
    public required string Id { get; init; }

    public required string Name { get; init; }

    [ReferenceResolver]
    public static async Task<Gadget?> ResolveByIdAsync(
        string id,
        WidgetStore store,
        CancellationToken cancellationToken)
    {
        ResolutionLog.Write($"enter  Gadget/{id}");
        var found = await store.LoadOneAsync(id, cancellationToken);
        ResolutionLog.Write($"leave  Gadget/{id}");
        return found is null ? null : new Gadget { Id = found.Id, Name = found.Name };
    }
}

/// <summary>
/// The four widgets this sample knows about, and a counter for how often
/// somebody went and got them.
/// </summary>
/// <remarks>
/// A stand-in for a database. What matters is that every route to the data
/// goes through one of the two methods below, so the store can count the
/// journeys and record how many keys travelled on each one.
/// </remarks>
public sealed class WidgetStore
{
    private static readonly IReadOnlyDictionary<string, Widget> s_rows =
        new Dictionary<string, Widget>
        {
            ["w1"] = new() { Id = "w1", Name = "Hex bolt" },
            ["w2"] = new() { Id = "w2", Name = "Wing nut" },
            ["w3"] = new() { Id = "w3", Name = "Split washer" },
            ["w4"] = new() { Id = "w4", Name = "Coach screw" }
        };

    public static IReadOnlyList<Widget> All => [.. s_rows.Values];

    /// <summary>One journey for one key.</summary>
    public Task<Widget?> LoadOneAsync(string id, CancellationToken cancellationToken)
    {
        ResolutionLog.Write($"lookup 1 key: {id}");
        return Task.FromResult(s_rows.GetValueOrDefault(id));
    }

    /// <summary>One journey for however many keys arrived together.</summary>
    public Task<IReadOnlyDictionary<string, Widget>> LoadManyAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        ResolutionLog.Write(
            $"lookup {ids.Count} key{(ids.Count == 1 ? "" : "s")}: {string.Join(", ", ids)}");

        return Task.FromResult<IReadOnlyDictionary<string, Widget>>(
            ids.Where(s_rows.ContainsKey).Distinct().ToDictionary(id => id, id => s_rows[id]));
    }
}

public static class WidgetDataLoaders
{
    [DataLoader]
    public static Task<IReadOnlyDictionary<string, Widget>> GetWidgetByIdAsync(
        IReadOnlyList<string> ids,
        WidgetStore store,
        CancellationToken cancellationToken)
        => store.LoadManyAsync(ids, cancellationToken);
}

/// <summary>
/// Every line the two reference resolvers and the store wrote, in the order
/// they wrote it.
/// </summary>
/// <remarks>
/// <para>
/// A static list rather than a request-scoped one, because the point of the
/// log is to be readable from a second request after the first has finished.
/// It makes this subgraph useless for anything but the exercise it exists for,
/// which is the definition of a sample.
/// </para>
/// <para>
/// The thread each line was written on is recorded and then thrown away by
/// <see cref="Read"/>, which keeps one derived fact instead: whether every
/// <c>enter</c> happened on the thread that entered the field. That is the
/// claim chapter 11 makes, and a raw thread number would be a different number
/// on every run.
/// </para>
/// </remarks>
public static class ResolutionLog
{
    private static readonly ConcurrentQueue<(string Line, int Thread)> s_lines = new();

    public static void Write(string line)
        => s_lines.Enqueue((line, Environment.CurrentManagedThreadId));

    public static void Clear()
        => s_lines.Clear();

    public static IReadOnlyList<string> Read()
    {
        var entries = s_lines.ToArray();
        var enters = entries.Where(e => e.Line.StartsWith("enter", StringComparison.Ordinal)).ToArray();
        var threads = enters.Select(e => e.Thread).Distinct().Count();

        return
        [
            .. entries.Select(e => e.Line),
            enters.Length == 0
                ? "entries: none"
                : $"entries: {enters.Length} on {threads} thread(s)"
        ];
    }
}

[QueryType]
public static partial class WidgetQueries
{
    /// <summary>Every widget, which is how a client gets a key to send back.</summary>
    public static IReadOnlyList<Widget> GetWidgets()
        => WidgetStore.All;

    /// <summary>
    /// What the resolvers did, most recent run last. Pass <c>clear: true</c>
    /// before the run you want to read, not after it.
    /// </summary>
    public static IReadOnlyList<string> GetResolutionLog(bool clear = false)
    {
        if (clear)
        {
            ResolutionLog.Clear();
            return [];
        }

        return ResolutionLog.Read();
    }
}
