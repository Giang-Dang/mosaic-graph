using HotChocolate.ApolloFederation.Types;

namespace Mosaic.Pricing.Model;

/// <summary>
/// An amount of money in a single currency.
/// </summary>
/// <remarks>
/// <para>
/// The third kind of thing chapter 12 had to duplicate, and the only one the
/// composer inspects field by field. A key format is a string both sides agree
/// to parse the same way. An enum is a list of names. <c>Money</c> is an object
/// type that two subgraphs both declare, and Apollo Federation refuses that
/// outright unless somebody says the fields may be resolved in more than one
/// place.
/// </para>
/// <para>
/// <c>[Shareable]</c> on the class marks every field on the type, which is what
/// is wanted here: <c>amount</c> and <c>currency</c> mean the same thing in
/// both services and either can answer. Ordering carries an identical record
/// with the same attribute, in <c>Mosaic.Ordering.Model</c>. The two are the
/// same GraphQL type because they have the same name and the same fields, and
/// nothing checks that they have the same meaning.
/// </para>
/// <para>
/// It is worth being precise about what <c>@shareable</c> is not. It does not
/// make the two declarations one declaration, and it does not make the router
/// prefer either. It says the graph may take this field from whichever subgraph
/// it is already talking to. Chapter 11's <c>@provides</c> section is the same
/// argument in a different shape: a value two services can both produce is a
/// value two services can disagree about.
/// </para>
/// </remarks>
[Shareable]
public sealed record Money(decimal Amount, string Currency);
