using HotChocolate.Authorization;
using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mosaic.ServiceDefaults.Counting;
using Mosaic.ServiceDefaults.Diagnostics;

namespace Mosaic.ServiceDefaults;

/// <summary>
/// The settings that make a Mosaic service a subgraph the other five compose
/// with, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Chapter 8 found three subgraph settings that only fail once two schemas
/// meet: <c>registerNodeInterface: false</c>, and the two cost options. With
/// two services those were two copies of a comment. With six they are a
/// package, because a service that gets one of them wrong does not break
/// itself - it breaks everybody's composition, and the error names a type
/// rather than a service.
/// </para>
/// <para>
/// This is the line the book draws around a shared library. What is here is
/// true of a Mosaic service whatever domain it owns. What is not here is
/// anything two services have to agree about: the shape of a key, the members
/// of an enum, the name of a type. Those stay duplicated, because a shared
/// class turns an agreement into a build dependency.
/// </para>
/// </remarks>
public static class MosaicSubgraphDefaults
{
    /// <summary>
    /// The registrations every Mosaic service makes before it builds a schema:
    /// the HTTP context accessor the two counters hang off, the lookup counter
    /// itself, and the per-request timeline.
    /// </summary>
    /// <remarks>
    /// The timeline is scoped, not singleton: the pipeline phases, the
    /// resolvers and the command interceptor all write to the same instance,
    /// and only for as long as the request lives.
    /// </remarks>
    public static IServiceCollection AddMosaicServiceDefaults(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ServiceCallCounter>();
        services.AddScoped<RequestTimeline>();
        return services;
    }

    /// <summary>
    /// The GraphQL builder calls every Mosaic subgraph makes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AddApolloFederation()</c> is the line that makes this a subgraph
    /// rather than a GraphQL service: it adds <c>_service</c> and
    /// <c>_entities</c> to <c>Query</c>, the <c>_Any</c> scalar, an
    /// <c>_Entity</c> union built from every type carrying <c>[Key]</c>, and
    /// the <c>@link</c> naming the federation version the schema is written
    /// against.
    /// </para>
    /// <para>
    /// <c>registerNodeInterface: false</c> keeps the node id serialiser, the
    /// <c>Node</c> interface and every node resolver, and drops
    /// <c>Query.node</c> and <c>Query.nodes</c>. Two subgraphs declaring those
    /// is a composition error, and declaring them <c>@shareable</c> composes
    /// and is worse: chapter 13 measured a graph where two subgraphs both
    /// advertise <c>node</c>, and the router picks one and answers null for
    /// every identifier belonging to the other.
    /// </para>
    /// <para>
    /// Which is why the default is <c>false</c> and exactly one service passes
    /// <c>true</c>. <c>Mosaic.Nodes</c> owns the two root fields on behalf of
    /// the whole graph and is the only subgraph allowed to. The parameter
    /// exists so that the exception is declared in one place and reads as a
    /// decision rather than as a service that forgot to call the shared method.
    /// </para>
    /// <para>
    /// The two cost options are version skew between HotChocolate and the Cosmo
    /// composer, described in full in chapter 8. Turning the defaults off does
    /// not turn cost analysis off: the analyzer and its limits are separate
    /// settings and stay on. What is lost is the automatic per-field weight.
    /// Chapter 25 sets weights deliberately.
    /// </para>
    /// <para>
    /// <c>AddApplicationService&lt;ILoggerFactory&gt;()</c> is not optional and
    /// is easy to lose. A diagnostic listener is built from the schema service
    /// provider, which does not inherit the application's registrations, so
    /// without this line every service fails at startup with "Unable to resolve
    /// service for type 'ILoggerFactory' while attempting to activate
    /// 'RequestTimelineListener'".
    /// </para>
    /// </remarks>
    /// <param name="builder">The GraphQL builder to configure.</param>
    /// <param name="registerNodeInterface">
    /// Whether this subgraph publishes <c>Query.node</c> and
    /// <c>Query.nodes</c>. False everywhere but <c>Mosaic.Nodes</c>.
    /// </param>
    public static IRequestExecutorBuilder AddMosaicSubgraph(
        this IRequestExecutorBuilder builder,
        bool registerNodeInterface = false)
        => builder
            .AddApolloFederation()
            .AddGlobalObjectIdentification(registerNodeInterface)

            // Chapter 15. This is what makes [Authorize] do anything: without
            // it the attribute compiles, the schema builds, and every field
            // carrying it answers to anybody. It is the second half of the
            // authentication call in each Program.cs, and it enforces inside
            // this process only - nothing it does reaches the schema this
            // service publishes, so no composer and no router learns that any
            // field here is protected.
            .AddAuthorization()

            .ModifyCostOptions(options =>
            {
                options.ApplyCostDefaults = false;
                options.ApplySlicingArgumentDefaultValue = false;
            })
            .AddApplicationService<ILoggerFactory>()
            .AddDiagnosticEventListener<RequestTimelineListener>();

    /// <summary>
    /// Logs the assembled request pipeline at startup.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of <see cref="AddMosaicSubgraph"/>. Pipeline
    /// modifiers run in registration order and the cost analyzer inserts its
    /// middleware through one, so this has to be registered after
    /// <c>AddGraphQL()</c> rather than inside it. Report first and the list is
    /// shorter and wrong.
    /// </remarks>
    public static IServiceCollection AddMosaicPipelineReport(this IServiceCollection services)
        => services.AddPipelineReport();

    /// <summary>
    /// The lines every Mosaic service runs after building the application:
    /// the lookup counter, the authentication pair, and a health endpoint the
    /// compose file and both verification scripts poll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chapter 15 added the middle two, and their order is the whole reason
    /// they are here rather than in seven copies. <c>UseAuthentication</c>
    /// reads the token and puts a principal on the context;
    /// <c>UseAuthorization</c> is what evaluates a policy against it. Reverse
    /// them and every rule runs against an anonymous principal, which fails
    /// closed and therefore looks like a policy bug rather than an ordering
    /// bug.
    /// </para>
    /// <para>
    /// Both run before <c>MapGraphQL</c> in each service, because a resolver
    /// asking who the caller is has to be downstream of the thing that
    /// decided.
    /// </para>
    /// </remarks>
    public static WebApplication UseMosaicServiceDefaults(this WebApplication app)
    {
        app.UseServiceCallCounting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok("healthy"));
        return app;
    }
}
