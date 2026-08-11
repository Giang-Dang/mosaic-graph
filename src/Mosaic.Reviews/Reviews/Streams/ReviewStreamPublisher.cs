using System.Text.Json;
using HotChocolate.Resolvers;
using HotChocolate.Types.Relay;
using NATS.Client.Core;
using Mosaic.Reviews.Catalog.Model;
using Mosaic.Reviews.Model;

namespace Mosaic.Reviews.Streams;

/// <summary>
/// Announces an accepted review on NATS, for the benefit of a subscription this
/// service does not implement.
/// </summary>
/// <remarks>
/// <para>
/// Chapter 5's subscription and this one answer the same question and are built
/// the opposite way round. <c>onReviewAdded</c> is a field of this service: a
/// client's subscription reaches the router, the router holds a WebSocket open
/// to this process for as long as the client is listening, and the payload is
/// whatever the subscribe resolver yields. <c>reviewPublished</c> is a field of
/// no service at all. It is declared in <c>schema/streams.graphql</c>, which has
/// no project beside it, and the router subscribes to NATS on the client's
/// behalf. This class is the only thing Mosaic contributes to that arrangement,
/// and it is a publisher rather than a server.
/// </para>
/// <para>
/// What goes on the wire is deliberately almost nothing. The router treats the
/// message body as a federation representation, so it needs a type name and a
/// key and it will fetch everything else through <c>_entities</c> like any other
/// field. Publishing the whole review would be publishing a second copy of a
/// schema this service already owns, and it would go stale the first time a
/// field changed.
/// </para>
/// <para>
/// The <c>__typename</c> is not optional and its absence is not reported. A
/// message carrying only <c>id</c> reaches the router, matches the subject, and
/// fails at the client as a non-null violation on whichever field the client
/// happened to select. Nothing in the router log, the subgraph log or the error
/// mentions the missing type name.
/// </para>
/// </remarks>
public sealed class ReviewStreamPublisher(
    INatsConnection nats,
    ILogger<ReviewStreamPublisher> logger)
{
    /// <summary>
    /// The subject one product's reviews are announced on.
    /// </summary>
    /// <remarks>
    /// This is the counterpart of <see cref="Types.ReviewTopics.ReviewAdded"/>
    /// and it is not the same string, because the two ends are not the same
    /// pair. The in-memory topic is agreed between two methods in this file's
    /// own project. This subject is agreed between this class and a template in
    /// a schema file that no compiler reads:
    /// <c>@edfs__natsSubscribe(subjects: ["mosaic.reviewAdded.{{ args.productId }}"])</c>.
    /// Chapter 13's rule about shared types applies to shared strings too, and
    /// the only thing holding these two together is that both are asserted by
    /// <c>scripts/realtime-cases.mjs</c>.
    /// </remarks>
    public const string SubjectPrefix = "mosaic.reviewAdded.";

    public async Task PublishAsync(
        Review review,
        Guid productId,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        // The subject carries the product identifier in the form a client
        // writes it, because the client is what fills in the template: the
        // argument the router interpolates is whatever arrived in the
        // subscription document, and that is a global object identifier rather
        // than a Guid. So the publisher has to encode what every other part of
        // this service decodes.
        var accessor = context.Schema.Services?.GetService(typeof(INodeIdSerializerAccessor))
            as INodeIdSerializerAccessor;

        if (accessor is null)
        {
            logger.LogWarning("No node id serialiser; review {ReviewId} was not announced.", review.Id);
            return;
        }

        var subject = SubjectPrefix + accessor.Serializer.Format(nameof(Product), productId);

        // A representation and nothing else. The router adds no type name of
        // its own and checks nothing about this body.
        var body = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["__typename"] = nameof(Review),
            ["id"] = accessor.Serializer.Format(nameof(Review), review.Id),
        });

        try
        {
            await nats.PublishAsync(subject, body, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // A broker that is down must not fail a write that has already
            // committed. The review exists; what is lost is the announcement,
            // and a client holding the in-process subscription still gets it.
            // Saying so here is the difference between a missing event and a
            // silently missing event.
            logger.LogWarning(ex, "Could not announce review {ReviewId} on {Subject}.", review.Id, subject);
        }
    }
}
