using System.Diagnostics;
using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Resolvers;

namespace Mosaic.Api.Infrastructure.Diagnostics;

/// <summary>
/// Reports what the execution pipeline did with each request.
/// </summary>
/// <remarks>
/// <para>
/// Every phase event on <see cref="ExecutionDiagnosticEventListener"/> returns
/// an <see cref="IDisposable"/>; the engine opens the scope before the phase and
/// disposes it after, so timing a phase means timing the scope. Events that
/// report a fact rather than bracket a span - the two cache hits - return
/// nothing.
/// </para>
/// <para>
/// A listener is registered in the <em>schema</em> service provider, not the
/// application one, so the <see cref="ILoggerFactory"/> below only resolves
/// because Program.cs also calls <c>AddApplicationService&lt;ILoggerFactory&gt;()</c>.
/// Without that line the schema fails to build at startup.
/// </para>
/// </remarks>
public sealed class RequestTimelineListener(ILoggerFactory loggerFactory)
    : ExecutionDiagnosticEventListener
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("Mosaic.RequestTimeline");

    /// <summary>
    /// Per-field events are off unless a listener opts in, because they fire
    /// once per resolver rather than once per request.
    /// </summary>
    public override bool EnableResolveFieldValue => true;

    public override IDisposable ExecuteRequest(RequestContext context)
    {
        if (Timeline(context) is not { } timeline)
        {
            return EmptyScope;
        }

        return new PhaseScope(elapsed =>
        {
            timeline.Total = elapsed;
            Report(timeline);
        });
    }

    public override IDisposable ParseDocument(RequestContext context)
        => Measure(context, static (timeline, elapsed) => timeline.Parse = elapsed);

    public override IDisposable ValidateDocument(RequestContext context)
        => Measure(context, static (timeline, elapsed) => timeline.Validate = elapsed);

    public override IDisposable CompileOperation(RequestContext context)
        => Measure(context, static (timeline, elapsed) => timeline.Compile = elapsed);

    public override IDisposable CoerceVariables(RequestContext context)
        => Measure(context, static (timeline, elapsed) => timeline.CoerceVariables = elapsed);

    public override IDisposable ExecuteOperation(RequestContext context)
        => Measure(context, static (timeline, elapsed) => timeline.Execute = elapsed);

    public override void RetrievedDocumentFromCache(RequestContext context)
    {
        if (Timeline(context) is { } timeline)
        {
            timeline.DocumentCacheHit = true;
        }
    }

    public override void RetrievedOperationFromCache(RequestContext context)
    {
        if (Timeline(context) is { } timeline)
        {
            timeline.OperationCacheHit = true;
        }
    }

    public override IDisposable ResolveFieldValue(IMiddlewareContext context)
    {
        context.RequestServices.GetService<RequestTimeline>()?.CountResolver();
        return EmptyScope;
    }

    private static RequestTimeline? Timeline(RequestContext context)
        => context.RequestServices.GetService<RequestTimeline>();

    private static IDisposable Measure(RequestContext context, Action<RequestTimeline, TimeSpan> record)
        => Timeline(context) is { } timeline
            ? new PhaseScope(elapsed => record(timeline, elapsed))
            : EmptyScope;

    private void Report(RequestTimeline timeline)
        => _logger.LogInformation(
            "parse {Parse} validate {Validate} compile {Compile} coerce {Coerce} "
            + "execute {Execute} total {Total} (document cache {DocumentCache}, "
            + "operation cache {OperationCache}, {Resolvers} resolvers)",
            Format(timeline.Parse),
            Format(timeline.Validate),
            Format(timeline.Compile),
            Format(timeline.CoerceVariables),
            Format(timeline.Execute),
            Format(timeline.Total),
            timeline.DocumentCacheHit ? "hit" : "miss",
            timeline.OperationCacheHit ? "hit" : "miss",
            timeline.ResolverCount);

    private static string Format(TimeSpan elapsed)
        => elapsed == TimeSpan.Zero ? "-" : $"{elapsed.TotalMilliseconds:0.000}ms";

    private sealed class PhaseScope(Action<TimeSpan> onDisposed) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();

        public void Dispose() => onDisposed(Stopwatch.GetElapsedTime(_start));
    }
}
