namespace Mosaic.Api.Pricing.Model;

/// <summary>
/// An amount of money in a single currency.
/// </summary>
public sealed record Money(decimal Amount, string Currency);
