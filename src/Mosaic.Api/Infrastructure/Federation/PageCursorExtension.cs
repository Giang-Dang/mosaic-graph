using GreenDonut.Data;
using HotChocolate.ApolloFederation.Types;

namespace Mosaic.Api.Infrastructure.Federation;

/// <summary>
/// Marks <c>PageCursor</c> as shareable, because HotChocolate marks
/// <c>PageInfo</c> and stops there.
/// </summary>
/// <remarks>
/// The identical file lives in <c>Mosaic.Catalog</c>, and the reasoning is
/// there. In short: two subgraphs with connections both declare
/// <c>PageInfo</c> and <c>PageCursor</c>, HotChocolate's federation
/// interceptor auto-marks the first by name and nothing marks the second, and
/// the composer refuses a type two subgraphs define without <c>@shareable</c>
/// on it somewhere.
/// </remarks>
[Shareable]
[ObjectType<PageCursor>]
public static partial class PageCursorExtension;
