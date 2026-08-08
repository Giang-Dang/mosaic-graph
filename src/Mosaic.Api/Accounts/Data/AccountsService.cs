using Microsoft.EntityFrameworkCore;
using Mosaic.Api.Accounts.Model;
using Mosaic.Api.Infrastructure;
using Mosaic.Api.Infrastructure.Data;

namespace Mosaic.Api.Accounts.Data;

/// <summary>
/// Everything the rest of Mosaic is allowed to ask the Accounts domain.
/// </summary>
/// <remarks>
/// <see cref="GetCustomersByIdsAsync"/> is the one the DataLoader calls, and it
/// is the only overload that exists because Reviews and Ordering ask about a
/// customer more than once in the same request. The single-key method stays for
/// the root field that really does want one.
/// </remarks>
public sealed class AccountsService(MosaicDbContext db, ServiceCallCounter counter)
{
    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Customer?> GetCustomerByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    /// <summary>
    /// Several customers by their identifiers, keyed for a caller that has to
    /// match them back up. A customer who is not there is simply absent.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, Customer>> GetCustomersByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        counter.RecordLookup();

        return await db.Customers
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
    }
}
