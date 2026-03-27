#region
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using DALib.Abstractions;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
using SkiaSharp;
#endregion

namespace DALib.Drawing;

/// <summary>
///     Represents a palette of 256 colors
/// </summary>
public sealed class Palette : Collection<SKColor>, ISavable
{
    private Palette(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Default, true);

        for (var i = 0; i < CONSTANTS.COLORS_PER_PALETTE; ++i)
            Add(new SKColor(reader.ReadByte(), reader.ReadByte(), reader.ReadByte()));
    }

    /// <summary>
    ///     Initializes a new instance of the Palette class with 256 transparent-black colors
    /// </summary>
    public Palette()
        : base(
            Enumerable.Repeat(SKColors.Transparent, CONSTANTS.COLORS_PER_PALETTE)
                      .ToList()) { }

    /// <summary>
    ///     Initializes a new instance of the Palette class with the specified colors
    /// </summary>
    /// <param name="colors">
    ///     The colors of the palette
    /// </param>
    public Palette(IEnumerable<SKColor> colors)
        : this()
    {
        var index = 0;

        foreach (var color in colors)
            this[index++] = color;
    }

    /// <summary>
    ///     Creates a new palette with colors cycled within the specified index range based on the given stage
    /// </summary>
    /// <param name="startIndex">
    ///     The starting index of the color range to cycle
    /// </param>
    /// <param name="endIndex">
    ///     The ending index of the color range to cycle (inclusive)
    /// </param>
    /// <param name="stage">
    ///     The stage of the cycling period. Will be modulo by the range length
    /// </param>
    /// <return>
    ///     A new Palette instance with the cycled colors
    /// </return>
    public Palette Cycle(byte startIndex, byte endIndex, int stage = 0)
    {
        var cycledPalette = new Palette(this);
        var rangeLength = endIndex - startIndex + 1;
        var offset = stage % rangeLength;

        for (var i = 0; i < rangeLength; i++)
        {
            var sourceIndex = startIndex + (i - offset + rangeLength) % rangeLength;
            cycledPalette[startIndex + i] = this[sourceIndex];
        }

        return cycledPalette;
    }

    /// <summary>
    ///     Applies a dye to this palette beginning at the specified color index, and continuing for the length of the color
    ///     table entry
    /// </summary>
    /// <param name="colorTableEntry">
    ///     The color table entry to apply to this palette
    /// </param>
    /// <param name="dyeIndexStart">
    ///     The index to start copying colors to from the color table entry
    /// </param>
    /// <remarks>
    ///     This will create a new instance of the palette and return it. The existing palette instance will not be modified.
    ///     For the most part, this method is used to apply dye table entries to a palette, in which case the index start will
    ///     be 98, and the table entry will contain 6 colors
    /// </remarks>
    public Palette Dye(ColorTableEntry colorTableEntry, int dyeIndexStart = CONSTANTS.PALETTE_DYE_INDEX_START)
    {
        var dyedPalette = new Palette(this);

        for (var i = 0; i < colorTableEntry.Colors.Length; ++i)
            dyedPalette[dyeIndexStart + i] = colorTableEntry.Colors[i];

        return dyedPalette;
    }

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".pal"),
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

        for (var i = 0; i < Count; ++i)
        {
            var color = this[i];

            writer.Write(color.Red);
            writer.Write(color.Green);
            writer.Write(color.Blue);
        }

        //pad the palette with black to 256 colors
        for (var i = Count; i < CONSTANTS.COLORS_PER_PALETTE; ++i)
        {
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
        }
    }
    #endregion

    #region LoadFrom
    /// <summary>
    ///     Loads all palettes from the specified archive that match the specified pattern
    /// </summary>
    /// <param name="pattern">
    ///     The pattern to match
    /// </param>
    /// <param name="archive">
    ///     The archive from which to extract palettes
    /// </param>
    public static Dictionary<int, Palette> FromArchive(string pattern, DataArchive archive)
    {
        var palettes = new Dictionary<int, Palette>();

        foreach (var entry in archive.GetEntries(pattern, ".pal"))
        {
            if (!entry.TryGetNumericIdentifier(out var identifier))
                continue;

            palettes[identifier] = FromEntry(entry);
        }

        return palettes;
    }

    /// <summary>
    ///     Loads a palette from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the palette from
    /// </param>
    public static Palette FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new Palette(segment);
    }

    /// <summary>
    ///     Loads a palette from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file to be read.
    /// </param>
    public static Palette FromFile(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".pal"),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        return new Palette(stream);
    }
    #endregion
}