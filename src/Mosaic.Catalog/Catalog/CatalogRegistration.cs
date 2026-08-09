using Mosaic.Catalog.Data;

namespace Mosaic.Catalog;

public static class CatalogRegistration
{
    public static IServiceCollection AddCatalogDomain(this IServiceCollection services)
    {
        services.AddSingleton<CatalogSeedData>();
        services.AddScoped<CatalogService>();
        return services;
    }
}
