using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Sample.Entities.Crates;

/// <summary>
/// A crate holds one widget, and remembers what that widget was called on the
/// day it was packed.
/// </summary>
/// <remarks>
/// The two widget fields return the same object and differ only in what this
/// subgraph promises the router about it. <c>widget</c> carries
/// <c>@provides(fields: "name")</c>; <c>widgetByKey</c> carries nothing. Ask
/// for the name through the first and the router believes this subgraph and
/// stops here; ask through the second and it goes to Catalog for it.
/// </remarks>
public sealed class Crate
{
    [ID]
    public required string Id { get; init; }

    public required string Label { get; init; }

    [GraphQLIgnore]
    public required string WidgetId { get; init; }

    /// <summary>What the packing slip says the widget is called.</summary>
    [GraphQLIgnore]
    public required string PackedAs { get; init; }

    [Provides("name")]
    public Widget GetWidget() => new() { Id = WidgetId, Name = PackedAs };

    /// <summary>The same widget, with no promise attached.</summary>
    public Widget GetWidgetByKey() => new() { Id = WidgetId, Name = PackedAs };
}

/// <summary>
/// Catalog's widget, declared here for the second time so that this subgraph
/// can name the field it claims to be able to provide.
/// </summary>
/// <remarks>
/// <para>
/// <c>Name</c> is <c>@external</c>: this subgraph publishes the field, says it
/// does not own it, and then hands back a value for it anyway on one path.
/// That is exactly what <c>@provides</c> means, and it is only honest when the
/// two subgraphs agree. Widget <c>w2</c> in this sample is where they do not,
/// on purpose.
/// </para>
/// <para>
/// The key says <c>resolvable: false</c>, and no reference resolver is here to
/// contradict it. Nothing in this subgraph can turn a widget key into a
/// widget; the only widgets it can produce are the ones a crate is already
/// holding. The declaration exists so that <c>@provides</c> has an entity to
/// point at, which is what the specification asks of it, and the flag is what
/// stops the router from ever routing a widget key here.
/// </para>
/// </remarks>
[Key("id", resolvable: false)]
public sealed class Widget
{
    [ID]
    public required string Id { get; init; }

    [External]
    public required string Name { get; init; }
}

public static class CrateData
{
    /// <summary>
    /// Four crates, and three ways for the second hop to be interesting.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>c2 holds a widget whose packed name is out of date, so the two
    /// subgraphs disagree about it.</item>
    /// <item>c4 holds a widget key Catalog has never heard of, which is what a
    /// dangling foreign key looks like once it is a service boundary.</item>
    /// <item>the rest agree, so the disagreement stands out.</item>
    /// </list>
    /// </remarks>
    public static readonly IReadOnlyList<Crate> Crates =
    [
        new() { Id = "c1", Label = "Crate one", WidgetId = "w1", PackedAs = "Hex bolt" },
        new() { Id = "c2", Label = "Crate two", WidgetId = "w2", PackedAs = "Butterfly nut" },
        new() { Id = "c3", Label = "Crate three", WidgetId = "w3", PackedAs = "Split washer" },
        new() { Id = "c4", Label = "Crate four", WidgetId = "w9", PackedAs = "Unknown part" }
    ];
}

[QueryType]
public static partial class CrateQueries
{
    public static IReadOnlyList<Crate> GetCrates() => CrateData.Crates;
}
