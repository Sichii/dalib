#region
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DALib.Data;
using DALib.Extensions;
#endregion

namespace DALib.Drawing.Virtualized;

/// <summary>
///     A lightweight view over an EPF file in a DataArchive. Parses only the header and table of contents on construction;
///     individual frame pixel data is read on demand from the underlying memory-mapped archive entry.
/// </summary>
public sealed class EpfView
{
    private const int HEADER_LENGTH = 12;
    private const int TOC_ENTRY_SIZE = 16;

    private readonly DataArchiveEntry Entry;
    private readonly TocEntry[] Toc;
    private readonly int TocAddress;

    /// <summary>
    ///     The pixel height of the image
    /// </summary>
    public short PixelHeight { get; }

    /// <summary>
    ///     The pixel width of the image
    /// </summary>
    public short PixelWidth { get; }

    /// <summary>
    ///     The number of frames in the EPF file
    /// </summary>
    public int Count => Toc.Length;

    private EpfView(
        DataArchiveEntry entry,
        short pixelWidth,
        short pixelHeight,
        int tocAddress,
        TocEntry[] toc)
    {
        Entry = entry;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        TocAddress = tocAddress;
        Toc = toc;
    }

    /// <summary>
    ///     Creates an EpfView with the specified fileName from the specified archive
    /// </summary>
    /// <param name="fileName">
    ///     The name of the EPF file to extract from the archive.
    /// </param>
    /// <param name="archive">
    ///     The DataArchive from which to retrieve the EPF file.
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if the EPF file with the specified name is not found in the archive.
    /// </exception>
    public static EpfView FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".epf"), out var entry))
            throw new FileNotFoundException($"EPF file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Creates an EpfView from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the EpfView from
    /// </param>
    public static EpfView FromEntry(DataArchiveEntry entry)
    {
        using var stream = entry.ToStreamSegment();
        using var reader = new BinaryReader(stream, Encoding.Default, true);

        var frameCount = reader.ReadInt16();
        var pixelWidth = reader.ReadInt16();
        var pixelHeight = reader.ReadInt16();
        stream.Seek(2, SeekOrigin.Current);
        var tocAddress = reader.ReadInt32();

        var tocEntries = new List<TocEntry>(frameCount);

        for (var i = 0; i < frameCount; i++)
        {
            stream.Seek(HEADER_LENGTH + tocAddress + i * TOC_ENTRY_SIZE, SeekOrigin.Begin);

            var top = reader.ReadInt16();
            var left = reader.ReadInt16();
            var bottom = reader.ReadInt16();
            var right = reader.ReadInt16();
            var startAddress = reader.ReadInt32();
            var endAddress = reader.ReadInt32();

            //empty frames (width==0 || height==0) are preserved in the TOC so that direct-index
            //access by animation-frame index stays stable. Weapons/equipment use 0x0 frames as a
            //"no visual on this pose" marker; dropping them would shift all subsequent indices and
            //either mis-render or mask later frames.
            tocEntries.Add(
                new TocEntry(
                    top,
                    left,
                    bottom,
                    right,
                    startAddress,
                    endAddress));
        }

        return new EpfView(
            entry,
            pixelWidth,
            pixelHeight,
            tocAddress,
            tocEntries.ToArray());
    }

    /// <summary>
    ///     Reads and returns the frame at the specified index. Pixel data is read from the archive on each access.
    /// </summary>
    public EpfFrame this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Toc.Length);

            var toc = Toc[index];

            var width = toc.Right - toc.Left;
            var height = toc.Bottom - toc.Top;

            //empty-frame marker: preserve the TOC entry but return an empty Data array — callers
            //should check PixelWidth/PixelHeight before rendering.
            if ((width == 0) || (height == 0))
                return new EpfFrame
                {
                    Top = toc.Top,
                    Left = toc.Left,
                    Bottom = toc.Bottom,
                    Right = toc.Right,
                    Data = []
                };

            using var stream = Entry.ToStreamSegment();
            using var reader = new BinaryReader(stream, Encoding.Default, true);

            stream.Seek(HEADER_LENGTH + toc.StartAddress, SeekOrigin.Begin);

            var data = (toc.EndAddress - toc.StartAddress) == (width * height)
                ? reader.ReadBytes(toc.EndAddress - toc.StartAddress)
                : reader.ReadBytes(TocAddress - toc.StartAddress);

            return new EpfFrame
            {
                Top = toc.Top,
                Left = toc.Left,
                Bottom = toc.Bottom,
                Right = toc.Right,
                Data = data
            };
        }
    }

    /// <summary>
    ///     Attempts to read the frame at the specified index. Returns false if the index is out of range.
    /// </summary>
    public bool TryGetValue(int index, out EpfFrame? frame)
    {
        if ((index < 0) || (index >= Toc.Length))
        {
            frame = null;

            return false;
        }

        frame = this[index];

        return true;
    }

    private readonly record struct TocEntry(
        short Top,
        short Left,
        short Bottom,
        short Right,
        int StartAddress,
        int EndAddress);
}