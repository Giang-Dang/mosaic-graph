using Mosaic.Api.Accounts.Data;
using Mosaic.Api.Accounts.Model;

namespace Mosaic.Api.Accounts.Types;

/// <summary>
/// The Accounts domain's entry into the graph. There is no field that lists
/// every customer: the only way in is by identifier, which is how the other
/// domains reach one anyway.
/// </summary>
[QueryType]
public static partial class AccountQueries
{
    /// <summary>One customer by their identifier.</summary>
    public static Task<Customer?> GetCustomerByIdAsync(
        [ID] Guid id,
        AccountsService accounts,
        CancellationToken cancellationToken)
        => accounts.GetCustomerByIdAsync(id, cancellationToken);
}
