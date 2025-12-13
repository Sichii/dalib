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
using DALib.Utility;
using SkiaSharp;

namespace DALib.Drawing;

/// <summary>
///     Represents an image file with the ".mpf" extension. This image format supports one or more palettized images only.
///     This format is used primarily for StopMotion Animation. The palettes will be stored in a separate file and will
///     support full RGB888
/// </summary>
public sealed class MpfFile : Collection<MpfFrame>, ISavable
{
    /// <summary>
    ///     The number of frames for the second attack animation
    /// </summary>
    public byte Attack2FrameCount { get; set; }

    /// <summary>
    ///     The starting frame index of the second attack animation
    /// </summary>
    public byte Attack2StartIndex { get; set; }

    /// <summary>
    ///     The number of frames for the third attack animation
    /// </summary>
    public byte Attack3FrameCount { get; set; }

    /// <summary>
    ///     The starting frame index of the third attack animation
    /// </summary>
    public byte Attack3StartIndex { get; set; }

    /// <summary>
    ///     The number of frames for the primary attack animation
    /// </summary>
    public byte AttackFrameCount { get; set; }

    /// <summary>
    ///     The starting frame index of the primary attack animation
    /// </summary>
    public byte AttackFrameIndex { get; set; }

    /// <summary>
    ///     Indicates whether the MpfFile will contains multiple attack animations or not
    /// </summary>
    public MpfFormatType FormatType { get; set; }

    /// <summary>
    ///     Indicates if the image will have 4 extra bytes at the beginning of the header
    /// </summary>
    public MpfHeaderType HeaderType { get; set; }

    /// <summary>
    ///     The number of frames in the standing animation including optional frames. If your normal standing animation has 4
    ///     frames, but there are 2 extra frames that should occasionally be played, then you would put 6 here. (4 normal
    ///     frames + 2 optional frames). If there is no optional animation, this will have a value of 0.
    /// </summary>
    public byte OptionalAnimationFrameCount { get; set; }

    /// <summary>
    ///     Specifies the ratio of playing the optional standing frames. For example, if this is set to 30, it will play the
    ///     optional frames 30% of the time
    /// </summary>
    public byte OptionalAnimationRatio { get; set; }

    /// <summary>
    ///     The palette number used to colorize this image
    /// </summary>
    public int PaletteNumber { get; set; }

    /// <summary>
    ///     The pixel height of the image
    /// </summary>
    public short PixelHeight { get; set; }

    /// <summary>
    ///     The pixel width of the image
    /// </summary>
    public short PixelWidth { get; set; }

    /// <summary>
    ///     The number of frames for the standing animation without the optional frames
    /// </summary>
    public byte StandingFrameCount { get; set; }

    /// <summary>
    ///     The starting frame index of the standing animation
    /// </summary>
    public byte StandingFrameIndex { get; set; }

    /// <summary>
    ///     Unknown header bytes at the beginning of the file. Only used if HeaderType is set to Unknown
    /// </summary>
    public byte[] UnknownHeaderBytes { get; set; }

    /// <summary>
    ///     The number of frames for the walking animation
    /// </summary>
    public byte WalkFrameCount { get; set; }

    /// <summary>
    ///     The starting frame index of the walking animation
    /// </summary>
    public byte WalkFrameIndex { get; set; }

    /// <summary>
    ///     Initializes a new instance of the MpfFile class with the specified width and height.
    /// </summary>
    /// <param name="headerType">
    ///     Used to determine if 4 empty bytes will be written to the header
    /// </param>
    /// <param name="formatType">
    ///     Used to determine how many attack animations there are
    /// </param>
    /// <param name="width">
    ///     The pixel width of the image
    /// </param>
    /// <param name="height">
    ///     The pixel height of the image
    /// </param>
    public MpfFile(
        MpfHeaderType headerType,
        MpfFormatType formatType,
        short width,
        short height)
    {
        HeaderType = headerType;
        FormatType = formatType;
        UnknownHeaderBytes = HeaderType == MpfHeaderType.Unknown ? new byte[4] : [];
        PixelWidth = width;
        PixelHeight = height;
    }

    private MpfFile(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Default, true);

        HeaderType = (MpfHeaderType)reader.ReadInt32();

        switch (HeaderType)
        {
            case MpfHeaderType.Unknown:
                //read 4 bytes
                var headerBytes = reader.ReadBytes(4);

                //convert those bytes to a number
                var num = BitConverter.ToInt32(headerBytes);

                //if they equal "4", read 8 more bytes into the header
                if (num == 4)
                {
                    var moreBytes = reader.ReadBytes(8);

                    headerBytes = headerBytes.Concat(moreBytes)
                                             .ToArray();
                }

                UnknownHeaderBytes = headerBytes;

                break;
            default:
                stream.Seek(-4, SeekOrigin.Current);

                UnknownHeaderBytes = [];

                break;
        }

        var frameCount = reader.ReadByte();

        PixelWidth = reader.ReadInt16();
        PixelHeight = reader.ReadInt16();

        var dataLength = reader.ReadInt32();

        WalkFrameIndex = reader.ReadByte();
        WalkFrameCount = reader.ReadByte();

        FormatType = (MpfFormatType)reader.ReadInt16();

        switch (FormatType)
        {
            case MpfFormatType.MultipleAttacks:
                StandingFrameIndex = reader.ReadByte();
                StandingFrameCount = reader.ReadByte();
                OptionalAnimationFrameCount = reader.ReadByte();
                OptionalAnimationRatio = reader.ReadByte();
                AttackFrameIndex = reader.ReadByte();
                AttackFrameCount = reader.ReadByte();
                Attack2StartIndex = reader.ReadByte();
                Attack2FrameCount = reader.ReadByte();
                Attack3StartIndex = reader.ReadByte();
                Attack3FrameCount = reader.ReadByte();

                break;
            default:
                stream.Seek(-2, SeekOrigin.Current);

                AttackFrameIndex = reader.ReadByte();
                AttackFrameCount = reader.ReadByte();
                StandingFrameIndex = reader.ReadByte();
                StandingFrameCount = reader.ReadByte();
                OptionalAnimationFrameCount = reader.ReadByte();
                OptionalAnimationRatio = reader.ReadByte();

                break;
        }

        var dataStart = stream.Length - dataLength;

        using var dataSegment = stream.Slice(dataStart, dataLength);

        for (var i = 0; i < frameCount; ++i)
        {
            var left = reader.ReadInt16();
            var top = reader.ReadInt16();
            var right = reader.ReadInt16();
            var bottom = reader.ReadInt16();
            var centerX = reader.ReadInt16();
            var centerY = reader.ReadInt16();
            var startAddress = reader.ReadInt32();

            if ((left == -1) && (top == -1))
            {
                PaletteNumber = startAddress;
                --frameCount;

                continue;
            }

            var frameWidth = right - left;
            var frameHeight = bottom - top;

            Add(
                new MpfFrame
                {
                    Top = top,
                    Left = left,
                    Bottom = bottom,
                    Right = right,
                    CenterX = centerX,
                    CenterY = centerY,
                    StartAddress = startAddress,
                    Data = new byte[frameWidth * frameHeight]
                });
        }

        foreach (var frame in this)
        {
            dataSegment.Seek(frame.StartAddress, SeekOrigin.Begin);

            dataSegment.ReadExactly(frame.Data);
        }
    }

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".mpf"),
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

        if (HeaderType == MpfHeaderType.Unknown)
        {
            writer.Write((int)HeaderType);
            writer.Write(UnknownHeaderBytes);
        }

        var frameCount = (byte)(Count + 1);

        writer.Write(frameCount);
        writer.Write(PixelWidth);
        writer.Write(PixelHeight);
        writer.Write(this.Sum(frame => frame.Data.Length));
        writer.Write(WalkFrameIndex);
        writer.Write(WalkFrameCount);

        if (FormatType == MpfFormatType.MultipleAttacks)
        {
            writer.Write((short)FormatType);
            writer.Write(StandingFrameIndex);
            writer.Write(StandingFrameCount);
            writer.Write(OptionalAnimationFrameCount);
            writer.Write(OptionalAnimationRatio);
            writer.Write(AttackFrameIndex);
            writer.Write(AttackFrameCount);
            writer.Write(Attack2StartIndex);
            writer.Write(Attack2FrameCount);
            writer.Write(Attack3StartIndex);
            writer.Write(Attack3FrameCount);
        } else
        {
            writer.Write(AttackFrameIndex);
            writer.Write(AttackFrameCount);
            writer.Write(StandingFrameIndex);
            writer.Write(StandingFrameCount);
            writer.Write(OptionalAnimationFrameCount);
            writer.Write(OptionalAnimationRatio);
        }

        var startAddress = 0;

        foreach (var frame in this)
        {
            writer.Write(frame.Left);
            writer.Write(frame.Top);
            writer.Write(frame.Right);
            writer.Write(frame.Bottom);
            writer.Write(frame.CenterX);
            writer.Write(frame.CenterY);

            frame.StartAddress = startAddress;
            startAddress += frame.Data.Length;

            writer.Write(frame.StartAddress);
        }

        //write palette "frame"
        //byte.MaxValue == 0xFF, "-1" as a short is 0xFFFF
        var paletteFrameBuffer = Enumerable.Repeat(byte.MaxValue, 12)
                                           .ToArray();
        writer.Write(paletteFrameBuffer);
        writer.Write(PaletteNumber);

        foreach (var frame in this)
            writer.Write(frame.Data);
    }
    #endregion

    #region LoadFrom
    /// <summary>
    ///     Converts a sequence of fully colorized images to an MpfFile
    /// </summary>
    /// <param name="options">
    ///     Options to be used for quantization. EpfFiles can only have a maximum of 256 colors due to being a palettized
    ///     format
    /// </param>
    /// <param name="formatType">
    ///     The MpfFormat type of the resulting image
    /// </param>
    /// <param name="orderedFrames">
    ///     The ordered collection of SKImage frames.
    /// </param>
    /// <remarks>
    ///     The resulting MpfFile will have a palette number of 0, and the indexes/frame counts/stopMotionRatio will not be
    ///     set. These details will need to manually be set by you.
    /// </remarks>
    public static Palettized<MpfFile> FromImages(QuantizerOptions options, MpfFormatType formatType, IEnumerable<SKImage> orderedFrames)
        => FromImages(options, formatType, orderedFrames.ToArray());

    /// <summary>
    ///     Converts a collection of fully colorized images to an MpfFile
    /// </summary>
    /// <param name="options">
    ///     Options to be used for quantization. EpfFiles can only have a maximum of 256 colors due to being a palettized
    ///     format
    /// </param>
    /// <param name="formatType">
    ///     The MpfFormat type of the resulting image
    /// </param>
    /// <param name="orderedFrames">
    ///     The ordered collection of SKImage frames.
    /// </param>
    /// <remarks>
    ///     The resulting MpfFile will have a palette number of 0, and the indexes/frame counts/stopMotionRatio will not be
    ///     set. These details will need to manually be set by you.
    /// </remarks>
    public static Palettized<MpfFile> FromImages(QuantizerOptions options, MpfFormatType formatType, params SKImage[] orderedFrames)
    {
        ImageProcessor.PreserveNonTransparentBlacks(orderedFrames);

        using var quantized = ImageProcessor.QuantizeMultiple(options, orderedFrames);

        (var images, var palette) = quantized;

        // Crop all transparent edges and get the top-left offsets
        var anchorPoints = ImageProcessor.CropTransparentPixels(images);
        
        var imageWidth = (short)images.Max(img => img.Width);
        var imageHeight = (short)images.Max(img => img.Height);

        var mpfFile = new MpfFile(
            formatType == MpfFormatType.SingleAttack ? MpfHeaderType.None : MpfHeaderType.Unknown,
            formatType,
            imageWidth,
            imageHeight);

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var dims = anchorPoints[i];
            
            mpfFile.Add(
                new MpfFrame
                {
                    Top = (short)dims.Y,
                    Left = (short)dims.X,
                    Right = (short)(dims.X + image.Width),
                    Bottom = (short)(dims.Y + image.Height),
                    Data = image.GetPalettizedPixelData(palette)
                });
        }
        
        return new Palettized<MpfFile>
        {
            Entity = mpfFile,
            Palette = palette
        };
    }

    /// <summary>
    ///     Loads an MpfFile with the specified fileName from the specified archive
    /// </summary>
    /// <param name="fileName">
    ///     The name of the MPF file to extract from the archive.
    /// </param>
    /// <param name="archive">
    ///     The DataArchive from which to retreive the MPF file.
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if the MPF file with the specified name is not found in the archive.
    /// </exception>
    public static MpfFile FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".mpf"), out var entry))
            throw new FileNotFoundException($"MPF file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Loads an MpfFile from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the MpfFile from
    /// </param>
    /// <returns>
    /// </returns>
    public static MpfFile FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new MpfFile(segment);
    }

    /// <summary>
    ///     Loads an MpfFile from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file to be read.
    /// </param>
    public static MpfFile FromFile(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".mpf"),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        return new MpfFile(stream);
    }
    #endregion
}