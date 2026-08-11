using HotChocolate.ApolloFederation.Types;
using HotChocolate.Authorization;
using Mosaic.Accounts.Data;
using Mosaic.Accounts.Model;
using Mosaic.ServiceDefaults.Security;

namespace Mosaic.Accounts.Types;

/// <summary>
/// The Accounts domain's entry into the graph. There is no field that lists
/// every customer: the only way in is by identifier, which is how the other
/// domains reach one anyway.
/// </summary>
[QueryType]
public static partial class AccountQueries
{
    /// <summary>One customer by their identifier.</summary>
    /// <remarks>
    /// <para>
    /// Chapter 15. A scope rather than a bare <c>@authenticated</c>, because
    /// this field is the one route into a customer record that is not somebody
    /// following a review back to its author, and being signed in is not the
    /// same as being allowed to look people up.
    /// </para>
    /// <para>
    /// The pair below is the same doubling <c>Customer.email</c> carries and
    /// with one extra wrinkle worth knowing: the composer reads
    /// <c>@requiresScopes</c> and writes <em>two</em> things into the router's
    /// field configuration, <c>requiredOrScopes</c> and
    /// <c>requiresAuthentication: true</c>. A scope implies a token, so the
    /// router refuses an anonymous caller here without <c>@authenticated</c>
    /// having been written anywhere.
    /// </para>
    /// <para>
    /// The scopes argument is a list of lists: the outer list is an OR and the
    /// inner list an AND. One scope is therefore <c>[["customers:read"]]</c>,
    /// which reads oddly and is the spec's shape rather than HotChocolate's
    /// invention. A second <c>[RequiresScopes]</c> attribute on the same field
    /// appends another OR branch rather than replacing this one.
    /// </para>
    /// </remarks>
    [RequiresScopes([MosaicTokens.Scopes.CustomersRead])]
    [Authorize(MosaicTokens.Policies.CustomersRead)]
    public static Task<Customer?> GetCustomerByIdAsync(
        [ID] Guid id,
        AccountsService accounts,
        CancellationToken cancellationToken)
        => accounts.GetCustomerByIdAsync(id, cancellationToken);
}
