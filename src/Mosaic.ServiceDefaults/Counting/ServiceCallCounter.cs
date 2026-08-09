using Microsoft.AspNetCore.Http;

namespace Mosaic.ServiceDefaults.Counting;

/// <summary>
/// Counts how many times the domain services were asked for something while
/// serving one GraphQL request.
/// </summary>
/// <remarks>
/// <para>
/// The running total lives on the <see cref="HttpContext"/> rather than in this
/// class, which is not the obvious design and is worth explaining. HotChocolate
/// resolves a resolver's injected services from a scope of its own, one per
/// resolver invocation. A counter registered as scoped therefore gets
/// constructed afresh for every resolver, sees exactly one lookup, and reports
/// one instead of the request's total. Anything that has to accumulate across a
/// whole request has to hang off the request itself.
/// </para>
/// <para>
/// This counts questions asked of a domain service, which is not the same thing
/// as work done by the database. Until chapter 4 the two were the same number
/// because every question was answered from a list in memory. They part company
/// the moment a service can answer several questions with one statement, which
/// is what a DataLoader arranges;
/// <see cref="Diagnostics.RequestTimeline.SqlCommandCount"/> is the other half
/// of the picture.
/// </para>
/// </remarks>
public sealed class ServiceCallCounter(IHttpContextAccessor httpContextAccessor)
{
    internal const string ItemKey = "Mosaic:ServiceLookups";

    public void RecordLookup()
    {
        if (httpContextAccessor.HttpContext is { } context
            && context.Items.TryGetValue(ItemKey, out var value)
            && value is RequestLookupCount count)
        {
            count.Increment();
        }
    }
}
