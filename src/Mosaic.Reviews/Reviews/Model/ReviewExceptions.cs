namespace Mosaic.Reviews.Model;

/// <summary>
/// The two ways submitting a review is refused for a reason the caller can do
/// something about.
/// </summary>
/// <remarks>
/// <para>
/// These are exceptions because that is the shape the domain service can throw
/// from wherever the rule is checked, and because a rule violation really is
/// exceptional relative to the method's job. They are not what the client sees:
/// each one is mapped to an error type on the mutation payload, so a caller
/// gets data rather than an entry in the <c>errors</c> array.
/// </para>
/// <para>
/// Nothing outside Reviews throws these. A domain that owns a rule owns the
/// failure of that rule, and chapter 12 is where that sentence started cutting
/// both ways: <c>CustomerNotFoundException</c> is gone because Reviews stopped
/// owning the rule the moment it stopped owning the customers.
/// </para>
/// </remarks>
public abstract class ReviewSubmissionException(string message) : Exception(message);

/// <summary>
/// Thrown when the rating is outside one to five.
/// </summary>
/// <remarks>
/// This one is worth pausing on. GraphQL's type system can say that a rating is
/// an <c>Int!</c> and cannot say that it is between one and five, so a range is
/// not something validation can reject before a resolver runs. Every constraint
/// the type system cannot express has to live somewhere, and the choice is
/// between an error the client can read and one it cannot.
/// </remarks>
public sealed class RatingOutOfRangeException(int rating)
    : ReviewSubmissionException($"A rating has to be between 1 and 5. Got {rating}.")
{
    public int Rating { get; } = rating;
}

/// <summary>
/// Thrown when this customer has already reviewed this product.
/// </summary>
public sealed class DuplicateReviewException(Guid productId, Guid customerId)
    : ReviewSubmissionException("This customer has already reviewed this product.")
{
    public Guid ProductId { get; } = productId;

    public Guid CustomerId { get; } = customerId;
}
