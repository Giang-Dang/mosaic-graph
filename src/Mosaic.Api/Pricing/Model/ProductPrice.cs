namespace Mosaic.Api.Pricing.Model;

/// <summary>
/// What one product costs. The product is referred to by its identifier only:
/// Pricing never holds a reference to a Catalog object.
/// </summary>
public sealed record ProductPrice(Guid ProductId, Money Amount);
