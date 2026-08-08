using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Mosaic.Sample.ResolverScopes;

/// <summary>
/// A scoped service that can say which service scope constructed it.
/// </summary>
public sealed class ScopeProbe
{
    private static int _counter;

    public int Id { get; } = Interlocked.Increment(ref _counter);
}

/// <summary>
/// What one field saw: the probe handed to it as a parameter, and the probe
/// living in the request's own service provider.
/// </summary>
public sealed record ScopeReading(int Injected, int FromRequestServices);

[QueryType]
public static partial class ScopeQueries
{
    /// <summary>
    /// A field with no scope annotation. Queries default to a service scope per
    /// resolver, so <c>injected</c> differs from <c>fromRequestServices</c> and
    /// differs again on every field that asks.
    /// </summary>
    public static ScopeReading GetDefaultScope(
        ScopeProbe injected,
        IResolverContext context)
        => new(
            injected.Id,
            context.RequestServices.GetRequiredService<ScopeProbe>().Id);

    /// <summary>
    /// The same field with <c>[UseRequestScope]</c>. The injected probe now
    /// comes from the request scope, so both numbers agree, and they agree
    /// across every field annotated this way.
    /// </summary>
    [UseRequestScope]
    public static ScopeReading GetRequestScope(
        ScopeProbe injected,
        IResolverContext context)
        => new(
            injected.Id,
            context.RequestServices.GetRequiredService<ScopeProbe>().Id);
}
