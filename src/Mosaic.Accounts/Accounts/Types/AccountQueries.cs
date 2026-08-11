using HotChocolate.Authorization;
using Mosaic.Accounts.Data;
using Mosaic.Accounts.Model;

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
    /// Chapter 15, and the other half of the pair. <c>[Authorize]</c> comes
    /// from <c>HotChocolate.Authorization</c> and is enforced here, by this
    /// process, against the token this process validated.
    /// </para>
    /// <para>
    /// It compiles to an <c>@authorize</c> directive that the schema builder
    /// marks <c>Internal()</c>, which keeps it out of introspection and does
    /// not keep it out of what this service publishes: it is on the field in
    /// <c>schema/accounts.graphql</c>, declared underneath, and that file is
    /// what <c>_service</c> serves and the composer reads. The composer then
    /// drops it without a word, so nothing about this rule survives into the
    /// router's execution config either way.
    /// </para>
    /// <para>
    /// Which means the router in front of this service does not know this
    /// field is protected, cannot plan around it, and will happily send an
    /// anonymous request here and pass back whatever comes out. What comes out
    /// is an error, because this process checks. It also means the router has
    /// to be told to forward <c>Authorization</c>, or that error is what every
    /// caller gets, holding a good token or not. The two halves are
    /// independent on purpose and the chapter argues about which one should
    /// own a rule; what nothing does is derive either from the other.
    /// </para>
    /// </remarks>
    [Authorize]
    public static Task<Customer?> GetCustomerByIdAsync(
        [ID] Guid id,
        AccountsService accounts,
        CancellationToken cancellationToken)
        => accounts.GetCustomerByIdAsync(id, cancellationToken);
}
