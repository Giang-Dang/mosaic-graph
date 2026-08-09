using Mosaic.Inventory.Data;

namespace Mosaic.Inventory;

public static class InventoryRegistration
{
    public static IServiceCollection AddInventoryDomain(this IServiceCollection services)
    {
        services.AddSingleton<InventorySeedData>();
        services.AddScoped<InventoryService>();
        return services;
    }
}
