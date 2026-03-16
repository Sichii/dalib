using System;

namespace DALib.Definitions;

/// <summary>
///     Tils flags as used in sotp.dat
/// </summary>
[Flags]
public enum TileFlags : byte
{
    /// <summary>
    ///     Tile is a normal tile
    /// </summary>
    None = 0,

    /// <summary>
    ///     Tile is a wall
    /// </summary>
    Wall = 15,

    /// <summary>
    ///     Tile uses screen blend compositing (output = src + dst * (1 - src) per channel). The client renders these with
    ///     mode 0x6D, where each color channel's value acts as its own alpha. Black pixels are fully transparent, white
    ///     pixels fully opaque
    /// </summary>
    Transparent = 128
}