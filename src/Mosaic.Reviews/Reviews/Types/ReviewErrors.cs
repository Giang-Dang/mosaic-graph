using Mosaic.Reviews.Errors;
using Mosaic.Reviews.Model;

namespace Mosaic.Reviews.Types;

// The two domain errors submitReview can return, as types on the payload rather
// than entries in the errors array.
//
// Each class carries a static CreateErrorFrom taking the exception it
// represents. That factory is the whole reason these classes exist: pointing
// [Error] straight at an exception works and copies the exception's Message onto
// the payload, which means the message a developer wrote for a log ends up in
// front of a customer, and there is nowhere to put a code.
//
// The type name in the schema is the class name with no rewriting, because these
// are already called ...Error. Point [Error] at an exception instead and
// HotChocolate renames FooException to FooError for you.
//
// There were four before chapter 8 and three before chapter 12.
// ProductNotFoundError went with Catalog and CustomerNotFoundError went with
// Accounts, both because the check behind them did. What is left is the two
// rules Reviews owns outright: a rating is between one and five, and one
// customer reviews one product once. A domain that owns a rule can still raise
// the failure of that rule. A domain that has stopped owning the data cannot,
// and an error type nothing can raise is a branch a client will write and never
// reach.

/// <summary>
/// The rating was not between one and five.
/// </summary>
/// <remarks>
/// This error carries two fields the interface does not require, and they are
/// the argument for typed errors in one place. A client rendering a form can
/// read <c>min</c> and <c>max</c> and correct the input without knowing what
/// Mosaic considers a valid rating; an error in the <c>errors</c> array would
/// have had to encode the same thing in prose and hope somebody parsed it.
/// </remarks>
public sealed class RatingOutOfRangeError : IMosaicError
{
    private RatingOutOfRangeError(string message) => Message = message;

    public string Message { get; }

    public string Code => "RATING_OUT_OF_RANGE";

    public int Min => 1;

    public int Max => 5;

    public static RatingOutOfRangeError CreateErrorFrom(RatingOutOfRangeException exception)
        => new($"A rating has to be between 1 and 5. Got {exception.Rating}.");
}

/// <summary>
/// This customer has already reviewed this product.
/// </summary>
public sealed class DuplicateReviewError : IMosaicError
{
    private DuplicateReviewError(string message) => Message = message;

    public string Message { get; }

    public string Code => "DUPLICATE_REVIEW";

    public static DuplicateReviewError CreateErrorFrom(DuplicateReviewException exception)
        => new("You have already reviewed this product.");
}
