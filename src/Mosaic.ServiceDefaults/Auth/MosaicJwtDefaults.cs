using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Mosaic.ServiceDefaults.Auth;

/// <summary>
/// How a Mosaic service validates a bearer token, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Chapter 15. Every service in this graph validates the same tokens, issued
/// by the same authority, with the same clock skew and the same failure
/// behaviour, and none of them has an opinion about any of it. That is the
/// test decision 68 set for this library: a thing all six should do the same
/// way and nothing any two of them have to agree about between themselves.
/// </para>
/// <para>
/// The values are configuration rather than constants, and they come from the
/// environment. A signing secret in a committed file is a signing secret in
/// everybody's shell history, and the point of putting the wiring here is that
/// the wiring is the shared part - not the key.
/// </para>
/// <para>
/// HS256 with a shared secret rather than RS256 against a JWKS endpoint,
/// because a symmetric key needs no key server and this repository is meant to
/// start with <c>docker compose up</c>. Cosmo's router takes the same choice
/// through <c>authentication.jwt.jwks[].secret</c>. What a deployment does
/// instead is name a <c>url</c>, and the only line that changes is that one:
/// nothing in this file and nothing in any resolver knows which it is.
/// </para>
/// </remarks>
public static class MosaicJwtDefaults
{
    /// <summary>The issuer every Mosaic token carries.</summary>
    public const string Issuer = "https://mosaic.local/identity";

    /// <summary>The audience every Mosaic token is addressed to.</summary>
    public const string Audience = "mosaic-graph";

    /// <summary>
    /// The environment variable holding the symmetric signing key. Named
    /// rather than read inline so that the error below can print it.
    /// </summary>
    public const string SecretVariable = "MOSAIC_JWT_SECRET";

    /// <summary>
    /// Adds bearer-token authentication to a Mosaic service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MapInboundClaims = false</c> is the line worth reading twice. Left
    /// at its default, the handler rewrites short JWT claim names into the
    /// long WS-Federation URIs, so <c>sub</c> arrives as
    /// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>
    /// and a policy written against the name in the token does not match. The
    /// token is the contract; the claim names in it are the ones a resolver
    /// should see.
    /// </para>
    /// <para>
    /// Authentication is not authorisation and this method does neither: a
    /// request with no token, or with a bad one, still reaches the schema. It
    /// arrives unauthenticated, and what happens next is whatever the fields
    /// it asked for require. That is deliberate - a graph where every request
    /// needs a token cannot serve a product listing to a browser.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMosaicJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var secret = configuration[SecretVariable]
            ?? throw new InvalidOperationException(
                $"No signing key in {SecretVariable}. Every Mosaic service validates "
                + "the same tokens, so this is set once for the whole graph: "
                + "docker-compose.yml passes it to the six services and to the "
                + "router, and scripts/mint-token.mjs signs with the same value.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Issuer,
                    ValidAudience = Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,

                    // The default is five minutes, which is generous enough to
                    // hide an expiry bug in a test that runs in seconds.
                    ClockSkew = TimeSpan.Zero
                };
            });

        return services;
    }
}
