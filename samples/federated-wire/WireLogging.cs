using Microsoft.AspNetCore.HttpLogging;

namespace Mosaic.Sample.Wire;

/// <summary>
/// The one piece of configuration both subgraphs in this sample share: write
/// every request and every response to the console, headers and bodies
/// included.
/// </summary>
/// <remarks>
/// This is ASP.NET Core's own HTTP logging middleware, not a proxy and not a
/// packet capture. That matters for the chapter: what gets printed is what the
/// subgraph process actually received and actually sent, read inside the
/// server rather than inferred from outside it.
/// </remarks>
public static class WireLogging
{
    /// <summary>
    /// Headers the middleware would otherwise print as <c>[Redacted]</c>.
    /// Its default allow-list covers the well-known ones; these are the
    /// headers the Cosmo router adds, discovered by running the sample once
    /// and reading what came out redacted.
    /// </summary>
    private static readonly string[] RouterHeaders =
    [
        "graphql-client-name",
        "graphql-client-version",
        "traceparent",
        "tracestate"
    ];

    public static WebApplicationBuilder AddWireLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpLogging(options =>
        {
            options.LoggingFields =
                HttpLoggingFields.RequestMethod
                | HttpLoggingFields.RequestPath
                | HttpLoggingFields.RequestHeaders
                | HttpLoggingFields.RequestBody
                | HttpLoggingFields.ResponseStatusCode
                | HttpLoggingFields.ResponseBody;

            // The defaults are 32 KB in and 32 KB out, which is plenty here,
            // but a truncated body would be a lie in print rather than a
            // missing line, so say the number rather than inherit it.
            options.RequestBodyLogLimit = 32 * 1024;
            options.ResponseBodyLogLimit = 32 * 1024;

            // A body is only logged when its media type is on this list.
            // GraphQL over HTTP answers with application/graphql-response+json,
            // which is not a type ASP.NET Core knows about.
            options.MediaTypeOptions.AddText("application/json");
            options.MediaTypeOptions.AddText("application/graphql-response+json");

            foreach (var header in RouterHeaders)
            {
                options.RequestHeaders.Add(header);
            }
        });

        // Without this the middleware is registered and silent: the default
        // minimum level for Microsoft.* categories hides its Information logs.
        builder.Logging.AddFilter(
            "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware",
            LogLevel.Information);

        return builder;
    }
}
