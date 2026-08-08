using Mosaic.Api.Accounts.Model;
using Mosaic.Api.Infrastructure;

namespace Mosaic.Api.Accounts.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Accounts domain.
/// </summary>
/// <remarks>
/// As in Catalog, nothing here accepts a list of identifiers. Reviews and
/// Ordering each reach a customer one row at a time through
/// <see cref="GetCustomerByIdAsync"/>. That omission is deliberate, and it is
/// what a nested query against this schema is meant to expose.
/// </remarks>
public sealed class AccountsService(InMemoryAccountsData data, ServiceCallCounter counter)
{
    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken)
    {
        await counter.RecordLookupAsync(cancellationToken);
        return data.Customers;
    }

    public async Task<Customer?> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await counter.RecordLookupAsync(cancellationToken);
        return data.Customers.FirstOrDefault(c => c.Id == id);
    }
}
