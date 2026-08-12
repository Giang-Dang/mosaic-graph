using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mosaic.Sample.ExecutorInternals;

/// <summary>
/// The five cases, each one a fact chapter 16 prints.
/// </summary>
internal static class Cases
{
    private static readonly Dictionary<string, Func<Task>> s_cases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pure-fields-cost-no-task"] = PureFieldsCostNoTaskAsync,
            ["run-task-counts-no-resolver"] = RunTaskCountsNoResolverAsync,
            ["authorization-defeats-the-validation-cache"] = AuthorizationDefeatsTheValidationCacheAsync,
            ["warmup-fills-the-operation-cache"] = WarmupFillsTheOperationCacheAsync,
            ["single-flight-compiles-once"] = SingleFlightCompilesOnceAsync
        };

    public static async Task<int> RunAsync(string[] args)
    {
        var selected = args.Length > 0 ? args[0] : null;

        if (selected is not null && !s_cases.ContainsKey(selected))
        {
            Console.Error.WriteLine($"Unknown case '{selected}'. Known cases:");
            foreach (var known in s_cases.Keys)
            {
                Console.Error.WriteLine($"  {known}");
            }

            return 1;
        }

        var failures = 0;

        foreach (var (name, run) in s_cases)
        {
            if (selected is not null
                && !string.Equals(name, selected, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Console.WriteLine($"== {name}");

            try
            {
                await run();
                Console.WriteLine("   PASS");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"   FAIL {ex.Message}");
            }

            Console.WriteLine();
        }

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// The resolver count has always been a count of resolver tasks. A field
    /// whose resolver returns something other than a task gets a
    /// <c>PureFieldDelegate</c>, runs inline, and never raises
    /// <c>ResolveFieldValue</c>.
    /// </summary>
    private static async Task PureFieldsCostNoTaskAsync()
    {
        var executor = await BuildAsync();

        EventCounter.Reset();
        await RunDocumentAsync(executor, "{ things { id name upper } }");
        var pureOnly = EventCounter.ResolverTasks;

        EventCounter.Reset();
        await RunDocumentAsync(executor, "{ things { id name upper delayed } }");
        var withAsync = EventCounter.ResolverTasks;

        Console.WriteLine($"   three things, nine pure fields selected -> {pureOnly} resolver tasks");
        Console.WriteLine($"   the same plus one async field per thing -> {withAsync} resolver tasks");

        // The root field is asynchronous, so it is the one task in the first run.
        Assert(pureOnly == 1, $"expected 1 resolver task for the pure selection, got {pureOnly}");

        // The root, plus one per thing for the field that returns a task.
        Assert(withAsync == 4, $"expected 4 resolver tasks once an async field is selected, got {withAsync}");
    }

    /// <summary>
    /// <c>RunTask</c> is gated by the same flag as <c>ResolveFieldValue</c> and
    /// counts something else: only <c>DeferTask</c> and the all-fields-skipped
    /// placeholder reach it. For an ordinary query it fires zero times.
    /// </summary>
    private static async Task RunTaskCountsNoResolverAsync()
    {
        var executor = await BuildAsync(b => b.ModifyOptions(o => o.EnableDefer = true));

        EventCounter.Reset();
        await RunDocumentAsync(executor, "{ things { id delayed } }");
        var plainResolvers = EventCounter.ResolverTasks;
        var plainTasks = EventCounter.ExecutionTasks;

        EventCounter.Reset();
        await RunDocumentAsync(executor, "{ things { id ... @defer { delayed } } }");
        var deferTasks = EventCounter.ExecutionTasks;

        Console.WriteLine($"   plain      -> {plainResolvers} resolver tasks, {plainTasks} execution tasks");
        Console.WriteLine($"   with defer -> execution tasks {deferTasks}");

        Assert(plainResolvers == 4, $"expected 4 resolver tasks for the plain query, got {plainResolvers}");
        Assert(plainTasks == 0, $"expected 0 execution tasks for the plain query, got {plainTasks}");

        // A DeferTask is what finally raises RunTask.
        Assert(deferTasks > 0, $"expected at least one execution task once @defer is used, got {deferTasks}");
    }

    /// <summary>
    /// <c>AddAuthorization</c> registers <c>AuthorizeValidationRule</c>, whose
    /// <c>IsCacheable</c> is hard-coded false. That alone makes
    /// <c>DocumentValidator.HasNonCacheableRules</c> true, so the validation
    /// phase re-opens on every document-cache hit - with no guarded field
    /// anywhere in the schema.
    /// </summary>
    private static async Task AuthorizationDefeatsTheValidationCacheAsync()
    {
        const string document = "{ things { id name } }";

        const string documentId = "authorization-case";

        var plain = await BuildAsync();
        EventCounter.Reset();
        await RunDocumentAsync(plain, document, documentId);
        await RunDocumentAsync(plain, document, documentId);
        var plainValidations = EventCounter.Validate;
        var plainDocumentHits = EventCounter.DocumentCacheHits;

        // The only difference between the two schemas. No [Authorize] anywhere,
        // no policy, no guarded field: the call itself is the whole change.
        var authorized = await BuildAsync(b => b.AddAuthorization());
        EventCounter.Reset();
        await RunDocumentAsync(authorized, document, documentId);
        await RunDocumentAsync(authorized, document, documentId);
        var authorizedValidations = EventCounter.Validate;
        var authorizedDocumentHits = EventCounter.DocumentCacheHits;

        Console.WriteLine(
            $"   without AddAuthorization -> {plainValidations} validation(s) over 2 requests, "
            + $"{plainDocumentHits} document cache hit(s)");
        Console.WriteLine(
            $"   with AddAuthorization    -> {authorizedValidations} validation(s) over 2 requests, "
            + $"{authorizedDocumentHits} document cache hit(s)");

        // Both schemas must actually hit the document cache on the second
        // request, or the comparison is measuring something else.
        Assert(
            plainDocumentHits == 1,
            $"expected 1 document cache hit without authorization, got {plainDocumentHits}");
        Assert(
            authorizedDocumentHits == 1,
            $"expected 1 document cache hit with authorization, got {authorizedDocumentHits}");

        Assert(
            plainValidations == 1,
            $"expected validation to run once without authorization, got {plainValidations}");
        Assert(
            authorizedValidations == 2,
            $"expected validation to re-open on the cache hit with authorization, got {authorizedValidations}");
    }

    /// <summary>
    /// A warmup request is parsed, validated, costed and compiled and then
    /// dropped by the middleware at position 10, so the first real client
    /// request finds its operation already compiled and runs no compiler.
    /// </summary>
    /// <remarks>
    /// The document cache is a separate question and this case deliberately
    /// asserts that it stays cold. <c>DocumentCacheMiddleware</c> only consults
    /// the cache when the request carries a <c>DocumentId</c> or a
    /// <c>DocumentHash</c>, and only stores one when the parsed document ended
    /// up with an id. An in-process request built from source text carries
    /// neither, so there is no document-cache lookup at all - which is why the
    /// numbers here differ from the ones chapter 3 measured over HTTP, where
    /// the transport supplies the hash.
    /// </remarks>
    private static async Task WarmupFillsTheOperationCacheAsync()
    {
        const string document = "{ things { id name upper } }";

        var cold = await BuildAsync();
        EventCounter.Reset();
        await RunDocumentAsync(cold, document);
        var coldDocumentHits = EventCounter.DocumentCacheHits;
        var coldOperationHits = EventCounter.OperationCacheHits;

        var warmed = await BuildAsync(b => b.AddWarmupTask(async (executor, ct) =>
        {
            var request = OperationRequestBuilder.New()
                .SetDocument(document)
                .MarkAsWarmupRequest()
                .Build();

            var result = await executor.ExecuteAsync(request, ct);
            await result.DisposeAsync();
        }));

        EventCounter.Reset();
        await RunDocumentAsync(warmed, document);
        var warmDocumentHits = EventCounter.DocumentCacheHits;
        var warmOperationHits = EventCounter.OperationCacheHits;
        var warmResolvers = EventCounter.ResolverTasks;

        Console.WriteLine(
            $"   first request, no warmup task -> document cache hits {coldDocumentHits}, "
            + $"operation cache hits {coldOperationHits}");
        Console.WriteLine(
            $"   first request, warmed         -> document cache hits {warmDocumentHits}, "
            + $"operation cache hits {warmOperationHits}");

        Assert(
            coldDocumentHits == 0,
            $"expected a cold first request to miss the document cache, got {coldDocumentHits}");
        Assert(
            coldOperationHits == 0,
            $"expected a cold first request to miss the operation cache, got {coldOperationHits}");

        // The half warmup actually buys: the client request compiles nothing.
        Assert(
            warmOperationHits == 1,
            $"expected the warmed first request to hit the operation cache, got {warmOperationHits}");

        // And the half it does not, in process. See the remarks above: without a
        // document id or hash on the request there is no document-cache lookup
        // to hit. If this ever starts hitting, the middleware's conditions have
        // changed and the chapter's explanation is stale.
        Assert(
            warmDocumentHits == 0,
            $"expected no document-cache lookup for an in-process request, got {warmDocumentHits}");

        // The client request still resolves; it is the warmup request that did not.
        Assert(warmResolvers == 1, $"expected the client request to still run its resolver, got {warmResolvers}");
    }

    /// <summary>
    /// Concurrent requests for the same uncached operation elect one
    /// single-flight leader. The others await its compiled operation rather
    /// than compiling the same document again.
    /// </summary>
    private static async Task SingleFlightCompilesOnceAsync()
    {
        const int burst = 32;

        // A document this executor has never seen, so there is a real
        // compilation to coalesce.
        const string document = "query Burst { things { id name upper } count }";

        var executor = await BuildAsync();

        EventCounter.Reset();

        var requests = Enumerable
            .Range(0, burst)
            .Select(_ => Task.Run(() => RunDocumentAsync(executor, document)))
            .ToArray();

        await Task.WhenAll(requests);

        var compiles = EventCounter.Compile;

        Console.WriteLine($"   {burst} concurrent identical first-time requests -> {compiles} compilation(s)");

        Assert(compiles == 1, $"expected exactly one compilation for the burst, got {compiles}");
    }

    private static async Task<IRequestExecutor> BuildAsync(
        Func<IRequestExecutorBuilder, IRequestExecutorBuilder>? configure = null)
    {
        var builder = new ServiceCollection()
            // ASP.NET Core's authorization services take an ILogger, and one of
            // the cases registers them. A bare ServiceCollection has no logging.
            .AddLogging()
            .AddGraphQL()
            .AddExecutorInternals()
            .AddDiagnosticEventListener<EventCounter>();

        builder = configure?.Invoke(builder) ?? builder;

        return await builder.BuildRequestExecutorAsync();
    }

    private static async Task RunDocumentAsync(
        IRequestExecutor executor,
        string document,
        string? documentId = null)
    {
        var builder = OperationRequestBuilder.New().SetDocument(document);

        // The document cache is keyed by document id, and DocumentCacheMiddleware
        // only looks in it when the request carries one. Over HTTP the transport
        // supplies a hash and this happens for free; in process it has to be
        // asked for, which is why the cases that care about the document cache
        // pass an id and the ones that do not, do not.
        if (documentId is not null)
        {
            builder.SetDocumentId(documentId);
        }

        var request = builder.Build();
        var result = await executor.ExecuteAsync(request);

        switch (result)
        {
            case OperationResult single:
                ThrowOnErrors(single, document);
                break;

            // A @defer'd query answers with a stream rather than one result,
            // and the deferred payloads only arrive if somebody reads it.
            case IResponseStream stream:
                await foreach (var chunk in stream.ReadResultsAsync())
                {
                    if (chunk is OperationResult chunkResult)
                    {
                        ThrowOnErrors(chunkResult, document);
                    }
                }

                break;

            default:
                throw new InvalidOperationException($"unexpected result type {result.GetType().Name}");
        }

        await result.DisposeAsync();
    }

    private static void ThrowOnErrors(OperationResult result, string document)
    {
        if (result.Errors is { Count: > 0 } errors)
        {
            throw new InvalidOperationException(
                $"the document '{document}' answered with errors: {errors[0].Message}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
