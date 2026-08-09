using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;

namespace Mosaic.Sample.EntityAttributePlacement;

// Four ways to write the same entity, one type each, in one schema so that the
// answers cannot be confused with each other. Chapter 8 prints the table this
// produces.
//
// [Key] works wherever you put it. [ReferenceResolver] works on the runtime
// type and nowhere else: put it on a method inside an [ObjectType<T>] class
// and the source generator turns the method into an ordinary public field, no
// reference resolver is registered, and nothing warns. Export the schema and
// look for the field that should not be there.

// ---------------------------------------------------------------------------
// Alpha - both attributes on the [ObjectType<T>] class. Broken.
// ---------------------------------------------------------------------------

public sealed record Alpha(string Id, string Title);

[Key("id")]
[ObjectType<Alpha>]
public static partial class AlphaNode
{
    [ReferenceResolver]
    public static Alpha? ResolveByKey(string id)
        => PlacementData.Alphas.FirstOrDefault(a => a.Id == id);
}

// ---------------------------------------------------------------------------
// Bravo - both attributes on the runtime type. Works. This is the shape the
// book uses from chapter 8 onwards.
// ---------------------------------------------------------------------------

[Key("id")]
public sealed record Bravo(string Id, string Title)
{
    [ReferenceResolver]
    public static Bravo? ResolveByKey(string id)
        => PlacementData.Bravos.FirstOrDefault(b => b.Id == id);
}

// ---------------------------------------------------------------------------
// Charlie - key on the runtime type, reference resolver on the class. Broken
// the same way Alpha is, which is what shows that the two attributes are
// independent rather than a pair.
// ---------------------------------------------------------------------------

[Key("id")]
public sealed record Charlie(string Id, string Title);

[ObjectType<Charlie>]
public static partial class CharlieNode
{
    [ReferenceResolver]
    public static Charlie? ResolveByKey(string id)
        => PlacementData.Charlies.FirstOrDefault(c => c.Id == id);
}

// ---------------------------------------------------------------------------
// Delta - key on the class, reference resolver on the runtime type. Works.
// ---------------------------------------------------------------------------

public sealed record Delta(string Id, string Title)
{
    [ReferenceResolver]
    public static Delta? ResolveByKey(string id)
        => PlacementData.Deltas.FirstOrDefault(d => d.Id == id);
}

[Key("id")]
[ObjectType<Delta>]
public static partial class DeltaNode;

public static class PlacementData
{
    public static readonly IReadOnlyList<Alpha> Alphas = [new("1", "alpha one")];
    public static readonly IReadOnlyList<Bravo> Bravos = [new("1", "bravo one")];
    public static readonly IReadOnlyList<Charlie> Charlies = [new("1", "charlie one")];
    public static readonly IReadOnlyList<Delta> Deltas = [new("1", "delta one")];
}

/// <summary>
/// Every entity needs a root field that reaches it, or HotChocolate never
/// builds the type at all and the schema comes out with no keys and no
/// <c>_entities</c> field. Chapter 7 spent a section on that trap.
/// </summary>
[QueryType]
public static partial class PlacementQueries
{
    public static IReadOnlyList<Alpha> GetAlphas() => PlacementData.Alphas;

    public static IReadOnlyList<Bravo> GetBravos() => PlacementData.Bravos;

    public static IReadOnlyList<Charlie> GetCharlies() => PlacementData.Charlies;

    public static IReadOnlyList<Delta> GetDeltas() => PlacementData.Deltas;
}
