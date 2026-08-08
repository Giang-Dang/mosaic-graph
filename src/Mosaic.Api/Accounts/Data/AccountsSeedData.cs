using Mosaic.Api.Accounts.Model;

namespace Mosaic.Api.Accounts.Data;

/// <summary>
/// Mosaic's customers. Twelve of them: enough that a review feed repeats a
/// name now and then, few enough to hold in your head while you read a trace.
/// <para>
/// Until chapter 4 this list was the store. It is now the seed: the same twelve
/// rows, written into PostgreSQL once at start-up, so that every identifier the
/// earlier chapters printed still identifies the same customer.
/// </para>
/// </summary>
public sealed class AccountsSeedData
{
    /// <summary>
    /// Customer identifiers follow the same fixed pattern the catalog uses for
    /// products, with a different first character so a misrouted identifier is
    /// obvious on sight. Reviews and Ordering seed their own data by calling
    /// this method rather than by sharing a file with Accounts, because once
    /// the book splits these domains into separate services the identifier is
    /// the only contract left between them.
    /// </summary>
    public static Guid CustomerId(int n) => new($"c0000000-0000-4000-8000-{n:D12}");

    public IReadOnlyList<Customer> Customers { get; } =
    [
        new(CustomerId(1), "Anneke de Vries", "anneke.devries@example.com"),
        new(CustomerId(2), "Mateusz Kowalczyk", "m.kowalczyk@example.net"),
        new(CustomerId(3), "Sofia Ferrari", "sofia.ferrari@example.org"),
        new(CustomerId(4), "Lars Andersen", "lars.andersen@example.com"),
        new(CustomerId(5), "Chloe Moreau", "chloe.moreau@example.net"),
        new(CustomerId(6), "Tomas Novak", "tomas.novak@example.org"),
        new(CustomerId(7), "Ingrid Lindqvist", "ingrid.lindqvist@example.com"),
        new(CustomerId(8), "Rafael Oliveira", "rafael.oliveira@example.net"),
        new(CustomerId(9), "Katharina Brandt", "k.brandt@example.org"),
        new(CustomerId(10), "Eoin Gallagher", "eoin.gallagher@example.com"),
        new(CustomerId(11), "Marta Ibanez", "marta.ibanez@example.net"),
        new(CustomerId(12), "Janos Halasz", "janos.halasz@example.org")
    ];
}
