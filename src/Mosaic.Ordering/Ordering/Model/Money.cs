using HotChocolate.ApolloFederation.Types;

namespace Mosaic.Ordering.Model;

/// <summary>
/// An amount of money in a single currency.
/// </summary>
/// <remarks>
/// <para>
/// Pricing's record, copied. It lived in <c>Pricing/Model</c> inside the
/// monolith and Ordering referenced it across a folder boundary, which was free
/// then and is a project reference between two teams now.
/// </para>
/// <para>
/// <c>[Shareable]</c> is what lets both subgraphs declare the type. Without it
/// on both sides the composer reports every field of <c>Money</c> as defined in
/// two subgraphs without <c>@shareable</c> in either, and names the type rather
/// than the two services that disagree.
/// </para>
/// <para>
/// Ordering's use of it is not Pricing's, and that is the interesting part. A
/// price is what a product costs now, and Pricing owns it. A line's unit price
/// is what a customer paid on the day, and Ordering owns that and always did -
/// <c>OrderLine.unitPrice</c> was never a lookup. Two services that share a
/// value type do not necessarily share anything else, and the composer has no
/// way to know which of those two situations it is looking at.
/// </para>
/// </remarks>
[Shareable]
public sealed record Money(decimal Amount, string Currency);
