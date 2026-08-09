using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Reviews.Accounts.Model;

/// <summary>
/// A customer, as Reviews knows one: an identifier it stored and can hand back.
/// </summary>
/// <remarks>
/// <para>
/// Accounts owns <c>Customer</c> and holds the name and the address.
/// <c>Review.author</c> returns one of these, which is a key in a wrapper:
/// the router reads the identifier, calls Accounts with a representation, and
/// fills in everything the client actually asked for.
/// </para>
/// <para>
/// <c>resolvable: false</c> is the declaration that makes that true in both
/// directions. Nothing in this service can turn a customer key into a customer,
/// so the key exists to be referenced and never to be resolved, and the flag is
/// what stops the router from ever routing one here. Chapter 11 met the flag
/// twice as the cause of a broken graph and once, on the crates sample, used
/// honestly. This is the honest use, four times over across the six subgraphs.
/// </para>
/// <para>
/// What the old code did instead is worth keeping in view. Until chapter 12
/// this field called a DataLoader, got a whole <c>Customer</c> out of the same
/// database, and threw if there was not one. It could tell you that a review
/// named an author who does not exist. Nothing in this service can tell you
/// that now.
/// </para>
/// </remarks>
[Key("id", resolvable: false)]
public sealed class Customer
{
    /// <summary>The customer's global identifier, and the federation key.</summary>
    [ID]
    public required Guid Id { get; init; }
}