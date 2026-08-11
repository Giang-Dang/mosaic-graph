using System.Security.Claims;
using System.Text;
using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Mosaic.ServiceDefaults.Security;

/// <summary>
/// What every Mosaic service does with a token: validate it, and turn the
/// scopes inside it into policies a resolver can ask for.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is about federation, and that is the point chapter 15 spends a
/// section on. A subgraph validating a bearer token is an ordinary ASP.NET Core
/// service validating a bearer token. What federation adds is a second party
/// that also validates it, ahead of this one, and a second vocabulary for
/// saying which fields need what.
/// </para>
/// <para>
/// Mosaic makes every service check the token itself rather than trusting a
/// header the router derived, and the reason is measurable: all seven services
/// listen on a host port, so a client that skips the router is one
/// <c>curl</c> away. A graph whose subgraphs are only reachable through the
/// router can take the cheaper arrangement, and chapter 15 shows what its
/// configuration looks like. The expensive part of that choice is not the code
/// below; it is that the decision has to be re-made every time somebody adds a
/// deployment target.
/// </para>
/// </remarks>
public static class MosaicSecurityDefaults
{
    /// <summary>
    /// Adds JWT bearer authentication and Mosaic's scope policies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MapInboundClaims = false</c> is the line to read twice. Left at its
    /// default of true, ASP.NET Core rewrites well-known JWT claim names into
    /// the long-form URIs of the old WS-Federation claim types, so the
    /// <c>sub</c> the token carries arrives as
    /// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>
    /// and a resolver looking for <c>sub</c> finds nothing. The token is
    /// correct, the signature is valid, the request is authenticated, and the
    /// claim is missing under the name it was written with. Turning the map off
    /// is what makes the claim names in a resolver the claim names in the
    /// token, which is also what the router reads.
    /// </para>
    /// <para>
    /// <c>ClockSkew = TimeSpan.Zero</c> matters for one measurement rather than
    /// for security. The default allows five minutes of skew, which means a
    /// token that has expired is still accepted for five more minutes, and
    /// chapter 15 wants to watch a short-lived token actually expire without
    /// waiting out the default.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMosaicSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var secret = configuration[MosaicTokens.SecretVariable]
            ?? Environment.GetEnvironmentVariable(MosaicTokens.SecretVariable)
            ?? throw new InvalidOperationException(
                $"No signing key. Set {MosaicTokens.SecretVariable} in the environment; "
                + "`node scripts/mint-token.mjs --help` prints one that works, and "
                + "docker-compose.yml sets the same value for every service and the router.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = MosaicTokens.Issuer,
                    ValidateAudience = true,
                    ValidAudience = MosaicTokens.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "sub",
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(MosaicTokens.Policies.OrdersRead,
                policy => policy.RequireAssertion(
                    context => HasScope(context.User, MosaicTokens.Scopes.OrdersRead)))
            .AddPolicy(MosaicTokens.Policies.CustomersRead,
                policy => policy.RequireAssertion(
                    context => HasScope(context.User, MosaicTokens.Scopes.CustomersRead)))
            .AddPolicy(MosaicTokens.Policies.ReviewsWrite,
                policy => policy.RequireAssertion(
                    context => HasScope(context.User, MosaicTokens.Scopes.ReviewsWrite)));

        return services;
    }

    /// <summary>
    /// Whether a caller holds one scope.
    /// </summary>
    /// <remarks>
    /// The obvious spelling of this policy is
    /// <c>policy.RequireClaim("scope", "orders:read")</c> and it does not work.
    /// A scope claim is one string holding every granted scope separated by
    /// spaces, so <c>RequireClaim</c> compares
    /// <c>"orders:read reviews:write"</c> against <c>"orders:read"</c> and
    /// refuses a token that plainly holds the scope. The failure is a 403 on a
    /// correct token, which is a bad half hour. Splitting is the whole fix, and
    /// it has to happen in every policy, which is why it happens here once.
    /// </remarks>
    public static bool HasScope(ClaimsPrincipal user, string scope)
    {
        foreach (var claim in user.FindAll(MosaicTokens.ScopeClaim))
        {
            foreach (var granted in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(granted, scope, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The <c>sub</c> claim, which in Mosaic is a customer's global object
    /// identifier, or null for an anonymous caller.
    /// </summary>
    /// <remarks>
    /// Returned as the string the token carried rather than as a
    /// <c>Guid</c>, because turning it into one is a decode against this
    /// service's own copy of the key format and belongs in the service that
    /// owns the copy. See any <c>CustomerKey</c> in the repository.
    /// </remarks>
    public static string? SubjectOrNull(ClaimsPrincipal? user)
        => user?.FindFirst("sub")?.Value;

    /// <summary>
    /// Turns on GraphQL authorization for a subgraph.
    /// </summary>
    /// <remarks>
    /// Deliberately not folded into <c>AddMosaicSubgraph</c>. That method is
    /// the settings that make a service composable with the other six, and this
    /// is not one of them: a subgraph with no guarded field composes exactly
    /// like a subgraph with one, because <c>@authorize</c> never reaches the
    /// composed graph. Keeping the call separate is what makes the four
    /// services that need it name it in their own <c>Program.cs</c>.
    /// </remarks>
    public static IRequestExecutorBuilder AddMosaicAuthorization(
        this IRequestExecutorBuilder builder)
        => builder.AddAuthorization();

    /// <summary>
    /// The two middleware calls, in the order ASP.NET Core requires.
    /// </summary>
    public static WebApplication UseMosaicSecurity(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
