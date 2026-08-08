using Mosaic.Api.Inventory.Data;

namespace Mosaic.Api.Inventory;

public static class InventoryRegistration
{
    public static IServiceCollection AddInventoryDomain(this IServiceCollection services)
    {
        services.AddSingleton<InventorySeedData>();
        services.AddScoped<InventoryService>();
        return services;
    }
}
