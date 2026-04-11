#region
using System.IO;
using System.Text;
using DALib.Abstractions;
using DALib.Drawing;
using DALib.Extensions;
#endregion

namespace DALib.Data;

/// <summary>
///     Represents a map file with a grid of tiles.
/// </summary>
public sealed class MapFile(int width, int height) : ISavable
{
    /// <summary>
    ///     The height of the map in tiles
    /// </summary>
    public int Height { get; } = height;

    /// <summary>
    ///     A 2d array of tiles contained within the map
    /// </summary>
    public MapTile[,] Tiles { get; } = new MapTile[width, height];

    /// <summary>
    ///     The width of the map in tiles
    /// </summary>
    public int Width { get; } = width;

    private MapFile(Stream stream, int width, int height)
        : this(width, height)
    {
        if (stream.Length != (width * height * 6))
            throw new InvalidDataException("Invalid map file");

        using var reader = new BinaryReader(stream, Encoding.Default, true);

        for (var y = 0; y < Height; ++y)
            for (var x = 0; x < Width; ++x)
            {
                var background = reader.ReadInt16();
                var leftForeground = reader.ReadInt16();
                var rightForeground = reader.ReadInt16();

                Tiles[x, y] = new MapTile
                {
                    Background = background,
                    LeftForeground = leftForeground,
                    RightForeground = rightForeground
                };
            }
    }

    #region LoadFrom
    /// <summary>
    ///     Loads a MapFile from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path to the file to read.
    /// </param>
    /// <param name="width">
    ///     The width of the map.
    /// </param>
    /// <param name="height">
    ///     The height of the map.
    /// </param>
    /// <returns>
    ///     A new instance of the MapFile class.
    /// </returns>
    public static MapFile FromFile(string path, int width, int height)
    {
        using var stream = File.Open(
            path.WithExtension(".map"),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        return new MapFile(stream, width, height);
    }
    #endregion

    /// <summary>
    ///     Gets the MapTile at the specified coordinates.
    /// </summary>
    /// <param name="x">
    ///     The x-coordinate of the MapTile.
    /// </param>
    /// <param name="y">
    ///     The y-coordinate of the MapTile.
    /// </param>
    /// <returns>
    ///     The MapTile at the specified coordinates.
    /// </returns>
    public MapTile this[int x, int y] => Tiles[x, y];

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".map"),
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        Save(stream);
    }

    /// <inheritdoc />
    public void Save(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.Default, true);

        for (var y = 0; y < Height; ++y)
            for (var x = 0; x < Width; ++x)
            {
                var tile = Tiles[x, y];

                writer.Write(tile.Background);
                writer.Write(tile.LeftForeground);
                writer.Write(tile.RightForeground);
            }
    }
    #endregion
}

/// <summary>
///     Represents a map tile with background and foreground layers.
/// </summary>
public sealed class MapTile
{
    /// <summary>
    ///     The id of the background part of the tile. This id references a <see cref="Tile" /> from a <see cref="Tileset" />
    ///     loaded from Seo.dat
    /// </summary>
    public short Background { get; init; }

    /// <summary>
    ///     The id of the left foreground part of the tile. This id references an HPF image loaded from ia.dat
    /// </summary>
    public short LeftForeground { get; set; }

    /// <summary>
    ///     The id of the right foreground part of the tile. This id references an HPF image loaded from ia.dat
    /// </summary>
    public short RightForeground { get; set; }
}