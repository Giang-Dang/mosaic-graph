using Mosaic.Api.Infrastructure.Errors;
using Mosaic.Api.Reviews.Model;

namespace Mosaic.Api.Reviews.Types;

/// <summary>
/// The four domain errors <c>submitReview</c> can return, as types on the
/// payload rather than entries in the <c>errors</c> array.
/// </summary>
/// <remarks>
/// <para>
/// Each class carries a static <c>CreateErrorFrom</c> taking the exception it
/// represents. That factory is the whole reason these classes exist: pointing
/// <c>[Error]</c> straight at an exception works and copies the exception's
/// <c>Message</c> onto the payload, which means the message a developer wrote
/// for a log ends up in front of a customer, and there is nowhere to put a code.
/// </para>
/// <para>
/// The type name in the schema is the class name with no rewriting, because
/// these are already called <c>...Error</c>. Point <c>[Error]</c> at an
/// exception instead and HotChocolate renames <c>FooException</c> to
/// <c>FooError</c> for you.
/// </para>
/// </remarks>
public sealed class ProductNotFoundError : IMosaicError
{
    private ProductNotFoundError(string message) => Message = message;

    public string Message { get; }

    public string Code => "PRODUCT_NOT_FOUND";

    public static ProductNotFoundError CreateErrorFrom(ProductNotFoundException exception)
        => new($"No product was found with id {exception.ProductId}.");
}

/// <inheritdoc cref="ProductNotFoundError"/>
public sealed class CustomerNotFoundError : IMosaicError
{
    private CustomerNotFoundError(string message) => Message = message;

    public string Message { get; }

    public string Code => "CUSTOMER_NOT_FOUND";

    public static CustomerNotFoundError CreateErrorFrom(CustomerNotFoundException exception)
        => new($"No customer was found with id {exception.CustomerId}.");
}

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

/// <inheritdoc cref="ProductNotFoundError"/>
public sealed class DuplicateReviewError : IMosaicError
{
    private DuplicateReviewError(string message) => Message = message;

    public string Message { get; }

    public string Code => "DUPLICATE_REVIEW";

    public static DuplicateReviewError CreateErrorFrom(DuplicateReviewException exception)
        => new("You have already reviewed this product.");
}
