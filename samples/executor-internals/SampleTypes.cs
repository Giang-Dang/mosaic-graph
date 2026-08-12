using HotChocolate.Types;

namespace Mosaic.Sample.ExecutorInternals;

/// <summary>
/// The smallest type that can tell a pure field from one that costs a task.
/// </summary>
/// <remarks>
/// <c>Id</c>, <c>Name</c> and <c>Upper</c> are read or computed without
/// awaiting anything, so the source generator classifies them
/// <c>ResolverResultKind.Pure</c> and the operation compiler gives each a
/// <c>PureFieldDelegate</c>. <c>Delayed</c> returns a <see cref="Task{T}"/>,
/// which is the whole of the difference: the classification is made on the
/// declared return type, not by looking for an <c>await</c> in the body.
/// </remarks>
public sealed record Thing(int Id, string Name)
{
    public string Upper() => Name.ToUpperInvariant();

    public async Task<string> DelayedAsync()
    {
        await Task.Yield();
        return $"{Name}!";
    }
}

[QueryType]
public static partial class SampleQueries
{
    private static readonly Thing[] s_things =
    [
        new(1, "alpha"),
        new(2, "beta"),
        new(3, "gamma")
    ];

    /// <summary>
    /// One asynchronous root field returning three things. Asynchronous on
    /// purpose: it is the one resolver task every case can count on.
    /// </summary>
    public static async Task<IReadOnlyList<Thing>> GetThingsAsync()
    {
        await Task.Yield();
        return s_things;
    }

    /// <summary>
    /// A second root field, so that the single-flight case can build documents
    /// that differ from each other and from every other case's.
    /// </summary>
    public static async Task<int> GetCountAsync()
    {
        await Task.Yield();
        return s_things.Length;
    }
}
