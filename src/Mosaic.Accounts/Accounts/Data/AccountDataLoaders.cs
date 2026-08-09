using GreenDonut;
using Mosaic.Accounts.Model;

namespace Mosaic.Accounts.Data;

/// <summary>
/// Accounts' DataLoaders.
/// </summary>
public static class AccountDataLoaders
{
    /// <summary>
    /// Customers by identifier, in one batch.
    /// </summary>
    /// <remarks>
    /// This is the loader with the most to gain in Mosaic, and not because of
    /// the batching. The catalog page asks for the authors of 120 reviews
    /// written by 12 customers, and a DataLoader deduplicates keys before it
    /// ever builds a batch: the promise cache is consulted in
    /// <c>LoadAsync</c>, so a repeated identifier gets the promise the first
    /// caller made and never reaches the fetch method. Twelve keys, not 120.
    /// </remarks>
    [DataLoader]
    public static Task<IReadOnlyDictionary<Guid, Customer>> GetCustomerByIdAsync(
        IReadOnlyList<Guid> ids,
        AccountsService accounts,
        CancellationToken cancellationToken)
        => accounts.GetCustomersByIdsAsync(ids, cancellationToken);
}
