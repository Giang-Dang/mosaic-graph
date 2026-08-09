using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mosaic.ServiceDefaults.Diagnostics;

/// <summary>
/// Logs the request pipeline HotChocolate actually assembled, once, at startup.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is a list of <see cref="RequestMiddlewareConfiguration"/> that
/// the executor composes into one delegate chain when the schema is built, and
/// it is assembled in two steps: the default pipeline is added first, then every
/// registered modifier runs in registration order. Insertions that name a
/// neighbour - <c>UseRequest(..., after: "DocumentValidationMiddleware")</c> -
/// are modifiers, which is why a modifier registered last sees the finished
/// list.
/// </para>
/// <para>
/// HotChocolate configures the pipeline through named options, so this reads it
/// back the same way rather than through reflection. Nothing here mutates the
/// list.
/// </para>
/// <para>
/// Registration order still matters and this class cannot enforce it: call it
/// after <c>AddGraphQL()</c> or the report is the pipeline as it stood before
/// the cost analyzer inserted its middleware, which is a shorter and wrong
/// list. Chapter 3 measured that.
/// </para>
/// </remarks>
public static class PipelineReportExtensions
{
    public static IServiceCollection AddPipelineReport(this IServiceCollection services)
        => services.AddTransient<IConfigureOptions<RequestExecutorSetup>>(
            serviceProvider => new ConfigureNamedOptions<RequestExecutorSetup>(
                ISchemaDefinition.DefaultName,
                setup => setup.PipelineModifiers.Add(
                    pipeline => Report(serviceProvider, pipeline))));

    private static void Report(
        IServiceProvider serviceProvider,
        IList<RequestMiddlewareConfiguration> pipeline)
    {
        var logger = serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Mosaic.Pipeline");

        logger.LogInformation("Request pipeline: {Count} middleware", pipeline.Count);

        for (var i = 0; i < pipeline.Count; i++)
        {
            logger.LogInformation(
                "  {Position}. {Key}",
                i + 1,
                pipeline[i].Key ?? "(unkeyed)");
        }
    }
}
