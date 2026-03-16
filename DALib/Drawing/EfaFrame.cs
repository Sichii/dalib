using DALib.Definitions;

namespace DALib.Drawing;

/// <summary>
///     Represents a frame in an EfaFile
/// </summary>
public sealed class EfaFrame
{
    /// <summary>
    ///     Optional alpha surface data that follows the RGB pixel data in the decompressed stream. Present for
    ///     <see cref="EfaBlendingType.SeparateAlpha" /> and <see cref="EfaBlendingType.PerChannelAlpha" /> blend types. For
    ///     SeparateAlpha, this is either raw or RLE-encoded per-pixel alpha (0-31 range). For PerChannelAlpha, this is
    ///     per-channel alpha encoded as RGB555 (2 bytes per pixel)
    /// </summary>
    public byte[]? AlphaData { get; set; }

    /// <summary>
    ///     The number of bytes of colorized data this frame contains
    /// </summary>
    public int ByteCount { get; set; }

    /// <summary>
    ///     The width in bytes of the colorized data this frame contains. (This is pixel width * 2 since pixels are encoded as
    ///     RGB565 (2 bytes))
    /// </summary>
    public int ByteWidth { get; set; }

    /// <summary>
    ///     The X coordinate of the image that lines up with the center of the draw area. The image will be reflected on this X
    ///     coordinate when facing left or right
    /// </summary>
    /// <remarks>
    ///     This value is generally exactly(or very close to) ImageWidth / 2
    /// </remarks>
    public short CenterX { get; set; }

    /// <summary>
    ///     The Y coordinate of the image that lines up with the center of the draw area. Generally, the bottom of the image
    ///     sits on the center of the draw area
    /// </summary>
    /// <remarks>
    ///     This value is generally close to ImageHeight. Depending on how much space was left at the bottom of the frame vs
    ///     the total height of the image, you may need to subtract a small number of pixels (1-15)
    /// </remarks>
    public short CenterY { get; set; }

    /// <summary>
    ///     The size in bytes of the frame data when it is compressed
    /// </summary>
    public int CompressedSize { get; set; }

    /// <summary>
    ///     The pixel data of the frame encoded as RGB565
    /// </summary>
    public required byte[] Data { get; set; }

    /// <summary>
    ///     The size in bytes of the frame data when it is decompressed
    /// </summary>
    public int DecompressedSize { get; set; }

    /// <summary>
    ///     The pixel height of the frame
    /// </summary>
    public short FramePixelHeight { get; set; }

    /// <summary>
    ///     The pixel width of the frame
    /// </summary>
    public short FramePixelWidth { get; set; }

    /// <summary>
    ///     The pixel height of the image this frame is part of
    /// </summary>
    public short ImagePixelHeight { get; set; }

    /// <summary>
    ///     The pixel width of the image this frame is part of
    /// </summary>
    public short ImagePixelWidth { get; set; }

    /// <summary>
    ///     The lowest X coordinate of the frame.
    /// </summary>
    public short Left { get; set; }

    /// <summary>
    ///     The address within the data segment of the file where the compressed image data for this frame can be found
    /// </summary>
    public int StartAddress { get; set; }

    /// <summary>
    ///     The lowest Y coordinate of the frame.
    /// </summary>
    public short Top { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public int Unknown1 { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public int Unknown2 { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public int Unknown3 { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public int Unknown4 { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public int Unknown5 { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public int Unknown6 { get; set; }

    /// <summary>
    ///     A value that has an unknown use LI: figure out what this is for
    /// </summary>
    public int Unknown7 { get; set; }
}