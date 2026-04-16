#region
using System;
using System.IO;
using System.Text;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
using SkiaSharp;
#endregion

namespace DALib.Drawing.Virtualized;

/// <summary>
///     A lightweight view over an SPF file in a DataArchive. Parses the header, palettes, and frame table of contents on
///     construction; individual frame pixel data is read on demand from the underlying archive entry.
/// </summary>
public sealed class SpfView
{
    private readonly long DataSectionOffset;
    private readonly DataArchiveEntry Entry;
    private readonly SpfTocEntry[] Toc;

    /// <summary>
    ///     Indicates whether the images are colorized or palettized
    /// </summary>
    public SpfFormatType Format { get; }

    /// <summary>
    ///     The primary palette used for palettized images
    /// </summary>
    public Palette? PrimaryColors { get; }

    /// <summary>
    ///     The secondary palette used for palettized images
    /// </summary>
    public Palette? SecondaryColors { get; }

    /// <summary>
    ///     The number of frames in the SPF file
    /// </summary>
    public int Count => Toc.Length;

    private SpfView(
        DataArchiveEntry entry,
        long dataSectionOffset,
        SpfTocEntry[] toc,
        SpfFormatType format,
        Palette? primaryColors,
        Palette? secondaryColors)
    {
        Entry = entry;
        DataSectionOffset = dataSectionOffset;
        Toc = toc;
        Format = format;
        PrimaryColors = primaryColors;
        SecondaryColors = secondaryColors;
    }

    /// <summary>
    ///     Creates an SpfView with the specified fileName from the specified archive
    /// </summary>
    /// <param name="fileName">
    ///     The name of the SPF file to extract from the archive.
    /// </param>
    /// <param name="archive">
    ///     The DataArchive from which to retrieve the SPF file.
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if the SPF file with the specified name is not found in the archive.
    /// </exception>
    public static SpfView FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".spf"), out var entry))
            throw new FileNotFoundException($"SPF file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Creates an SpfView from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the SpfView from
    /// </param>
    public static SpfView FromEntry(DataArchiveEntry entry)
    {
        using var stream = entry.ToStreamSegment();
        using var reader = new BinaryReader(stream, Encoding.Default, true);

        _ = reader.ReadUInt32(); // Unknown1
        _ = reader.ReadUInt32(); // Unknown2
        var format = (SpfFormatType)reader.ReadUInt32();

        Palette? primaryColors = null;
        Palette? secondaryColors = null;

        if (format == SpfFormatType.Palettized)
        {
            primaryColors = new Palette();
            secondaryColors = new Palette();

            for (var i = 0; i < 256; i++)
                primaryColors[i] = reader.ReadRgb565Color();

            for (var i = 0; i < 256; i++)
                secondaryColors[i] = reader.ReadRgb555Color();
        }

        var frameCount = reader.ReadUInt32();
        var tocEntries = new SpfTocEntry[frameCount];

        for (var i = 0; i < frameCount; i++)
            tocEntries[i] = new SpfTocEntry(
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                (reader.ReadUInt32() & 1) != 0,
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32());

        // totalByteCount followed by data section
        _ = reader.ReadUInt32();
        var dataSectionOffset = stream.Position;

        return new SpfView(
            entry,
            dataSectionOffset,
            tocEntries,
            format,
            primaryColors,
            secondaryColors);
    }

    /// <summary>
    ///     Reads and returns the frame at the specified index. Pixel data is read from the archive on each access.
    /// </summary>
    public SpfFrame this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Toc.Length);

            var toc = Toc[index];

            using var stream = Entry.ToStreamSegment();
            using var reader = new BinaryReader(stream, Encoding.Default, true);

            stream.Seek(DataSectionOffset + toc.StartAddress, SeekOrigin.Begin);

            var frame = new SpfFrame
            {
                Left = toc.Left,
                Top = toc.Top,
                Right = toc.Right,
                Bottom = toc.Bottom,
                CenterX = toc.CenterX,
                CenterY = toc.CenterY,
                HasCenterPoint = toc.HasCenterPoint,
                StartAddress = toc.StartAddress,
                ByteWidth = toc.ByteWidth,
                ByteCount = toc.ByteCount,
                ImageByteCount = toc.ImageByteCount
            };

            switch (Format)
            {
                case SpfFormatType.Palettized:
                    frame.Data = reader.ReadBytes((int)toc.ByteCount);

                    break;
                case SpfFormatType.Colorized:
                    frame.ColorData = new SKColor[toc.ImageByteCount];
                    var colorIndex = 0;

                    for (var y = 0; y < toc.Bottom; y++)
                        for (var x = 0; x < toc.Right; x++)
                            frame.ColorData[colorIndex++] = reader.ReadRgb565Color();

                    break;
            }

            return frame;
        }
    }

    /// <summary>
    ///     Attempts to read the frame at the specified index. Returns false if the index is out of range.
    /// </summary>
    public bool TryGetValue(int index, out SpfFrame? frame)
    {
        if ((index < 0) || (index >= Toc.Length))
        {
            frame = null;

            return false;
        }

        frame = this[index];

        return true;
    }

    private readonly record struct SpfTocEntry(
        ushort Left,
        ushort Top,
        ushort Right,
        ushort Bottom,
        short CenterX,
        short CenterY,
        bool HasCenterPoint,
        uint StartAddress,
        uint ByteWidth,
        uint ByteCount,
        uint ImageByteCount);
}