using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using DALib.Abstractions;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
using DALib.Utility;
using SkiaSharp;

namespace DALib.Drawing;

/// <summary>
///     Represents a collection of ground tiles
/// </summary>
public sealed class Tileset : Collection<Tile>, ISavable
{
    /// <summary>
    ///     Initializes a new instance of the Tileset class.
    /// </summary>
    public Tileset() { }

    private Tileset(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Default, true);

        var tileCount = (int)(stream.Length / CONSTANTS.TILE_SIZE);

        for (var i = 0; i < tileCount; i++)
        {
            var tileData = reader.ReadBytes(CONSTANTS.TILE_SIZE);

            Add(
                new Tile
                {
                    Data = tileData
                });
        }
    }

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".bmp"),
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

        for (var i = 0; i < Count; i++)
        {
            var tile = this[i];

            if (tile.Data.Length != CONSTANTS.TILE_SIZE)
                throw new InvalidDataException($"Tile {i} has an invalid size of {tile.Data.Length} bytes");

            writer.Write(tile.Data);
        }
    }
    #endregion

    #region LoadFrom
    /// <summary>
    ///     Converts a sequence of fully colored images to a Tileset
    /// </summary>
    /// <param name="images">
    ///     The sequence of SKImages.
    /// </param>
    /// <remarks>
    ///     This method will take a set of images and convert them to a Tileset. However, in order to save this tileset, you
    ///     will need to load the existing tilesets (tilea and tileas) from the archive and append this tileset to the existing
    ///     tileset. Then you can save that tileset and keep all existing tiles. Ensure the tile ids you add to the
    ///     PaletteTable are based on appending to the existing tileset.
    /// </remarks>
    /// <example>
    ///     <code>
    /// <![CDATA[
    /// // Open new tile image
    /// using var newTile = SKImage.FromEncodedData("newTile.png");
    /// // Open existing archive in memory
    /// using var seo = DataArchive.FromFile("seo.dat", cacheArchive: true);
    /// 
    /// // Get existing palette, paletteTable, and tileset
    /// var existingPaletteLookup = PaletteLookup.FromArchive("mpt", seo);
    /// var existingTileSet = Tileset.FromArchive("tilea", seo);
    /// 
    /// // Convert new tile image to tileset
    /// (var newTileSet, var newPalette) = Tileset.FromImages(newTile);
    /// 
    /// // Store the starting index of the new tileset before we add the new tile
    /// // This will be the starting ID when adding to the PaletteTable
    /// var startingIndex = existingTileSet.Count;
    /// // Need this to calculate the end of the range of tile IDs we're adding to the PaletteTable
    /// var tileCount = newTileSet.Count;
    /// 
    /// // Add the new tiles to the existing tileset
    /// foreach(var tile in newTileSet)
    ///     existingTileSet.Add(tile);
    ///     
    /// // Gets the next available palette ID so we know what to save our new palette as
    /// var newPaletteId = existingPaletteLookup.GetNextPaletteId();
    /// 
    /// // Add entries to the PaletteTable so that the new tiles use the new palette
    /// for(var tileId = startingIndex; tileId < (startingIndex + tileCount); tileId++)
    ///     existingPaletteLookup.Table.Add(newPaletteId, tileId);
    /// 
    /// // Save the new palette to the archive
    /// seo.Patch($"mpt{newPaletteId:D3}.pal", newPalette);
    /// // Replace the existing tileset in the archive
    /// seo.Patch($"tilea.bmp", existingTileSet);
    /// // Replace the existing PaletteTable in the archive
    /// seo.Patch("mptpal.tbl", existingPaletteLookup.Table);
    /// // Save the archive
    /// seo.Save("seo.dat");
    /// ]]>
    /// </code>
    /// </example>
    public static Palettized<Tileset> FromImages(IEnumerable<SKImage> images) => FromImages(images.ToArray());

    /// <summary>
    ///     Converts a collection of fully colored images to a Tileset
    /// </summary>
    /// <param name="images">
    ///     The collection of SKImages.
    /// </param>
    /// <remarks>
    ///     This method will take a set of images and convert them to a Tileset. However, in order to save this tileset, you
    ///     will need to load the existing tilesets (tilea and tileas) from the archive and append this tileset to the existing
    ///     tileset. Then you can save that tileset and keep all existing tiles. Ensure the tile ids you add to the
    ///     PaletteTable are based on appending to the existing tileset.
    /// </remarks>
    /// <example>
    ///     <code>
    /// <![CDATA[
    /// // Open new tile image
    /// using var newTile = SKImage.FromEncodedData("newTile.png");
    /// // Open existing archive in memory
    /// using var seo = DataArchive.FromFile("seo.dat", cacheArchive: true);
    /// 
    /// // Get existing palette, paletteTable, and tileset
    /// var existingPaletteLookup = PaletteLookup.FromArchive("mpt", seo);
    /// var existingTileSet = Tileset.FromArchive("tilea", seo);
    /// 
    /// // Convert new tile image to tileset
    /// (var newTileSet, var newPalette) = Tileset.FromImages(newTile);
    /// 
    /// // Store the starting index of the new tileset before we add the new tile
    /// // This will be the starting ID when adding to the PaletteTable
    /// var startingIndex = existingTileSet.Count;
    /// // Need this to calculate the end of the range of tile IDs we're adding to the PaletteTable
    /// var tileCount = newTileSet.Count;
    /// 
    /// // Add the new tiles to the existing tileset
    /// foreach(var tile in newTileSet)
    ///     existingTileSet.Add(tile);
    ///     
    /// // Gets the next available palette ID so we know what to save our new palette as
    /// var newPaletteId = existingPaletteLookup.GetNextPaletteId();
    /// 
    /// // Add entries to the PaletteTable so that the new tiles use the new palette
    /// for(var tileId = startingIndex; tileId < (startingIndex + tileCount); tileId++)
    ///     existingPaletteLookup.Table.Add(newPaletteId, tileId);
    /// 
    /// // Save the new palette to the archive
    /// seo.Patch($"mpt{newPaletteId:D3}.pal", newPalette);
    /// // Replace the existing tileset in the archive
    /// seo.Patch($"tilea.bmp", existingTileSet);
    /// // Replace the existing PaletteTable in the archive
    /// seo.Patch("mptpal.tbl", existingPaletteLookup.Table);
    /// // Save the archive
    /// seo.Save("seo.dat");
    /// ]]>
    /// </code>
    /// </example>
    /// <exception cref="InvalidDataException">
    ///     Thrown if any of the images has a size different than CONSTANTS.TILE_SIZE.
    /// </exception>
    public static Palettized<Tileset> FromImages(params SKImage[] images)
    {
        if (images.Any(img => (img.Height * img.Width) != CONSTANTS.TILE_SIZE))
            throw new InvalidDataException("All images must be 56x27");

        ImageProcessor.PreserveNonTransparentBlacks(images);

        using var quantized = ImageProcessor.QuantizeMultiple(QuantizerOptions.Default, images);
        (var quantizedImages, var palette) = quantized;

        var tileset = new Tileset();

        foreach (var image in quantizedImages)
            tileset.Add(
                new Tile
                {
                    Data = image.GetPalettizedPixelData(palette)
                });

        return new Palettized<Tileset>
        {
            Entity = tileset,
            Palette = palette
        };
    }

    /// <summary>
    ///     Loads a Tileset with the specified fileName from the specified archive
    /// </summary>
    /// <param name="fileName">
    ///     The name of the Tileset to search for in the archive.
    /// </param>
    /// <param name="archive">
    ///     The DataArchive from which to retreive the Tileset from
    /// </param>
    public static Tileset FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".bmp"), out var entry))
            throw new FileNotFoundException($"BMP file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Loads a Tileset from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the TileSet from
    /// </param>
    public static Tileset FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new Tileset(segment);
    }

    /// <summary>
    ///     Loads an TileSet from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file to be read.
    /// </param>
    public static Tileset FromFile(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".bmp"),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        return new Tileset(stream);
    }
    #endregion
}