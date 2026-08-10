using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Sample.InterfaceObject.Library;

/// <summary>
/// The interface that owns the implementations, and the reason this sample
/// exists.
/// </summary>
/// <remarks>
/// <para>
/// <c>[Key("id")]</c> on an <em>interface</em> is what makes this an entity
/// interface rather than an ordinary abstract type, and it is the thing the
/// other subgraph's <c>@interfaceObject</c> depends on. Without it the composer
/// still composes and the graph does not work; with one implementation missing
/// its own key, the composer does not produce an error at all. Both are
/// asserted in <c>scripts/modeling-cases.mjs</c>.
/// </para>
/// <para>
/// Chapter 6 described this directive from Apollo's documentation. This is the
/// smallest arrangement in which it can be watched.
/// </para>
/// </remarks>
[Key("id")]
[GraphQLName("Media")]
public interface IMedia
{
    [ID]
    string Id { get; }

    string Title { get; }
}

/// <summary>A book. An entity, keyed the same way the interface is.</summary>
[Key("id")]
public sealed class Book : IMedia
{
    [ID]
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required int Pages { get; init; }

    [ReferenceResolver]
    public static Book? ResolveById(string id)
        => MediaStore.Books.TryGetValue(id, out var book) ? book : null;
}

/// <summary>A film. The other implementation, and the one the ratings subgraph
/// has never heard of.</summary>
[Key("id")]
public sealed class Film : IMedia
{
    [ID]
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required int RuntimeMinutes { get; init; }

    [ReferenceResolver]
    public static Film? ResolveById(string id)
        => MediaStore.Films.TryGetValue(id, out var film) ? film : null;
}

/// <summary>Two books and two films, in memory.</summary>
public static class MediaStore
{
    public static readonly IReadOnlyDictionary<string, Book> Books =
        new Dictionary<string, Book>
        {
            ["b1"] = new() { Id = "b1", Title = "The Mezzanine", Pages = 135 },
            ["b2"] = new() { Id = "b2", Title = "Pale Fire", Pages = 315 }
        };

    public static readonly IReadOnlyDictionary<string, Film> Films =
        new Dictionary<string, Film>
        {
            ["f1"] = new() { Id = "f1", Title = "La Jetee", RuntimeMinutes = 28 },
            ["f2"] = new() { Id = "f2", Title = "Sans Soleil", RuntimeMinutes = 100 }
        };
}

[QueryType]
public static class LibraryQueries
{
    /// <summary>Everything in the library, books and films together.</summary>
    public static IEnumerable<IMedia> GetLibrary()
        => MediaStore.Books.Values.Cast<IMedia>().Concat(MediaStore.Films.Values);
}
