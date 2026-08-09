using Microsoft.EntityFrameworkCore;

namespace Mosaic.Catalog.Data;

public static class CatalogDataRegistration
{
    /// <summary>
    /// Registers the database: the pooled context factory, a scoped context
    /// resolved from it, and the start-up seeder.
    /// </summary>
    /// <remarks>
    /// Mosaic.Api's equivalent also attaches an EF Core command interceptor
    /// that counts statements, because chapters 3 and 4 are about that number.
    /// This service was born in chapter 8 and has no such history; chapter 23
    /// is where both of them get instrumentation worth deploying.
    /// </remarks>
    public static IServiceCollection AddCatalogDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddPooledDbContextFactory<CatalogDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped(serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<CatalogDbContext>>()
                .CreateDbContext());

        services.AddHostedService<CatalogDatabaseSeeder>();

        return services;
    }
}
