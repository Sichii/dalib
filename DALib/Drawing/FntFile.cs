using System;
using System.IO;
using DALib.Data;
using DALib.Extensions;

namespace DALib.Drawing;

/// <summary>
///     Represents a bitmap font file with the ".fnt" extension. Headerless 1-bit-per-pixel glyph bitmaps stored
///     contiguously. English fonts use 8x12 cells, Korean fonts use 16x12 cells
/// </summary>
public sealed class FntFile
{
    private readonly int BytesPerRow;
    private readonly int BytesPerGlyph;

    /// <summary>
    ///     The raw 1bpp glyph bitmap data
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    ///     The pixel width of each glyph cell
    /// </summary>
    public int GlyphWidth { get; }

    /// <summary>
    ///     The pixel height of each glyph cell
    /// </summary>
    public int GlyphHeight { get; }

    /// <summary>
    ///     The total number of glyphs in the font
    /// </summary>
    public int GlyphCount { get; }

    private FntFile(byte[] data, int glyphWidth, int glyphHeight)
    {
        Data = data;
        GlyphWidth = glyphWidth;
        GlyphHeight = glyphHeight;
        BytesPerRow = (glyphWidth + 7) / 8;
        BytesPerGlyph = BytesPerRow * glyphHeight;
        GlyphCount = data.Length / BytesPerGlyph;
    }

    /// <summary>
    ///     Returns true if the specified glyph index is within the valid range
    /// </summary>
    public bool IsValidIndex(int index) => (uint)index < (uint)GlyphCount;

    #region LoadFrom
    /// <summary>
    ///     Loads an FntFile with the specified fileName from the specified archive
    /// </summary>
    /// <param name="fileName">
    ///     The name of the FNT file to extract from the archive
    /// </param>
    /// <param name="archive">
    ///     The DataArchive from which to retrieve the FNT file
    /// </param>
    /// <param name="glyphWidth">
    ///     The pixel width of each glyph cell (8 for English, 16 for Korean)
    /// </param>
    /// <param name="glyphHeight">
    ///     The pixel height of each glyph cell (typically 12)
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if the FNT file with the specified name is not found in the archive
    /// </exception>
    public static FntFile FromArchive(string fileName, DataArchive archive, int glyphWidth, int glyphHeight)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".fnt"), out var entry))
            throw new FileNotFoundException($"FNT file \"{fileName}\" was not found in the archive");

        return FromEntry(entry, glyphWidth, glyphHeight);
    }

    /// <summary>
    ///     Loads an FntFile from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the FntFile from
    /// </param>
    /// <param name="glyphWidth">
    ///     The pixel width of each glyph cell
    /// </param>
    /// <param name="glyphHeight">
    ///     The pixel height of each glyph cell
    /// </param>
    public static FntFile FromEntry(DataArchiveEntry entry, int glyphWidth, int glyphHeight)
        => new(entry.ToSpan().ToArray(), glyphWidth, glyphHeight);

    /// <summary>
    ///     Loads an FntFile from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file to be read
    /// </param>
    /// <param name="glyphWidth">
    ///     The pixel width of each glyph cell
    /// </param>
    /// <param name="glyphHeight">
    ///     The pixel height of each glyph cell
    /// </param>
    public static FntFile FromFile(string path, int glyphWidth, int glyphHeight)
    {
        var data = File.ReadAllBytes(path);

        return new FntFile(data, glyphWidth, glyphHeight);
    }
    #endregion
}
