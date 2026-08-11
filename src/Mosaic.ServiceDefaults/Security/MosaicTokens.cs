namespace Mosaic.ServiceDefaults.Security;

/// <summary>
/// The names in Mosaic's tokens: who issues them, who they are for, and the
/// scopes the graph knows about.
/// </summary>
/// <remarks>
/// <para>
/// This is the one part of chapter 15 that sits on the wrong side of chapter
/// 12's line and is here anyway, so the reason is worth writing down. Decision
/// 68 asks "do two services have to agree about this?" - if yes it is a
/// contract and gets copied, if no it is platform and gets shared. A scope
/// string looks like a contract, and it is one, but not between two Mosaic
/// services: it is between whoever issues tokens and each service separately.
/// Ordering does not have to agree with Accounts about
/// <c>orders:read</c>; both have to agree with the issuer.
/// </para>
/// <para>
/// What genuinely is a contract between two services is the meaning of
/// <c>sub</c>, and that is not here. Mosaic puts a customer's global object
/// identifier in it, and every service that wants the customer behind a token
/// decodes it with its own copy of <c>CustomerKey</c>, exactly as chapter 8
/// decided for a federation key and chapter 12 repeated fifteen times. The
/// format of an identifier stays copied; the mechanics of validating a
/// signature are shared.
/// </para>
/// </remarks>
public static class MosaicTokens
{
    /// <summary>The issuer every Mosaic token carries.</summary>
    public const string Issuer = "mosaic";

    /// <summary>The audience every Mosaic token carries.</summary>
    public const string Audience = "mosaic-graph";

    /// <summary>
    /// The environment variable holding the signing key.
    /// </summary>
    /// <remarks>
    /// A symmetric key rather than a JWKS endpoint, because the router's
    /// configuration takes either and an identity provider is a book of its
    /// own. The one thing that changes for a real deployment is the provider
    /// block in <c>router/config.yaml</c> and this file; nothing in any
    /// resolver knows the difference.
    /// </remarks>
    public const string SecretVariable = "MOSAIC_JWT_SECRET";

    /// <summary>
    /// The claim holding the scopes a token was granted, space delimited.
    /// </summary>
    /// <remarks>
    /// Space delimited because that is what OAuth 2.0 says a <c>scope</c>
    /// response parameter is, and because it is the shape the Cosmo Router
    /// reads by default: its <c>scope_claim</c> setting defaults to this name.
    /// The delimiter is the part that catches people, and
    /// <c>MosaicPolicies</c> below has the comment about it.
    /// </remarks>
    public const string ScopeClaim = "scope";

    /// <summary>The scopes this graph knows about.</summary>
    public static class Scopes
    {
        /// <summary>Read a customer's orders.</summary>
        public const string OrdersRead = "orders:read";

        /// <summary>Read the parts of a customer record that are not public.</summary>
        public const string CustomersRead = "customers:read";

        /// <summary>Write a review.</summary>
        public const string ReviewsWrite = "reviews:write";
    }

    /// <summary>The authorization policy names the six services register.</summary>
    /// <remarks>
    /// One policy per scope, and the names are deliberately the same shape as
    /// the scopes rather than the same strings. A policy is a thing an ASP.NET
    /// Core service evaluates; a scope is a thing a token carries. They line up
    /// one to one here because Mosaic is small, and the day they stop lining up
    /// is the day the policy earns its name.
    /// </remarks>
    public static class Policies
    {
        /// <summary>Requires <see cref="Scopes.OrdersRead"/>.</summary>
        public const string OrdersRead = "OrdersRead";

        /// <summary>Requires <see cref="Scopes.CustomersRead"/>.</summary>
        public const string CustomersRead = "CustomersRead";

        /// <summary>Requires <see cref="Scopes.ReviewsWrite"/>.</summary>
        public const string ReviewsWrite = "ReviewsWrite";
    }
}
