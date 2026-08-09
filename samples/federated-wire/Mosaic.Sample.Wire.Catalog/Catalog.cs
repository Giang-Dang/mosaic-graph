using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Types.Relay;

namespace Mosaic.Sample.Wire.Catalog;

/// <summary>
/// A product, as the Catalog subgraph knows it. Three products and three
/// fields: the point of this sample is the traffic between the router and the
/// subgraphs, and every row printed in the book has to fit on a page.
/// </summary>
[Key("id")]
public sealed class Product
{
    [ID]
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required decimal Price { get; init; }

    /// <summary>
    /// The reference resolver. The router calls this through
    /// <c>Query._entities</c> whenever it holds a representation of a
    /// <c>Product</c> and needs Catalog's fields for it. The parameter name
    /// matches the key field, which is how the representation is unpacked.
    /// </summary>
    [ReferenceResolver]
    public static Product? ResolveById(string id)
        => CatalogData.Products.FirstOrDefault(p => p.Id == id);
}

public static class CatalogData
{
    public static readonly IReadOnlyList<Product> Products =
    [
        new() { Id = "1", Title = "Oak dining table", Price = 749.00m },
        new() { Id = "2", Title = "Linen armchair", Price = 429.00m },
        new() { Id = "3", Title = "Brass floor lamp", Price = 189.00m }
    ];
}

[QueryType]
public static partial class CatalogQueries
{
    /// <summary>
    /// The entry point every federated query in chapter 7 starts from.
    /// </summary>
    public static IReadOnlyList<Product> GetProducts()
        => CatalogData.Products;

    /// <summary>
    /// A single product by key. Not used by the router, which reaches products
    /// through <c>_entities</c>; it is here so the subgraph is usable on its
    /// own, which is how the chapter tests it before a router exists.
    /// </summary>
    public static Product? GetProductById([ID] string id)
        => CatalogData.Products.FirstOrDefault(p => p.Id == id);
}
