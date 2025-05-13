#region
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using DALib.Abstractions;
using DALib.Data;
using DALib.Extensions;
using DALib.Utility;
using SkiaSharp;
#endregion

namespace DALib.Drawing;

/// <summary>
///     Represents an image file with the ".epf" extension. This image format supports one or more palettized images only.
///     The palettes will be stored in a separate file and will support full RGB888
/// </summary>
public sealed class EpfFile : Collection<EpfFrame>, ISavable
{
    /// <summary>
    ///     The pixel height of the image
    /// </summary>
    public short PixelHeight { get; set; }

    /// <summary>
    ///     The pixel width of the image
    /// </summary>
    public short PixelWidth { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public byte[] UnknownBytes { get; set; }

    /// <summary>
    ///     Initializes a new instance of the EpfFile class with the specified width and height.
    /// </summary>
    /// <param name="width">
    ///     The width of the EpfFile.
    /// </param>
    /// <param name="height">
    ///     The height of the EpfFile.
    /// </param>
    public EpfFile(short width, short height)
    {
        PixelHeight = height;
        PixelWidth = width;
        UnknownBytes = new byte[2];
    }

    private EpfFile(Stream stream)
    {
        const int HEADER_LENGTH = 12;
        short frameCount;
        int tocAddress;

        using (var headerReader = new BinaryReader(stream, Encoding.Default, true))
        {
            frameCount = headerReader.ReadInt16();
            PixelWidth = headerReader.ReadInt16();
            PixelHeight = headerReader.ReadInt16();
            UnknownBytes = headerReader.ReadBytes(2);
            tocAddress = headerReader.ReadInt32();
        }

        using var segment = stream.Slice(HEADER_LENGTH, stream.Length - HEADER_LENGTH);
        using var reader = new BinaryReader(segment, Encoding.Default, true);

        for (var i = 0; i < frameCount; ++i)
        {
            segment.Seek(tocAddress + i * 16, SeekOrigin.Begin);

            var top = reader.ReadInt16();
            var left = reader.ReadInt16();
            var bottom = reader.ReadInt16();
            var right = reader.ReadInt16();

            var width = right - left;
            var height = bottom - top;

            var startAddress = reader.ReadInt32();
            var endAddress = reader.ReadInt32();

            segment.Seek(startAddress, SeekOrigin.Begin);

            var data = (endAddress - startAddress) == (width * height)
                ? reader.ReadBytes(endAddress - startAddress)
                : reader.ReadBytes(tocAddress - startAddress);

            if ((width == 0) || (height == 0))
                continue;

            Add(
                new EpfFrame
                {
                    Top = top,
                    Left = left,
                    Bottom = bottom,
                    Right = right,
                    Data = data
                });
        }
    }

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".epf"),
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

        writer.Write((short)Count);
        writer.Write(PixelWidth);
        writer.Write(PixelHeight);
        writer.Write(UnknownBytes);

        var footerStartAddress = this.Sum(frame => Math.Min(frame.Data.Length, frame.PixelWidth * frame.PixelHeight));

        writer.Write(footerStartAddress);

        //write frame data
        for (var i = 0; i < Count; i++)
        {
            var frame = this[i];
            var length = Math.Min(frame.Data.Length, frame.PixelWidth * frame.PixelHeight);

            writer.Write(frame.Data[..length]);
        }

        var dataIndex = 0;

        //write footers
        for (var i = 0; i < Count; i++)
        {
            var frame = this[i];

            var length = Math.Min(frame.Data.Length, frame.PixelWidth * frame.PixelHeight);
            var dataEndAddress = dataIndex + length;

            writer.Write(frame.Top);
            writer.Write(frame.Left);
            writer.Write(frame.Bottom);
            writer.Write(frame.Right);
            writer.Write(dataIndex);
            writer.Write(dataEndAddress);

            dataIndex += length;
        }
    }
    #endregion

    #region LoadFrom
    /// <summary>
    ///     Loads an EpfFile with the specified fileName from the specified archive
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
    public static EpfFile FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".epf"), out var entry))
            throw new FileNotFoundException($"EPF file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Loads an EpfFile from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the EpfFile from
    /// </param>
    public static EpfFile FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new EpfFile(segment);
    }

    /// <summary>
    ///     Loads an EpfFile from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file to be read.
    /// </param>
    public static EpfFile FromFile(string path)
    {
        using var stream = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        return new EpfFile(stream);
    }

    /// <summary>
    ///     Converts a sequence of fully colored images to an EpfFile.
    /// </summary>
    /// <param name="options">
    ///     Options to be used for quantization. EpfFiles can only have a maximum of 256 colors due to being a palettized
    ///     format
    /// </param>
    /// <param name="orderedFrames">
    ///     The ordered collection of SKImage frames.
    /// </param>
    public static Palettized<EpfFile> FromImages(QuantizerOptions options, IEnumerable<SKImage> orderedFrames)
        => FromImages(options, orderedFrames.ToArray());

    /// <summary>
    ///     Converts a collection of fully colored images to an EpfFile
    /// </summary>
    /// <param name="options">
    ///     Options to be used for quantization. EpfFiles can only have a maximum of 256 colors due to being a palettized
    ///     format
    /// </param>
    /// <param name="orderedFrames">
    ///     The ordered array of SKImage frames.
    /// </param>
    public static Palettized<EpfFile> FromImages(QuantizerOptions options, params SKImage[] orderedFrames)
    {
        ImageProcessor.PreserveNonTransparentBlacks(orderedFrames);

        using var quantized = ImageProcessor.QuantizeMultiple(options, orderedFrames);

        (var images, var palette) = quantized;

        var imageWidth = (short)orderedFrames.Select(img => img.Width)
                                             .Max();

        var imageHeight = (short)orderedFrames.Select(img => img.Height)
                                              .Max();
        var epfFile = new EpfFile(imageWidth, imageHeight);

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];

            epfFile.Add(
                new EpfFrame
                {
                    Top = 0,
                    Left = 0,
                    Right = (short)image.Width,
                    Bottom = (short)image.Height,
                    Data = image.GetPalettizedPixelData(palette)
                });
        }

        return new Palettized<EpfFile>
        {
            Entity = epfFile,
            Palette = palette
        };
    }
    #endregion
}