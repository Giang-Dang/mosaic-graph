namespace Mosaic.Api.Pricing.Model;

/// <summary>
/// What one product costs. The product is referred to by its identifier only:
/// Pricing never holds a reference to a Catalog object.
/// </summary>
/// <remarks>
/// The amount sits outside the primary constructor because Entity Framework
/// Core will not bind it there. Its error names navigations, but the rule is
/// wider than that: measured against EF Core 10.0.10, neither an owned
/// reference nor a complex property can be a constructor parameter. Only
/// scalars can. An <c>init</c> property is set through the backing field
/// instead, which EF Core is happy to do.
/// </remarks>
public sealed record ProductPrice(Guid ProductId)
{
    /// <summary>The price, as a value rather than a row of its own.</summary>
    public required Money Amount { get; init; }
}
