using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using DALib.Abstractions;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
using DALib.IO;
using DALib.Utility;
using SkiaSharp;

namespace DALib.Drawing;

/// <summary>
///     Represents an image file with the ".spf" extension. This image format supports one or more of either colorized or
///     palettized images. In the case where a palette is used, the palette will contain 256 RGB565 encoded entries, and
///     256 RGB555 encoded entries (only the RGB565 entries are used). In the case where colorized images are used, the
///     image will be encoded as RGB565, and a second copy of the data will encoded as RGB555 per frame.
/// </summary>
public sealed class SpfFile : Collection<SpfFrame>, ISavable
{
    /// <summary>
    ///     Indicates whether the images are colorized or palettized
    /// </summary>
    public SpfFormatType Format { get; set; }

    /// <summary>
    ///     The primary palette used for palettized images
    /// </summary>
    public Palette? PrimaryColors { get; set; }

    /// <summary>
    ///     The secondary palette used for palettized images
    /// </summary>
    public Palette? SecondaryColors { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public uint Unknown1 { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public uint Unknown2 { get; set; }

    /// <summary>
    ///     Initializes a new instance of the SpfFile class(palettized) with the specified primary and secondary palettes
    /// </summary>
    /// <param name="primaryColors">
    ///     The primary color palette used by the images (RGB565 scaled to RGB888)
    /// </param>
    /// <param name="secondaryColors">
    ///     The secondary color palette used by the images (RGB555 scale to RGB888)
    /// </param>
    public SpfFile(Palette primaryColors, Palette secondaryColors)
    {
        Format = SpfFormatType.Palettized;
        PrimaryColors = primaryColors;
        SecondaryColors = secondaryColors;
    }

    /// <summary>
    ///     Initializes a new instance of the SpfFile class(colorized)
    /// </summary>
    public SpfFile() => Format = SpfFormatType.Colorized;

    private SpfFile(Stream stream)
    {
        PrimaryColors = new Palette();
        SecondaryColors = new Palette();

        using var reader = new BinaryReader(stream, Encoding.Default, true);

        Unknown1 = reader.ReadUInt32();
        Unknown2 = reader.ReadUInt32();
        Format = (SpfFormatType)reader.ReadUInt32();

        switch (Format)
        {
            case SpfFormatType.Colorized:
                ReadColorized(reader);

                break;
            case SpfFormatType.Palettized:
                ReadPalettized(reader);

                break;
            default:
                throw new InvalidDataException($"SPF format \"{Format}\" is not supported");
        }
    }

    private void ReadColorized(BinaryReader reader)
    {
        var frameCount = reader.ReadUInt32();

        for (var i = 0; i < frameCount; i++)
        {
            var left = reader.ReadUInt16();
            var top = reader.ReadUInt16();
            var right = reader.ReadUInt16();
            var bottom = reader.ReadUInt16();
            var centerX = reader.ReadInt16();
            var centerY = reader.ReadInt16();
            var flags = reader.ReadUInt32();
            var startAddress = reader.ReadUInt32();
            var byteWidth = reader.ReadUInt32();
            var byteCount = reader.ReadUInt32();
            var imageByteCount = reader.ReadUInt32();

            Add(
                new SpfFrame
                {
                    Left = left,
                    Top = top,
                    Right = right,
                    Bottom = bottom,
                    CenterY = centerY,
                    CenterX = centerX,
                    HasCenterPoint = (flags & 1) != 0,
                    StartAddress = startAddress,
                    ByteWidth = byteWidth,
                    ByteCount = byteCount,
                    ImageByteCount = imageByteCount,
                    ColorData = new SKColor[imageByteCount]
                });
        }

        var totalByteAcount = reader.ReadUInt32();

        using var segment = new StreamSegment(reader.BaseStream, reader.BaseStream.Position, totalByteAcount);

        for (var i = 0; i < frameCount; i++)
        {
            var frame = Items[i];
            segment.Seek(frame.StartAddress, SeekOrigin.Begin);
            var index = 0;

            for (var y = 0; y < frame.Bottom; y++)
            {
                for (var x = 0; x < frame.Right; x++)
                {
                    var color = reader.ReadRgb565Color();
                    frame.ColorData![index++] = color;
                }
            }
        }
    }

    private void ReadPalettized(BinaryReader reader)
    {
        for (var i = 0; i < 256; i++)
            PrimaryColors![i] = reader.ReadRgb565Color();

        for (var i = 0; i < 256; i++)
            SecondaryColors![i] = reader.ReadRgb555Color();

        var frameCount = reader.ReadUInt32();

        for (var i = 0; i < frameCount; i++)
        {
            var left = reader.ReadUInt16();
            var top = reader.ReadUInt16();
            var right = reader.ReadUInt16();
            var bottom = reader.ReadUInt16();
            var centerX = reader.ReadInt16();
            var centerY = reader.ReadInt16();
            var flags = reader.ReadUInt32();
            var startAddress = reader.ReadUInt32();
            var byteWidth = reader.ReadUInt32();
            var byteCount = reader.ReadUInt32();
            var imageByteCount = reader.ReadUInt32();

            Add(
                new SpfFrame
                {
                    Left = left,
                    Top = top,
                    Right = right,
                    Bottom = bottom,
                    CenterY = centerY,
                    CenterX = centerX,
                    HasCenterPoint = (flags & 1) != 0,
                    StartAddress = startAddress,
                    ByteWidth = byteWidth,
                    ByteCount = byteCount,
                    ImageByteCount = imageByteCount,
                    Data = new byte[byteCount]
                });
        }

        var totalByteAcount = reader.ReadUInt32();

        using var segment = new StreamSegment(reader.BaseStream, reader.BaseStream.Position, totalByteAcount);

        for (var i = 0; i < frameCount; i++)
        {
            var frame = Items[i];
            segment.Seek(frame.StartAddress, SeekOrigin.Begin);
            _ = segment.Read(frame.Data!, 0, (int)frame.ByteCount);
        }
    }

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".spf"),
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

        writer.Write(Unknown1);
        writer.Write(Unknown2);
        writer.Write((int)Format);

        switch (Format)
        {
            case SpfFormatType.Palettized:
                SavePalettized(writer);

                break;
            case SpfFormatType.Colorized:
                SaveColorized(writer);

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void SaveColorized(BinaryWriter writer)
    {
        writer.Write(Count);
        var startAddress = 0u;

        for (var i = 0; i < Count; i++)
        {
            var frame = Items[i];
            frame.StartAddress = startAddress;
            startAddress += frame.ByteCount;

            writer.Write(frame.Left);
            writer.Write(frame.Top);
            writer.Write(frame.Right);
            writer.Write(frame.Bottom);
            writer.Write(frame.CenterX);
            writer.Write(frame.CenterY);
            writer.Write(frame.HasCenterPoint ? 1u : 0u);
            writer.Write(frame.StartAddress);
            writer.Write(frame.ByteWidth);
            writer.Write(frame.ByteCount);
            writer.Write(frame.ImageByteCount);
        }

        //startAddress is now the total byte count
        //since all frame data lengths have been added to it
        writer.Write(startAddress);

        for (var i = 0; i < Count; i++)
        {
            var frame = Items[i];

            //write primary color data
            for (var y = 0; y < frame.Bottom; y++)
            {
                for (var x = 0; x < frame.Right; x++)
                {
                    var color = frame.ColorData![y * frame.Right + x];
                    writer.WriteRgb565Color(color);
                }
            }

            //write secondary color dta
            for (var y = 0; y < frame.Bottom; y++)
            {
                for (var x = 0; x < frame.Right; x++)
                {
                    var color = frame.ColorData![y * frame.Right + x];
                    writer.WriteRgb555Color(color);
                }
            }
        }
    }

    private void SavePalettized(BinaryWriter writer)
    {
        for (var i = 0; i < CONSTANTS.COLORS_PER_PALETTE; i++)
            writer.WriteRgb565Color(PrimaryColors![i]);

        for (var i = 0; i < CONSTANTS.COLORS_PER_PALETTE; i++)
            writer.WriteRgb555Color(SecondaryColors![i]);

        writer.Write(Count);
        var startAddress = 0u;

        for (var i = 0; i < Count; i++)
        {
            var frame = Items[i];
            frame.StartAddress = startAddress;
            startAddress += frame.ByteCount;

            writer.Write(frame.Left);
            writer.Write(frame.Top);
            writer.Write(frame.Right);
            writer.Write(frame.Bottom);
            writer.Write(frame.CenterX);
            writer.Write(frame.CenterY);
            writer.Write(frame.HasCenterPoint ? 1u : 0u);
            writer.Write(frame.StartAddress);
            writer.Write(frame.ByteWidth);
            writer.Write(frame.ByteCount);
            writer.Write(frame.ImageByteCount);
        }

        //startAddress is now the total byte count
        //since all frame data lengths have been added to it
        writer.Write(startAddress);

        for (var i = 0; i < Count; i++)
        {
            var frame = Items[i];
            writer.Write(frame.Data!);
        }
    }
    #endregion

    #region LoadFrom
    /// <summary>
    ///     Converts a sequence of fully colored images to a colorized SpfFile.
    /// </summary>
    /// <param name="orderedFrames">
    ///     The ordered collection of SKImage frames.
    /// </param>
    public static SpfFile FromImages(IEnumerable<SKImage> orderedFrames) => FromImages(orderedFrames.ToArray());

    /// <summary>
    ///     Converts a collection of fully colored images to a colorized SpfFile
    /// </summary>
    /// <param name="orderedFrames">
    ///     The ordered array of SKImage frames.
    /// </param>
    public static SpfFile FromImages(params SKImage[] orderedFrames)
    {
        var spfFile = new SpfFile();

        for (var i = 0; i < orderedFrames.Length; i++)
        {
            var image = orderedFrames[i];
            using var bitmap = SKBitmap.FromImage(image);
            ImageProcessor.PreserveNonTransparentBlacks(bitmap);

            var frame = new SpfFrame
            {
                Left = 0,
                Top = 0,
                Right = (ushort)image.Width,
                Bottom = (ushort)image.Height,
                CenterX = unchecked((short)0xCCCC),
                CenterY = unchecked((short)0xCCCC),
                StartAddress = 0,
                ByteWidth = (uint)image.Width * 2,
                ByteCount = (uint)(image.Width * image.Height * 4), //2 bytes per pixel, 2 copies of image
                ImageByteCount = (uint)(image.Width * image.Height * 2),
                ColorData = new SKColor[image.Width * image.Height]
            };

            for (var y = 0; y < image.Height; y++)
                for (var x = 0; x < image.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    frame.ColorData[y * image.Width + x] = color;
                }

            spfFile.Add(frame);
        }

        return spfFile;
    }

    /// <summary>
    ///     Converts a sequence of fully colored images to a palettized SpfFile.
    /// </summary>
    /// <param name="options">
    ///     Options to be used for quantization. Palettized SpfFiles can only have a maximum of 256 colors due to being a
    ///     palettized format
    /// </param>
    /// <param name="orderedFrames">
    ///     The ordered collection of SKImage frames.
    /// </param>
    public static SpfFile FromImages(QuantizerOptions options, IEnumerable<SKImage> orderedFrames)
        => FromImages(options, orderedFrames.ToArray());

    /// <summary>
    ///     Converts a collection of fully colored images to a palettized SpfFile
    /// </summary>
    /// <param name="options">
    ///     Options to be used for quantization. Palettized SpfFiles can only have a maximum of 256 colors due to being a
    ///     palettized format
    /// </param>
    /// <param name="orderedFrames">
    ///     The ordered array of SKImage frames.
    /// </param>
    public static SpfFile FromImages(QuantizerOptions options, params SKImage[] orderedFrames)
    {
        ImageProcessor.PreserveNonTransparentBlacks(orderedFrames);

        using var quantized = ImageProcessor.QuantizeMultiple(options, orderedFrames);
        (var images, var palette) = quantized;

        var spfFile = new SpfFile(palette, palette);

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];

            spfFile.Add(
                new SpfFrame
                {
                    Left = 0,
                    Top = 0,
                    Right = (ushort)image.Width,
                    Bottom = (ushort)image.Height,
                    CenterX = unchecked((short)0xCCCC),
                    CenterY = unchecked((short)0xCCCC),
                    StartAddress = 0,
                    ByteWidth = (uint)image.Width,
                    ByteCount = (uint)image.Width * (uint)image.Height,
                    ImageByteCount = (uint)image.Width * (uint)image.Height,
                    Data = image.GetPalettizedPixelData(palette)
                });
        }

        return spfFile;
    }

    /// <summary>
    ///     Loads an SpfFile with the specified fileName from the specified archive
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
    public static SpfFile FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".spf"), out var entry))
            throw new FileNotFoundException($"SPF file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Loads an SpfFile from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the SpfFile from
    /// </param>
    public static SpfFile FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new SpfFile(segment);
    }

    /// <summary>
    ///     Loads an SpfFile from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file to be read.
    /// </param>
    public static SpfFile FromFile(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".spf"),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        return new SpfFile(stream);
    }
    #endregion
}