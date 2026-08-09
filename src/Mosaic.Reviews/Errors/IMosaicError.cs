namespace Mosaic.Reviews.Errors;

/// <summary>
/// The interface every domain error on a mutation payload implements.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate's default error interface is called <c>Error</c> and carries a
/// single field, <c>message</c>. Mosaic replaces it with this one, which is the
/// same plus a <c>code</c>, and keeps the name so that nothing else about the
/// generated payloads changes.
/// </para>
/// <para>
/// The code is the reason this interface exists. A message is written for a
/// person and gets reworded: it is translated, shortened, or made friendlier,
/// and any client that branched on its text breaks quietly when it is. A code
/// is written for a program and is part of the contract. Reword the message
/// freely; change the code and you have made a breaking change on purpose.
/// </para>
/// <para>
/// The code is a <c>String</c> rather than an enum, which is the less obvious
/// choice. An enum would be checked at build time and would let a client switch
/// exhaustively, but the moment a new domain error is added, every exhaustive
/// client has a case it has never seen. A string means a client that does not
/// recognise a code falls back to displaying the message, which is what it
/// should do anyway.
/// </para>
/// <para>
/// Registered with <c>AddErrorInterfaceType&lt;IMosaicError&gt;()</c>. Error
/// classes do not have to implement this interface in C# - HotChocolate only
/// requires that they carry matching properties - but Mosaic's do, so the
/// compiler catches an error type that forgets its code.
/// </para>
/// </remarks>
[GraphQLName("Error")]
public interface IMosaicError
{
    /// <summary>What went wrong, in a sentence meant for a person.</summary>
    string Message { get; }

    /// <summary>
    /// A stable, machine-readable name for this failure. Screaming snake case,
    /// like the error codes GraphQL itself puts in <c>extensions.code</c>.
    /// </summary>
    string Code { get; }
}
