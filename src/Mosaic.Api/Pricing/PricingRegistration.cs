using Mosaic.Api.Pricing.Data;

namespace Mosaic.Api.Pricing;

public static class PricingRegistration
{
    public static IServiceCollection AddPricingDomain(this IServiceCollection services)
    {
        services.AddSingleton<PricingSeedData>();
        services.AddScoped<PricingService>();
        return services;
    }
}
