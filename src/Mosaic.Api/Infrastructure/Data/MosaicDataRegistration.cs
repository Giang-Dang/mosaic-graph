using Microsoft.EntityFrameworkCore;

namespace Mosaic.Api.Infrastructure.Data;

public static class MosaicDataRegistration
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
    /// one. Entity Framework Core does not support parallel operations on a
    /// single context instance, and that default is what keeps this from
    /// becoming everybody's first production incident with GraphQL.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMosaicDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddPooledDbContextFactory<MosaicDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(
                    new SqlCommandCounter(
                        serviceProvider.GetRequiredService<IHttpContextAccessor>())));

        services.AddScoped(serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<MosaicDbContext>>()
                .CreateDbContext());

        services.AddHostedService<DatabaseSeeder>();

        return services;
    }
}
