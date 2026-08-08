using Mosaic.Api.Accounts.Data;

namespace Mosaic.Api.Accounts;

public static class AccountsRegistration
{
    public static IServiceCollection AddAccountsDomain(this IServiceCollection services)
    {
        services.AddSingleton<AccountsSeedData>();
        services.AddScoped<AccountsService>();
        return services;
    }
}
