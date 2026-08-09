using Microsoft.EntityFrameworkCore;
using Mosaic.ServiceDefaults.Data;

namespace Mosaic.Ordering.Data;

public static class OrderingDataRegistration
{
    /// <summary>
    /// Registers the database: the pooled context factory, a scoped context
    /// resolved from it, the command counter and the start-up seeder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The factory is pooled and therefore a singleton, so its options are
    /// built once. That is why <see cref="SqlCommandCounter"/> takes an
    /// accessor rather than the thing it wants to count into.
    /// </para>
    /// <para>
    /// The scoped registration is the one resolvers and DataLoaders use. A
    /// query field gets a fresh dependency injection scope per resolver, so a
    /// scoped context here means one context per resolver and no two concurrent
    /// resolvers sharing one.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOrderingDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddPooledDbContextFactory<OrderingDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(
                    new SqlCommandCounter(
                        serviceProvider.GetRequiredService<IHttpContextAccessor>())));

        services.AddScoped(serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<OrderingDbContext>>()
                .CreateDbContext());

        services.AddHostedService<OrderingDatabaseSeeder>();

        return services;
    }
}