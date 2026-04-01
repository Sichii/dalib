namespace DALib.Extensions;

/// <summary>
///     Provides extension methods for the <see cref="int" /> struct
/// </summary>
public static class IntExtensions
{
    /// <summary>
    ///     Determined if the tile index is rendered by the client
    /// </summary>
    /// <param name="tileIndex">
    ///     The tile index to check
    /// </param>
    /// <returns>
    ///     <c>
    ///         true
    ///     </c>
    ///     if the tile index is rendered by the client; otherwise,
    ///     <c>
    ///         false
    ///     </c>
    ///     .
    /// </returns>
    /// <remarks>
    ///     0-12 and 10000-10012 are not rendered by the client... 20000-20012 are rendered but are kinda buggy if you get
    ///     close to them
    /// </remarks>
    public static bool IsRenderedTileIndex(this ushort tileIndex) => (tileIndex > 10012) || ((tileIndex % 10000) > 12);
}