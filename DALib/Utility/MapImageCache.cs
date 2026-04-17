using System;

namespace DALib.Utility;

/// <summary>
///     A collection of <see cref="SKImageCache{TKey}" /> caches that can be used as a shared cache for rendering multiple
///     maps.
/// </summary>
public sealed class MapImageCache : IDisposable
{
    /// <summary>
    ///     The background cache
    /// </summary>
    public SKImageCache<int> BackgroundCache { get; }

    /// <summary>
    ///     The left foreground cache
    /// </summary>
    public SKImageCache<int> ForegroundCache { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="MapImageCache" /> class.
    /// </summary>
    public MapImageCache()
    {
        BackgroundCache = new SKImageCache<int>();
        ForegroundCache = new SKImageCache<int>();
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="MapImageCache" /> class using the specified caches.
    /// </summary>
    /// <param name="bgCache">
    ///     The background cache
    /// </param>
    /// <param name="fgCache">
    ///     The left foreground cache
    /// </param>
    public MapImageCache(SKImageCache<int> bgCache, SKImageCache<int> fgCache)
    {
        BackgroundCache = bgCache;
        ForegroundCache = fgCache;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        BackgroundCache.Dispose();
        ForegroundCache.Dispose();
    }
}