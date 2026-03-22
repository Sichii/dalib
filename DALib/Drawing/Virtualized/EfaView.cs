#region
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
using DALib.IO;
#endregion

namespace DALib.Drawing.Virtualized;

/// <summary>
///     A lightweight view over an EFA file in a DataArchive. Parses the header and frame table of contents on
///     construction; individual frame Zlib decompression is deferred until the frame is accessed.
/// </summary>
public sealed class EfaView
{
    private readonly long DataSectionOffset;
    private readonly DataArchiveEntry Entry;
    private readonly EfaTocEntry[] Toc;

    /// <summary>
    ///     The type of alpha blending to use when the image is rendered
    /// </summary>
    public EfaBlendingType BlendingType { get; }

    /// <summary>
    ///     The interval between frames in milliseconds
    /// </summary>
    public int FrameIntervalMs { get; }

    /// <summary>
    ///     The number of frames in the EFA file
    /// </summary>
    public int Count => Toc.Length;

    private EfaView(
        DataArchiveEntry entry,
        long dataSectionOffset,
        EfaTocEntry[] toc,
        EfaBlendingType blendingType,
        int frameIntervalMs)
    {
        Entry = entry;
        DataSectionOffset = dataSectionOffset;
        Toc = toc;
        BlendingType = blendingType;
        FrameIntervalMs = frameIntervalMs;
    }

    /// <summary>
    ///     Creates an EfaView with the specified fileName from the specified archive
    /// </summary>
    /// <param name="fileName">
    ///     The name of the EFA file to search for in the archive.
    /// </param>
    /// <param name="archive">
    ///     The DataArchive from which to retrieve the EFA file.
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if the EFA file with the specified name is not found in the archive.
    /// </exception>
    public static EfaView FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".efa"), out var entry))
            throw new FileNotFoundException($"EFA file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Creates an EfaView from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the EfaView from
    /// </param>
    public static EfaView FromEntry(DataArchiveEntry entry)
    {
        using var stream = entry.ToStreamSegment();
        using var reader = new BinaryReader(stream, Encoding.Default, true);

        // File header (64 bytes)
        _ = reader.ReadInt32(); // Unknown1
        var frameCount = reader.ReadInt32();
        var frameIntervalMs = reader.ReadInt32();
        var blendingType = (EfaBlendingType)reader.ReadByte();
        stream.Seek(51, SeekOrigin.Current); // Unknown2

        // Frame TOC entries (88 bytes each)
        var tocEntries = new EfaTocEntry[frameCount];

        for (var i = 0; i < frameCount; i++)
            tocEntries[i] = new EfaTocEntry(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt32(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt32());

        // Data section starts immediately after all TOC entries
        var dataSectionOffset = stream.Position;

        return new EfaView(
            entry,
            dataSectionOffset,
            tocEntries,
            blendingType,
            frameIntervalMs);
    }

    /// <summary>
    ///     Reads and decompresses the frame at the specified index. Zlib decompression occurs on each access.
    /// </summary>
    public EfaFrame this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Toc.Length);

            var toc = Toc[index];
            var data = new byte[toc.ByteCount];

            using var stream = Entry.ToStreamSegment();
            using var compressedSegment = new StreamSegment(stream, DataSectionOffset + toc.StartAddress, toc.CompressedSize);
            using var decompressor = new ZLibStream(compressedSegment, CompressionMode.Decompress, true);

            Span<byte> decompressed = stackalloc byte[toc.DecompressedSize];
            decompressor.ReadAtLeast(decompressed, toc.DecompressedSize);

            decompressed[..toc.ByteCount]
                .CopyTo(data);

            byte[]? alphaData = null;
            var alphaLength = toc.DecompressedSize - toc.ByteCount;

            if (alphaLength > 0)
            {
                alphaData = new byte[alphaLength];

                decompressed[toc.ByteCount..]
                    .CopyTo(alphaData);
            }

            return new EfaFrame
            {
                Unknown1 = toc.Unknown1,
                StartAddress = toc.StartAddress,
                CompressedSize = toc.CompressedSize,
                DecompressedSize = toc.DecompressedSize,
                Unknown2 = toc.Unknown2,
                Unknown3 = toc.Unknown3,
                ByteWidth = toc.ByteWidth,
                Unknown4 = toc.Unknown4,
                ByteCount = toc.ByteCount,
                Unknown5 = toc.Unknown5,
                CenterX = toc.CenterX,
                CenterY = toc.CenterY,
                Unknown6 = toc.Unknown6,
                ImagePixelWidth = toc.ImagePixelWidth,
                ImagePixelHeight = toc.ImagePixelHeight,
                Left = toc.Left,
                Top = toc.Top,
                FramePixelWidth = toc.FramePixelWidth,
                FramePixelHeight = toc.FramePixelHeight,
                Unknown7 = toc.Unknown7,
                Data = data,
                AlphaData = alphaData
            };
        }
    }

    /// <summary>
    ///     Attempts to read and decompress the frame at the specified index. Returns false if the index is out of range.
    /// </summary>
    public bool TryGetValue(int index, out EfaFrame? frame)
    {
        if ((index < 0) || (index >= Toc.Length))
        {
            frame = null;

            return false;
        }

        frame = this[index];

        return true;
    }

    private readonly record struct EfaTocEntry(
        int Unknown1,
        int StartAddress,
        int CompressedSize,
        int DecompressedSize,
        int Unknown2,
        int Unknown3,
        int ByteWidth,
        int Unknown4,
        int ByteCount,
        int Unknown5,
        short CenterX,
        short CenterY,
        int Unknown6,
        short ImagePixelWidth,
        short ImagePixelHeight,
        short Left,
        short Top,
        short FramePixelWidth,
        short FramePixelHeight,
        int Unknown7);
}