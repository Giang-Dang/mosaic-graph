using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mosaic.ServiceDefaults.Counting;

public static class ServiceCallCountingExtensions
{
    /// <summary>
    /// Attaches a lookup counter to each request and reports the total once the
    /// response is done.
    /// </summary>
    public static IApplicationBuilder UseServiceCallCounting(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var count = new RequestLookupCount();
            context.Items[ServiceCallCounter.ItemKey] = count;

            await next();

            if (count.Value > 0)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Mosaic.ServiceLookups");

                logger.LogInformation("Service lookups this request: {Count}", count.Value);
            }
        });
}
