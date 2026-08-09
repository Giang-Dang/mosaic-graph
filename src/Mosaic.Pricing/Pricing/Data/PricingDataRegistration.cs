using Microsoft.EntityFrameworkCore;
using Mosaic.ServiceDefaults.Data;

namespace Mosaic.Pricing.Data;

public static class PricingDataRegistration
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
    /// query field gets a fresh dependency injection scope per resolver, which
    /// chapter 3 read out of <c>ResolverTask</c>, so a scoped context here
    /// means one context per resolver and no two concurrent resolvers sharing
    /// one.
    /// </para>
    /// <para>
    /// Chapter 8 gave Catalog no command counter, on the argument that a
    /// service born in chapter 8 has no history of counting. Chapter 12
    /// reversed that for all six: with one service the number told you what
    /// that service did, and with six the only way to know which of them is
    /// doing the work is for each of them to say.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddPricingDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddPooledDbContextFactory<PricingDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(
                    new SqlCommandCounter(
                        serviceProvider.GetRequiredService<IHttpContextAccessor>())));

        services.AddScoped(serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<PricingDbContext>>()
                .CreateDbContext());

        services.AddHostedService<PricingDatabaseSeeder>();

        return services;
    }
}
