namespace Mosaic.Api.Infrastructure;

/// <summary>
/// Knobs on Mosaic's stand-in data layer.
/// </summary>
public sealed class MosaicDataOptions
{
    /// <summary>
    /// Artificial cost of a single lookup. Zero while the data lives in memory,
    /// which is the whole reason the naive lookups in this version of Mosaic
    /// feel free.
    /// </summary>
    public TimeSpan LookupDelay { get; set; } = TimeSpan.Zero;
}
