using Mosaic.Api.Reviews.Data;

namespace Mosaic.Api.Reviews;

public static class ReviewsRegistration
{
    public static IServiceCollection AddReviewsDomain(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryReviewsData>();
        services.AddScoped<ReviewsService>();
        return services;
    }
}
