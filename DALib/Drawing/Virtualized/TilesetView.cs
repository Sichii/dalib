#region
using System;
using System.IO;
using System.Text;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
#endregion

namespace DALib.Drawing.Virtualized;

/// <summary>
///     A lightweight view over a tileset file in a DataArchive. Stores only the entry reference and tile count; individual
///     tile pixel data is read on demand from the underlying memory-mapped archive entry.
/// </summary>
public sealed class TilesetView
{
    private readonly DataArchiveEntry Entry;

    /// <summary>
    ///     The number of tiles in the tileset
    /// </summary>
    public int Count { get; }

    private TilesetView(DataArchiveEntry entry, int count)
    {
        Entry = entry;
        Count = count;
    }

    /// <summary>
    ///     Creates a TilesetView with the specified fileName from the specified archive
    /// </summary>
    /// <param name="fileName">
    ///     The name of the Tileset to search for in the archive.
    /// </param>
    /// <param name="archive">
    ///     The DataArchive from which to retreive the Tileset from
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if the BMP file with the specified name is not found in the archive.
    /// </exception>
    public static TilesetView FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".bmp"), out var entry))
            throw new FileNotFoundException($"BMP file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Creates a TilesetView from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the TilesetView from
    /// </param>
    public static TilesetView FromEntry(DataArchiveEntry entry) => new(entry, entry.FileSize / CONSTANTS.TILE_SIZE);

    /// <summary>
    ///     Reads and returns the tile at the specified index. Pixel data is read from the archive on each access.
    /// </summary>
    public Tile this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            using var stream = Entry.ToStreamSegment();
            using var reader = new BinaryReader(stream, Encoding.Default, true);

            stream.Seek((long)index * CONSTANTS.TILE_SIZE, SeekOrigin.Begin);

            return new Tile
            {
                Data = reader.ReadBytes(CONSTANTS.TILE_SIZE)
            };
        }
    }

    /// <summary>
    ///     Attempts to read the tile at the specified index. Returns false if the index is out of range.
    /// </summary>
    public bool TryGetValue(int index, out Tile? tile)
    {
        if ((index < 0) || (index >= Count))
        {
            tile = null;

            return false;
        }

        tile = this[index];

        return true;
    }
}