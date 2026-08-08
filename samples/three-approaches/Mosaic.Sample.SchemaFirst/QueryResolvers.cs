namespace Mosaic.Sample.SchemaFirst;

/// <summary>
/// Backs the Query type declared in the SDL. Methods are matched to fields by
/// name with the Get prefix dropped, so GetProductById answers productById.
/// </summary>
/// <remarks>
/// A resolver class rather than an AddResolver delegate, for a reason worth
/// knowing: HotChocolate compiles this method and can see it is synchronous and
/// takes nothing but field arguments, so it counts as a pure resolver and the
/// cost analyzer leaves it alone. A delegate is opaque to that analysis, gets
/// the default resolver cost of 10, and the exported SDL then carries a
/// @cost(weight: "10") that the other two samples do not have.
/// </remarks>
public sealed class QueryResolvers
{
    public Product? GetProductById(Guid id) => Catalog.ById(id);
}
