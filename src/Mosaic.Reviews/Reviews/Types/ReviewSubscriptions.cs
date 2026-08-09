using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Mosaic.Reviews.Catalog.Model;
using Mosaic.Reviews.Model;

namespace Mosaic.Reviews.Types;

/// <summary>
/// The topic names both ends of a subscription have to agree on.
/// </summary>
/// <remarks>
/// One function, called by the publisher and by the subscribe resolver, so the
/// two cannot drift. The alternative HotChocolate offers is a placeholder in a
/// <c>[Topic]</c> attribute, which reads better and leaves the exact string an
/// argument value formats into as something you find out by running it. A topic
/// is a contract between two pieces of code; this makes it one line of C# that
/// both of them call.
/// </remarks>
public static class ReviewTopics
{
    public static string ReviewAdded(Guid productId) => $"reviewAdded:{productId}";
}

/// <summary>
/// Mosaic's only subscription: reviews of one product, as they are written.
/// </summary>
/// <remarks>
/// <para>
/// One service, one process, and an in-memory pub/sub. Everything here works
/// because the publisher and the subscriber are the same process, which is a
/// property that survives exactly as long as there is one instance of Mosaic.
/// Chapter 14 is where this stops being true.
/// </para>
/// <para>
/// Note what a subscription is not. It is not a query that repeats: the client
/// gets one result per event, the event decides when, and the selection set is
/// applied to each result as it goes.
/// </para>
/// </remarks>
[SubscriptionType]
public static partial class ReviewSubscriptions
{
    /// <summary>
    /// Subscribes to reviews of one product.
    /// </summary>
    /// <remarks>
    /// The subscribe resolver runs once, when the client subscribes, and its
    /// job is to produce the stream. It sees the field's arguments like any
    /// other resolver, which is what makes a per-product topic possible without
    /// a placeholder.
    /// </remarks>
    public static ValueTask<ISourceStream<Review>> SubscribeToReviewsAsync(
        [ID<Product>] Guid productId,
        ITopicEventReceiver receiver,
        CancellationToken cancellationToken)
        => receiver.SubscribeAsync<Review>(
            ReviewTopics.ReviewAdded(productId),
            cancellationToken);

    /// <summary>A review, the moment it is written.</summary>
    /// <remarks>
    /// This method runs once per event rather than once per subscription, and
    /// it receives the message the mutation published. Returning it unchanged
    /// is the common case; the hook exists so that a payload can be reshaped or
    /// an event dropped before it reaches the client.
    /// </remarks>
    [Subscribe(With = nameof(SubscribeToReviewsAsync))]
    public static Review OnReviewAdded(
        [ID<Product>] Guid productId,
        [EventMessage] Review review)
        => review;
}
