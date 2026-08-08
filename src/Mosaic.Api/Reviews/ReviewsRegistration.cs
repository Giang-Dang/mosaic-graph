using Mosaic.Api.Reviews.Data;

namespace Mosaic.Api.Reviews;

public static class ReviewsRegistration
{
    public static IServiceCollection AddReviewsDomain(this IServiceCollection services)
    {
        services.AddSingleton<ReviewsSeedData>();
        services.AddScoped<ReviewsService>();
        return services;
    }
}
