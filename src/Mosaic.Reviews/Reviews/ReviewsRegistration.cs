using Microsoft.Extensions.DependencyInjection.Extensions;
using Mosaic.Reviews.Data;
using Mosaic.Reviews.Streams;
using NATS.Client.Core;

namespace Mosaic.Reviews;

public static class ReviewsRegistration
{
    public static IServiceCollection AddReviewsDomain(this IServiceCollection services)
    {
        services.AddSingleton<ReviewsSeedData>();
        services.AddScoped<ReviewsService>();
        return services;
    }

    /// <summary>
    /// The publisher chapter 14 added, and the NATS connection behind it.
    /// </summary>
    /// <remarks>
    /// Registered separately from the domain because it is not domain code and
    /// not platform code either: it is this one service's link to a broker the
    /// other six know nothing about.
    /// <para>
    /// The connection is constructed here rather than through
    /// <c>AddNatsClient</c> from NATS.Client.Hosting, because the one-line
    /// overload of that method's <c>ConfigureOptions</c> is marked obsolete at
    /// 3.1.0 and its replacement takes an options builder. One <c>new</c> says
    /// what is being built. <c>NatsConnection</c> dials lazily, so a Reviews
    /// started with no broker running starts anyway and costs a warning on the
    /// first review rather than refusing to serve queries.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddReviewStreams(
        this IServiceCollection services,
        string url)
    {
        services.TryAddSingleton<INatsConnection>(
            _ => new NatsConnection(NatsOpts.Default with { Url = url }));
        services.TryAddScoped<ReviewStreamPublisher>();
        return services;
    }
}
