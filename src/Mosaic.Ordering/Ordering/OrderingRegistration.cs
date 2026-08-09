using Mosaic.Ordering.Data;

namespace Mosaic.Ordering;

public static class OrderingRegistration
{
    public static IServiceCollection AddOrderingDomain(this IServiceCollection services)
    {
        services.AddSingleton<OrderingSeedData>();
        services.AddScoped<OrderingService>();
        return services;
    }
}
