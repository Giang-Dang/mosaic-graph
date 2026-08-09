namespace Mosaic.ServiceDefaults.Diagnostics;

/// <summary>
/// One GraphQL request's trip through the execution pipeline: which caches it
/// hit, how long each phase took, how many resolvers ran, and how many commands
/// reached the database.
/// </summary>
/// <remarks>
/// <para>
/// This is registered as a scoped service and read back through
/// <c>RequestServices</c>, which is the request's own service provider. That
/// matters, and it is the distinction chapter 2's lookup counter fell foul of:
/// the services injected into a resolver's parameters come from a child scope
/// created per resolver invocation, but <c>RequestServices</c> is not that
/// scope. Anything accumulating across a whole request can live here.
/// </para>
/// <para>
/// Chapter 3 wrote this class inside <c>Mosaic.Api</c>, and chapter 8's
/// extraction left it there, so for four chapters exactly one of Mosaic's
/// services could say anything about a request. Chapter 12 moved it here
/// because there are six of them now. That is not the fix chapter 23 owes the
/// book: six services each reporting their own timeline is still six unrelated
/// timelines, and nothing in a response says which of them belong to the same
/// federated query.
/// </para>
/// </remarks>
public sealed class RequestTimeline
{
    private int _resolverCount;
    private int _sqlCommandCount;

    /// <summary>The document was found in the document cache, so it was neither parsed nor validated.</summary>
    public bool DocumentCacheHit { get; set; }

    /// <summary>The compiled operation was found in the operation cache, so it was not compiled.</summary>
    public bool OperationCacheHit { get; set; }

    public TimeSpan Parse { get; set; }

    public TimeSpan Validate { get; set; }

    public TimeSpan Compile { get; set; }

    public TimeSpan CoerceVariables { get; set; }

    public TimeSpan Execute { get; set; }

    public TimeSpan Total { get; set; }

    /// <summary>
    /// How many field resolvers the execution engine ran. Resolvers run
    /// concurrently, so the increment has to be atomic for the same reason
    /// <see cref="Counting.RequestLookupCount"/>'s does.
    /// </summary>
    public int ResolverCount => Volatile.Read(ref _resolverCount);

    public void CountResolver() => Interlocked.Increment(ref _resolverCount);

    /// <summary>
    /// How many commands Entity Framework Core sent to PostgreSQL while serving
    /// this request. Counted by <see cref="Data.SqlCommandCounter"/>, which
    /// intercepts the command itself, so this is round trips rather than an
    /// estimate of them.
    /// </summary>
    public int SqlCommandCount => Volatile.Read(ref _sqlCommandCount);

    public void CountSqlCommand() => Interlocked.Increment(ref _sqlCommandCount);
}
