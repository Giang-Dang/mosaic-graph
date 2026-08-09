using Mosaic.Ordering.Catalog.Model;
using Mosaic.Ordering.Model;

namespace Mosaic.Ordering.Types;

/// <summary>
/// Ordering's own view of the <c>OrderLine</c> type. The line stores a product
/// identifier; this class turns it into a <c>Product</c>.
/// </summary>
[ObjectType<OrderLine>]
public static partial class OrderLineNode
{
    /// <summary>The product this line sold.</summary>
    /// <remarks>
    /// <para>
    /// Until chapter 8 this method took a DataLoader, asked Catalog for the
    /// row, and threw if it was not there. Now it returns a key in a wrapper.
    /// Everything a client asks for beyond the identifier - the title, the sku,
    /// the category - is answered by the other service, reached through a
    /// representation the router builds out of this object's <c>id</c>.
    /// </para>
    /// <para>
    /// It is worth being clear about what that costs. The old version could
    /// tell you that a line sold a product the catalog has never heard of,
    /// because it looked. This one cannot, and neither can anything else in
    /// this service. A dangling identifier is now Catalog's problem, one hop
    /// later and one service further away.
    /// </para>
    /// <para>
    /// Nullable since chapter 12, and it was <c>Product!</c> for four chapters
    /// before that. Chapter 11 measured what a non-null reference does when the
    /// entity behind it cannot be resolved: the null propagates outwards through
    /// <c>lines: [OrderLine!]!</c> and takes the whole response with it, so one
    /// line pointing at a deleted product costs a client its entire order
    /// history. The only reason it never bit is that the seeder builds every
    /// line from a product it just wrote, and a guarantee that holds because of
    /// how the test data was made is not a guarantee.
    /// </para>
    /// <para>
    /// It is also worth being clear about what the change buys. This is no
    /// longer a resolver that can be slow: no DataLoader, no batch, no
    /// statement. The order query got cheaper here and more expensive somewhere
    /// else, and chapter 10 is where both halves are visible at once.
    /// </para>
    /// </remarks>
    public static Product? GetProduct([Parent] OrderLine line)
        => new() { Id = line.ProductId };
}
