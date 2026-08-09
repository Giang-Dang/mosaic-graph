using Mosaic.Accounts.Data;

namespace Mosaic.Accounts;

public static class AccountsRegistration
{
    public static IServiceCollection AddAccountsDomain(this IServiceCollection services)
    {
        services.AddSingleton<AccountsSeedData>();
        services.AddScoped<AccountsService>();
        return services;
    }
}
