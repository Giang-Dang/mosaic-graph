using GreenDonut.Data;
using HotChocolate.ApolloFederation.Types;

namespace Mosaic.Catalog.Federation;

/// <summary>
/// Marks <c>PageCursor</c> as shareable, because HotChocolate marks
/// <c>PageInfo</c> and stops there.
/// </summary>
/// <remarks>
/// <para>
/// Any two subgraphs that both have a connection declare <c>PageInfo</c> and
/// <c>PageCursor</c> twice, and Apollo Federation refuses to compose an object
/// two subgraphs define unless somebody says it is shareable.
/// <c>FederationTypeInterceptor</c> handles the first of those and its comment
/// says so in as many words - "if we find a PagingInfo we will make all fields
/// sharable" - matching on the type name <c>PageInfo</c> and nothing else.
/// <c>PageCursor</c> is the type <c>PageInfo.forwardCursors</c> returns, so it
/// arrives in the schema through the type the interceptor just fixed, and is
/// left alone.
/// </para>
/// <para>
/// The result is a composition error that names a type neither service wrote.
/// One empty partial class with two attributes on it is the whole fix:
/// <c>@shareable</c> on an object makes every field on it shareable, and
/// <c>[ObjectType&lt;T&gt;]</c> is how this repository extends a type it does
/// not own - the same mechanism Pricing uses on <c>Product</c>.
/// </para>
/// <para>
/// Mosaic.Api carries the identical file. Both subgraphs have connections, so
/// both declare the type, so both have to say it.
/// </para>
/// </remarks>
[Shareable]
[ObjectType<PageCursor>]
public static partial class PageCursorExtension;
