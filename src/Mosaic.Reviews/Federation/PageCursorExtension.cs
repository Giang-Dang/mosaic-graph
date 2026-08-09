using GreenDonut.Data;
using HotChocolate.ApolloFederation.Types;

namespace Mosaic.Reviews.Federation;

/// <summary>
/// Marks <c>PageCursor</c> as shareable, because HotChocolate marks
/// <c>PageInfo</c> and stops there.
/// </summary>
/// <remarks>
/// <para>
/// Any two subgraphs that both have a connection declare <c>PageInfo</c> and
/// <c>PageCursor</c> twice, and Apollo Federation refuses to compose an object
/// two subgraphs define unless somebody says it is shareable.
/// <c>FederationTypeInterceptor</c> handles the first of those, matching on the
/// type name <c>PageInfo</c> and nothing else. <c>PageCursor</c> is the type
/// <c>PageInfo.forwardCursors</c> returns, so it arrives in the schema through
/// the type the interceptor just fixed, and is left alone.
/// </para>
/// <para>
/// The identical file lives in <c>Mosaic.Catalog</c>. Two of the six subgraphs
/// have a connection - <c>Query.browseProducts</c> there and
/// <c>Product.reviews</c> here - so two of them carry this, and the other four
/// do not need it and would not compose any differently if they did.
/// </para>
/// </remarks>
[Shareable]
[ObjectType<PageCursor>]
public static partial class PageCursorExtension;
