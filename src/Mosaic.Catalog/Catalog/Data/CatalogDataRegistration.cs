using Microsoft.EntityFrameworkCore;
using Mosaic.ServiceDefaults.Data;

namespace Mosaic.Catalog.Data;

public static class CatalogDataRegistration
{
    /// <summary>
    /// Registers the database: the pooled context factory, a scoped context
    /// resolved from it, the command counter and the start-up seeder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The command counter is new at chapter 12. Chapter 8 left it out and gave
    /// a reason: a service born in chapter 8 has no history of counting, and
    /// chapter 23 would give both services something better. The reason did not
    /// survive contact with six services. With one instrumented process the
    /// number told you what that process did; with six, a graph where one
    /// service reports and five do not is a graph where every question about
    /// cost has the same answer, which is that it happened somewhere else.
    /// </para>
    /// <para>
    /// This is still not observability. Six services each reporting their own
    /// timeline is six unrelated timelines, and nothing in a response says which
    /// of them belong to the same federated query. Chapter 23 owns the trace
    /// that joins them, and it is a different piece of work rather than a bigger
    /// version of this one.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCatalogDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddPooledDbContextFactory<CatalogDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(
                    new SqlCommandCounter(
                        serviceProvider.GetRequiredService<IHttpContextAccessor>())));

        services.AddScoped(serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>()
                .CreateDbContext());

        services.AddHostedService<CatalogDatabaseSeeder>();

        return services;
    }
}
