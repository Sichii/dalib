#region
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DALib.Abstractions;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
using DALib.IO;
using DALib.Memory;
using DALib.Utility;
using SkiaSharp;
#endregion

namespace DALib.Drawing;

/// <summary>
///     Represents an image file with the ".efa" extension. This image format supports one or more fully colorized images
///     encoded in RGB565 (rendered in RGB555), ZLIB compressed image data, multiple blend modes, and a hard coded frame
///     interval
/// </summary>
public sealed class EfaFile : Collection<EfaFrame>, ISavable
{
    /// <summary>
    ///     The type of alpha blending to use when the image is rendered
    /// </summary>
    public EfaBlendingType BlendingType { get; set; }

    /// <summary>
    ///     The interval between frames in milliseconds
    /// </summary>
    public int FrameIntervalMs { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public int Unknown1 { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public byte[] Unknown2 { get; set; }

    /// <summary>
    ///     Creates an EFA file with default values
    /// </summary>
    public EfaFile()
    {
        Unknown1 = 0;
        BlendingType = EfaBlendingType.Additive;
        FrameIntervalMs = 50;
        Unknown2 = new byte[51];
    }

    private EfaFile(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Default, true);

        Unknown1 = reader.ReadInt32();
        var frameCount = reader.ReadInt32();
        FrameIntervalMs = reader.ReadInt32();
        var blendingType = reader.ReadByte();
        BlendingType = (EfaBlendingType)blendingType;
        Unknown2 = reader.ReadBytes(51);

        for (var i = 0; i < frameCount; i++)
        {
            var unknown1 = reader.ReadInt32();
            var offset = reader.ReadInt32();
            var compressedSize = reader.ReadInt32();
            var decompressedSize = reader.ReadInt32();
            var unknown2 = reader.ReadInt32();
            var unknown3 = reader.ReadInt32();
            var byteWidth = reader.ReadInt32();
            var unknown4 = reader.ReadInt32();
            var byteCount = reader.ReadInt32();
            var unknown5 = reader.ReadInt32();
            var centerX = reader.ReadInt16();
            var centerY = reader.ReadInt16();
            var unknown6 = reader.ReadInt32();
            var imageWidth = reader.ReadInt16();
            var imageHeight = reader.ReadInt16();
            var padWidth = reader.ReadInt16();
            var padHeight = reader.ReadInt16();
            var frameWidth = reader.ReadInt16();
            var frameHeight = reader.ReadInt16();
            var unknown7 = reader.ReadInt32();

            Add(
                new EfaFrame
                {
                    Unknown1 = unknown1,
                    StartAddress = offset,
                    CompressedSize = compressedSize,
                    DecompressedSize = decompressedSize,
                    Unknown2 = unknown2,
                    Unknown3 = unknown3,
                    ByteWidth = byteWidth,
                    Unknown4 = unknown4,
                    ByteCount = byteCount,
                    Unknown5 = unknown5,
                    CenterX = centerX,
                    CenterY = centerY,
                    Unknown6 = unknown6,
                    ImagePixelWidth = imageWidth,
                    ImagePixelHeight = imageHeight,
                    Left = padWidth,
                    Top = padHeight,
                    FramePixelWidth = frameWidth,
                    FramePixelHeight = frameHeight,
                    Unknown7 = unknown7,
                    Data = new byte[byteCount]
                });
        }

        using var dataSegment = new StreamSegment(stream, stream.Position, stream.Length - stream.Position);

        for (var i = 0; i < frameCount; i++)
        {
            var frame = this[i];

            DecompressToFrame(dataSegment, frame);
        }
    }

    #region LoadFrom
    /// <summary>
    ///     Loads an EfaFile with the specified fileName from the specified archive
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
    public static EfaFile FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".efa"), out var entry))
            throw new FileNotFoundException($"EFA file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Loads an EfaFile from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the EfaFile from
    /// </param>
    public static EfaFile FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new EfaFile(segment);
    }

    /// <summary>
    ///     Loads an EfaFile from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file to be read.
    /// </param>
    public static EfaFile FromFile(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".efa"),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        return new EfaFile(stream);
    }

    /// <summary>
    ///     Converts a sequence of fully colored images to an EfaFile.
    /// </summary>
    /// <param name="orderedFrames">
    ///     The ordered collection of SKImage frames.
    /// </param>
    public static EfaFile FromImages(IEnumerable<SKImage> orderedFrames) => FromImages(orderedFrames.ToArray());

    /// <summary>
    ///     Converts a collection of fully colored images to an EfaFile
    /// </summary>
    /// <param name="orderedFrames">
    ///     The ordered array of SKImage frames.
    /// </param>
    public static EfaFile FromImages(params SKImage[] orderedFrames)
    {
        var efaFile = new EfaFile();

        var imageWidth = orderedFrames.Max(img => img.Width);
        var imageHeight = orderedFrames.Max(img => img.Height);

        for (var i = 0; i < orderedFrames.Length; i++)
        {
            var image = orderedFrames[i];

            using var bitmap = SKBitmap.FromImage(image);

            // Only preserve blacks for non-additive effects. For Additive, black pixels
            // are correctly "add nothing" and should not be converted to near-black.
            if (efaFile.BlendingType != EfaBlendingType.Additive)
                ImageProcessor.PreserveNonTransparentBlacks(bitmap);

            byte[] rawBytes;

            if (image.ColorType == SKColorType.Rgb565)
                rawBytes = bitmap.Bytes;
            else
            {
                var writer = new SpanWriter(Encoding.Default, endianness: Endianness.LittleEndian);

                for (var y = 0; y < image.Height; y++)
                    for (var x = 0; x < image.Width; x++)
                    {
                        var color = bitmap.GetPixel(x, y);
                        writer.WriteRgb565Color(color);
                    }

                rawBytes = writer.ToSpan()
                                 .ToArray();
            }

            efaFile.Add(
                new EfaFrame
                {
                    Unknown1 = 3,
                    StartAddress = -1,
                    CompressedSize = -1,
                    DecompressedSize = rawBytes.Length,
                    Unknown2 = 1,
                    Unknown3 = 0,
                    ByteWidth = image.Width * 2,
                    Unknown4 = 4,
                    ByteCount = rawBytes.Length,
                    Unknown5 = 0,
                    CenterX = 0,
                    CenterY = 0,
                    Unknown6 = 0,
                    ImagePixelWidth = (short)imageWidth,
                    ImagePixelHeight = (short)imageHeight,
                    Left = 0,
                    Top = 0,
                    FramePixelWidth = (short)image.Width,
                    FramePixelHeight = (short)image.Height,
                    Unknown7 = 0,
                    Data = rawBytes
                });
        }

        return efaFile;
    }

    private static void DecompressToFrame(Stream dataStream, EfaFrame frame)
    {
        using var compressedSegment = new StreamSegment(dataStream, frame.StartAddress, frame.CompressedSize);
        using var decompressor = new ZLibStream(compressedSegment, CompressionMode.Decompress, true);

        Span<byte> decompressed = stackalloc byte[frame.DecompressedSize];

        decompressor.ReadAtLeast(decompressed, frame.DecompressedSize);

        decompressed[..frame.ByteCount]
            .CopyTo(frame.Data);

        //alpha surface data follows the RGB pixel data for SeparateAlpha and PerChannelAlpha blend types
        var alphaLength = frame.DecompressedSize - frame.ByteCount;

        if (alphaLength > 0)
        {
            frame.AlphaData = new byte[alphaLength];

            decompressed[frame.ByteCount..]
                .CopyTo(frame.AlphaData);
        }
    }
    #endregion

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".efa"),
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

        //write file header
        writer.Write(Unknown1);
        writer.Write(Count);
        writer.Write(FrameIntervalMs);
        writer.Write((byte)BlendingType);
        writer.Write(Unknown2);

        var offset = 0;
        var compressedFrames = new byte[Count][];

        for (var i = 0; i < Count; i++)
        {
            var frame = this[i];

            //compress the frame data (and alpha surface if present) and store it to be written later
            using var compressed = new MemoryStream();

            using (var compressor = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            {
                compressor.Write(frame.Data, 0, frame.Data.Length);

                if (frame.AlphaData is { Length: > 0 })
                    compressor.Write(frame.AlphaData, 0, frame.AlphaData.Length);
            }

            var compressedBytes = compressed.ToArray();
            var compressedSize = compressedBytes.Length;
            compressedFrames[i] = compressedBytes;

            //offset starts at 0 and grows with each frame's compressed data size (including the zlib header)
            frame.StartAddress = offset;
            frame.CompressedSize = compressedSize;

            writer.Write(frame.Unknown1);
            writer.Write(frame.StartAddress);
            writer.Write(frame.CompressedSize);
            writer.Write(frame.DecompressedSize);
            writer.Write(frame.Unknown2);
            writer.Write(frame.Unknown3);
            writer.Write(frame.ByteWidth);
            writer.Write(frame.Unknown4);
            writer.Write(frame.ByteCount);
            writer.Write(frame.Unknown5);
            writer.Write(frame.CenterX);
            writer.Write(frame.CenterY);
            writer.Write(frame.Unknown6);
            writer.Write(frame.ImagePixelWidth);
            writer.Write(frame.ImagePixelHeight);
            writer.Write(frame.Left);
            writer.Write(frame.Top);
            writer.Write(frame.FramePixelWidth);
            writer.Write(frame.FramePixelHeight);
            writer.Write(frame.Unknown7);

            offset += compressedSize;
        }

        for (var i = 0; i < Count; i++)
        {
            var compressedBytes = compressedFrames[i];

            writer.Write(compressedBytes);
        }
    }
    #endregion
}