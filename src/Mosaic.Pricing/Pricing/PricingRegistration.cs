using Mosaic.Pricing.Data;

namespace Mosaic.Pricing;

public static class PricingRegistration
{
    public static IServiceCollection AddPricingDomain(this IServiceCollection services)
    {
        services.AddSingleton<PricingSeedData>();
        services.AddScoped<PricingService>();
        return services;
    }
}
