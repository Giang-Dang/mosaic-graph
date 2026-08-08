using Microsoft.Extensions.Options;

namespace Mosaic.Api.Infrastructure;

/// <summary>
/// Counts how many times the domain services were asked for something while
/// serving one GraphQL request, and pays whatever a lookup currently costs.
/// </summary>
/// <remarks>
/// The running total lives on the <see cref="HttpContext"/> rather than in this
/// class, which is not the obvious design and is worth explaining. HotChocolate
/// resolves a resolver's injected services from a scope of its own, one per
/// resolver invocation. A counter registered as scoped therefore gets
/// constructed afresh for every resolver, sees exactly one lookup, and reports
/// one instead of the request's total. Anything that has to accumulate across a
/// whole request has to hang off the request itself.
/// </remarks>
public sealed class ServiceCallCounter(
    IHttpContextAccessor httpContextAccessor,
    IOptions<MosaicDataOptions> options)
{
    internal const string ItemKey = "Mosaic:ServiceLookups";

    public async ValueTask RecordLookupAsync(CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is { } context
            && context.Items.TryGetValue(ItemKey, out var value)
            && value is RequestLookupCount count)
        {
            count.Increment();
        }

        var delay = options.Value.LookupDelay;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }
}
