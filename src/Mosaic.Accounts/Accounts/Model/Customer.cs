using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Resolvers;
using Mosaic.Accounts.Data;

namespace Mosaic.Accounts.Model;

/// <summary>
/// A customer as the Accounts domain understands it: who they are and how to
/// reach them.
/// </summary>
/// <remarks>
/// <para>
/// <c>Customer</c> became an entity in chapter 12, and it is the second one in
/// this book after <c>Product</c>. Until then Reviews and Ordering reached a
/// customer by calling a DataLoader in the same process, so nothing had to be
/// declared: the type was an ordinary object in one schema. Splitting the three
/// apart turned two method calls into two references across a network, and a
/// reference across a network needs a key.
/// </para>
/// <para>
/// The <c>@key</c> is the same shape Catalog's is - chapter 5's global object
/// identifier, base64, with the type name inside it - because a second key
/// format would be a second thing every team has to learn.
/// <see cref="CustomerKey"/> decodes it, and is a copy of Catalog's
/// <c>ProductKey</c> with one identifier changed.
/// </para>
/// <para>
/// <c>[Key]</c> and <c>[ReferenceResolver]</c> go here, on the runtime type,
/// rather than on the <c>[ObjectType&lt;Customer&gt;]</c> class in
/// <c>Types/</c>. Chapter 8 measured what the other placement does: the source
/// generator turns the method into an ordinary public field, no reference
/// resolver is registered, and <c>_entities</c> answers
/// <c>Unexpected Execution Error</c> with nothing warning about it.
/// </para>
/// </remarks>
[Key("id")]
public sealed record Customer(
    Guid Id,
    string DisplayName,
    string Email)
{
    /// <summary>
    /// Answers one representation: given the key Reviews or Ordering is
    /// holding, hand back the customer it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Behind the same DataLoader every other route into a customer uses, so a
    /// page of a hundred and twenty reviews whose authors are twelve people
    /// costs one statement. That property was true inside the monolith too;
    /// what changed is that the batch now forms out of a list of
    /// representations rather than out of a hundred and twenty resolver calls.
    /// </para>
    /// <para>
    /// Returns null rather than throwing for a key it cannot read. A key this
    /// service cannot decode is genuinely not one of its customers, and the
    /// specification's nullable <c>[_Entity]</c> exists for exactly that.
    /// </para>
    /// </remarks>
    [ReferenceResolver]
    public static async Task<Customer?> ResolveReferenceAsync(
        string id,
        IResolverContext context,
        ICustomerByIdDataLoader customerById,
        CancellationToken cancellationToken)
        => CustomerKey.TryDecode(id, context, out var customerId)
            ? await customerById.LoadAsync(customerId, cancellationToken)
            : null;
}
