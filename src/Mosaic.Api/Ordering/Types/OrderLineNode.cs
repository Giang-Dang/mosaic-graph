using Mosaic.Api.Catalog.Model;
using Mosaic.Api.Ordering.Model;

namespace Mosaic.Api.Ordering.Types;

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
    /// this service. A dangling identifier now surfaces as a null product at
    /// the router, one hop later and one service further away.
    /// </para>
    /// <para>
    /// It is also worth being clear about what it buys. This is no longer a
    /// resolver that can be slow: no DataLoader, no batch, no statement. The
    /// order query got cheaper here and more expensive somewhere else, and
    /// chapter 10 is where both halves are visible at once.
    /// </para>
    /// </remarks>
    public static Product GetProduct([Parent] OrderLine line)
        => new() { Id = line.ProductId };
}
