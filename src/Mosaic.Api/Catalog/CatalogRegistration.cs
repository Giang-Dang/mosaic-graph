using Mosaic.Api.Catalog.Data;

namespace Mosaic.Api.Catalog;

public static class CatalogRegistration
{
    public static IServiceCollection AddCatalogDomain(this IServiceCollection services)
    {
        services.AddSingleton<CatalogSeedData>();
        services.AddScoped<CatalogService>();
        return services;
    }
}
