using Mosaic.Reviews.Data;

namespace Mosaic.Reviews;

public static class ReviewsRegistration
{
    public static IServiceCollection AddReviewsDomain(this IServiceCollection services)
    {
        services.AddSingleton<ReviewsSeedData>();
        services.AddScoped<ReviewsService>();
        return services;
    }
}
