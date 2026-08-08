namespace Mosaic.Api.Infrastructure;

/// <summary>
/// A single request's running total of domain-service lookups.
/// </summary>
/// <remarks>
/// Resolvers run concurrently, so the increment has to be atomic.
/// </remarks>
public sealed class RequestLookupCount
{
    private int _value;

    public int Value => Volatile.Read(ref _value);

    public void Increment() => Interlocked.Increment(ref _value);
}
