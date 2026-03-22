#region
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
#endregion

namespace DALib.Drawing.Virtualized;

/// <summary>
///     A lightweight view over an MPF file in a DataArchive. Parses the header, animation metadata, and frame table of
///     contents on construction; individual frame pixel data is read on demand from the underlying archive entry.
/// </summary>
public sealed class MpfView
{
    private readonly long DataSectionOffset;
    private readonly DataArchiveEntry Entry;
    private readonly MpfTocEntry[] Toc;

    /// <summary>
    ///     The number of frames for the second attack animation
    /// </summary>
    public byte Attack2FrameCount { get; }

    /// <summary>
    ///     The starting frame index of the second attack animation
    /// </summary>
    public byte Attack2StartIndex { get; }

    /// <summary>
    ///     The number of frames for the third attack animation
    /// </summary>
    public byte Attack3FrameCount { get; }

    /// <summary>
    ///     The starting frame index of the third attack animation
    /// </summary>
    public byte Attack3StartIndex { get; }

    /// <summary>
    ///     The number of frames for the primary attack animation
    /// </summary>
    public byte AttackFrameCount { get; }

    /// <summary>
    ///     The starting frame index of the primary attack animation
    /// </summary>
    public byte AttackFrameIndex { get; }

    /// <summary>
    ///     The number of frames in the standing animation including optional frames. If your normal standing animation has 4
    ///     frames, but there are 2 extra frames that should occasionally be played, then you would put 6 here. (4 normal
    ///     frames + 2 optional frames). If there is no optional animation, this will have a value of 0.
    /// </summary>
    public byte OptionalAnimationFrameCount { get; }

    /// <summary>
    ///     Specifies the ratio of playing the optional standing frames. For example, if this is set to 30, it will play the
    ///     optional frames 30% of the time
    /// </summary>
    public byte OptionalAnimationRatio { get; }

    /// <summary>
    ///     The palette number used to colorize this image
    /// </summary>
    public int PaletteNumber { get; }

    /// <summary>
    ///     The pixel height of the image
    /// </summary>
    public short PixelHeight { get; }

    /// <summary>
    ///     The pixel width of the image
    /// </summary>
    public short PixelWidth { get; }

    /// <summary>
    ///     The number of frames for the standing animation without the optional frames
    /// </summary>
    public byte StandingFrameCount { get; }

    /// <summary>
    ///     The starting frame index of the standing animation
    /// </summary>
    public byte StandingFrameIndex { get; }

    /// <summary>
    ///     The number of frames for the walking animation
    /// </summary>
    public byte WalkFrameCount { get; }

    /// <summary>
    ///     The starting frame index of the walking animation
    /// </summary>
    public byte WalkFrameIndex { get; }

    /// <summary>
    ///     The number of frames in the MPF file
    /// </summary>
    public int Count => Toc.Length;

    private MpfView(
        DataArchiveEntry entry,
        long dataSectionOffset,
        MpfTocEntry[] toc,
        short pixelWidth,
        short pixelHeight,
        int paletteNumber,
        byte walkFrameIndex,
        byte walkFrameCount,
        byte attackFrameIndex,
        byte attackFrameCount,
        byte attack2StartIndex,
        byte attack2FrameCount,
        byte attack3StartIndex,
        byte attack3FrameCount,
        byte standingFrameIndex,
        byte standingFrameCount,
        byte optionalAnimationFrameCount,
        byte optionalAnimationRatio)
    {
        Entry = entry;
        DataSectionOffset = dataSectionOffset;
        Toc = toc;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        PaletteNumber = paletteNumber;
        WalkFrameIndex = walkFrameIndex;
        WalkFrameCount = walkFrameCount;
        AttackFrameIndex = attackFrameIndex;
        AttackFrameCount = attackFrameCount;
        Attack2StartIndex = attack2StartIndex;
        Attack2FrameCount = attack2FrameCount;
        Attack3StartIndex = attack3StartIndex;
        Attack3FrameCount = attack3FrameCount;
        StandingFrameIndex = standingFrameIndex;
        StandingFrameCount = standingFrameCount;
        OptionalAnimationFrameCount = optionalAnimationFrameCount;
        OptionalAnimationRatio = optionalAnimationRatio;
    }

    /// <summary>
    ///     Creates an MpfView with the specified fileName from the specified archive
    /// </summary>
    /// <param name="fileName">
    ///     The name of the MPF file to extract from the archive.
    /// </param>
    /// <param name="archive">
    ///     The DataArchive from which to retrieve the MPF file.
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if the MPF file with the specified name is not found in the archive.
    /// </exception>
    public static MpfView FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".mpf"), out var entry))
            throw new FileNotFoundException($"MPF file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Creates an MpfView from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the MpfView from
    /// </param>
    public static MpfView FromEntry(DataArchiveEntry entry)
    {
        using var stream = entry.ToStreamSegment();
        using var reader = new BinaryReader(stream, Encoding.Default, true);

        // Parse variable-length header
        var headerType = (MpfHeaderType)reader.ReadInt32();

        switch (headerType)
        {
            case MpfHeaderType.Unknown:
                var num = reader.ReadInt32();

                if (num == 4)
                    stream.Seek(8, SeekOrigin.Current);

                break;
            default:
                stream.Seek(-4, SeekOrigin.Current);

                break;
        }

        var frameCount = reader.ReadByte();
        var pixelWidth = reader.ReadInt16();
        var pixelHeight = reader.ReadInt16();
        var dataLength = reader.ReadInt32();
        var walkFrameIndex = reader.ReadByte();
        var walkFrameCount = reader.ReadByte();
        var formatType = (MpfFormatType)reader.ReadInt16();

        byte standingFrameIndex,
             standingFrameCount,
             optionalAnimationFrameCount,
             optionalAnimationRatio,
             attackFrameIndex,
             attackFrameCount,
             attack2StartIndex = 0,
             attack2FrameCount = 0,
             attack3StartIndex = 0,
             attack3FrameCount = 0;

        switch (formatType)
        {
            case MpfFormatType.MultipleAttacks:
                standingFrameIndex = reader.ReadByte();
                standingFrameCount = reader.ReadByte();
                optionalAnimationFrameCount = reader.ReadByte();
                optionalAnimationRatio = reader.ReadByte();
                attackFrameIndex = reader.ReadByte();
                attackFrameCount = reader.ReadByte();
                attack2StartIndex = reader.ReadByte();
                attack2FrameCount = reader.ReadByte();
                attack3StartIndex = reader.ReadByte();
                attack3FrameCount = reader.ReadByte();

                break;
            default:
                stream.Seek(-2, SeekOrigin.Current);
                attackFrameIndex = reader.ReadByte();
                attackFrameCount = reader.ReadByte();
                standingFrameIndex = reader.ReadByte();
                standingFrameCount = reader.ReadByte();
                optionalAnimationFrameCount = reader.ReadByte();
                optionalAnimationRatio = reader.ReadByte();

                break;
        }

        var dataSectionOffset = stream.Length - dataLength;
        var paletteNumber = 0;
        var tocEntries = new List<MpfTocEntry>(frameCount);

        for (var i = 0; i < frameCount; i++)
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
                paletteNumber = startAddress;
                frameCount--;

                continue;
            }

            tocEntries.Add(
                new MpfTocEntry(
                    top,
                    left,
                    bottom,
                    right,
                    centerX,
                    centerY,
                    startAddress));
        }

        return new MpfView(
            entry,
            dataSectionOffset,
            tocEntries.ToArray(),
            pixelWidth,
            pixelHeight,
            paletteNumber,
            walkFrameIndex,
            walkFrameCount,
            attackFrameIndex,
            attackFrameCount,
            attack2StartIndex,
            attack2FrameCount,
            attack3StartIndex,
            attack3FrameCount,
            standingFrameIndex,
            standingFrameCount,
            optionalAnimationFrameCount,
            optionalAnimationRatio);
    }

    /// <summary>
    ///     Reads and returns the frame at the specified index. Pixel data is read from the archive on each access.
    /// </summary>
    public MpfFrame this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Toc.Length);

            var toc = Toc[index];
            var width = toc.Right - toc.Left;
            var height = toc.Bottom - toc.Top;
            var data = new byte[width * height];

            using var stream = Entry.ToStreamSegment();
            stream.Seek(DataSectionOffset + toc.StartAddress, SeekOrigin.Begin);
            stream.ReadExactly(data);

            return new MpfFrame
            {
                Top = toc.Top,
                Left = toc.Left,
                Bottom = toc.Bottom,
                Right = toc.Right,
                CenterX = toc.CenterX,
                CenterY = toc.CenterY,
                StartAddress = toc.StartAddress,
                Data = data
            };
        }
    }

    /// <summary>
    ///     Attempts to read the frame at the specified index. Returns false if the index is out of range.
    /// </summary>
    public bool TryGetValue(int index, out MpfFrame? frame)
    {
        if ((index < 0) || (index >= Toc.Length))
        {
            frame = null;

            return false;
        }

        frame = this[index];

        return true;
    }

    private readonly record struct MpfTocEntry(
        short Top,
        short Left,
        short Bottom,
        short Right,
        short CenterX,
        short CenterY,
        int StartAddress);
}