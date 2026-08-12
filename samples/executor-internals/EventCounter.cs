using HotChocolate.Execution;
using HotChocolate.Execution.Instrumentation;
using HotChocolate.Resolvers;

namespace Mosaic.Sample.ExecutorInternals;

/// <summary>
/// Counts the execution diagnostic events this sample is about.
/// </summary>
/// <remarks>
/// <para>
/// The counters are static because a diagnostic listener is constructed by the
/// schema service provider and every case in this sample builds its own
/// executor. Cases run one at a time and each calls <see cref="Reset"/> first,
/// so a static counter is the simplest thing that works; the increments are
/// still interlocked, because resolvers run concurrently and one case fires a
/// deliberate burst.
/// </para>
/// <para>
/// <see cref="EnableResolveFieldValue"/> gates two events rather than one.
/// Turning it on to see <see cref="ResolveFieldValue"/> also turns on
/// <see cref="RunTask"/>, which counts something else entirely, and that is one
/// of the things this sample exists to show.
/// </para>
/// </remarks>
public sealed class EventCounter : ExecutionDiagnosticEventListener
{
    private static int s_validate;
    private static int s_compile;
    private static int s_resolveFieldValue;
    private static int s_runTask;
    private static int s_documentCacheHits;
    private static int s_operationCacheHits;

    /// <summary>How many times the validation phase was opened.</summary>
    public static int Validate => Volatile.Read(ref s_validate);

    /// <summary>How many times an operation was compiled.</summary>
    public static int Compile => Volatile.Read(ref s_compile);

    /// <summary>How many resolver tasks ran. Pure fields never reach this.</summary>
    public static int ResolverTasks => Volatile.Read(ref s_resolveFieldValue);

    /// <summary>
    /// How many execution tasks ran. Only <c>DeferTask</c> and the
    /// all-fields-skipped placeholder reach this at 16.6.0; a resolver never
    /// does.
    /// </summary>
    public static int ExecutionTasks => Volatile.Read(ref s_runTask);

    public static int DocumentCacheHits => Volatile.Read(ref s_documentCacheHits);

    public static int OperationCacheHits => Volatile.Read(ref s_operationCacheHits);

    public static void Reset()
    {
        Interlocked.Exchange(ref s_validate, 0);
        Interlocked.Exchange(ref s_compile, 0);
        Interlocked.Exchange(ref s_resolveFieldValue, 0);
        Interlocked.Exchange(ref s_runTask, 0);
        Interlocked.Exchange(ref s_documentCacheHits, 0);
        Interlocked.Exchange(ref s_operationCacheHits, 0);
    }

    public override bool EnableResolveFieldValue => true;

    public override IDisposable ValidateDocument(RequestContext context)
    {
        Interlocked.Increment(ref s_validate);
        return EmptyScope;
    }

    public override IDisposable CompileOperation(RequestContext context)
    {
        Interlocked.Increment(ref s_compile);
        return EmptyScope;
    }

    public override IDisposable ResolveFieldValue(IMiddlewareContext context)
    {
        Interlocked.Increment(ref s_resolveFieldValue);
        return EmptyScope;
    }

    public override IDisposable RunTask(IExecutionTask task)
    {
        Interlocked.Increment(ref s_runTask);
        return EmptyScope;
    }

    public override void RetrievedDocumentFromCache(RequestContext context)
        => Interlocked.Increment(ref s_documentCacheHits);

    public override void RetrievedOperationFromCache(RequestContext context)
        => Interlocked.Increment(ref s_operationCacheHits);
}
