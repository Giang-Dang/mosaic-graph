namespace Mosaic.Api.Accounts.Model;

/// <summary>
/// A customer as the Accounts domain understands it: who they are and how to
/// reach them. What they bought and what they thought of it are fields on the
/// GraphQL type too, but Ordering and Reviews contribute those from their own
/// folders.
/// </summary>
public sealed record Customer(
    Guid Id,
    string DisplayName,
    string Email);
