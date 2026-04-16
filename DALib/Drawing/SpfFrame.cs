using SkiaSharp;

namespace DALib.Drawing;

/// <summary>
///     Represents an SPF (Sprite Pixel Format) frame.
/// </summary>
public sealed class SpfFrame
{
    /// <summary>
    ///     The highest Y coordinate of the frame.
    /// </summary>
    public ushort Bottom { get; set; }

    /// <summary>
    ///     The number of bytes of image data this frame contains
    /// </summary>
    /// <remarks>
    ///     For Palettized images, this will be equal to Height * Width
    ///     <br />
    ///     For Colorized images, this will be equal to Height * Width * 4 (2 bytes per pixel, 2 copies of the image data)
    /// </remarks>
    public uint ByteCount { get; set; }

    /// <summary>
    ///     The width of the image in bytes.
    /// </summary>
    /// <remarks>
    ///     For Palettized images, this will be equal to Width
    ///     <br />
    ///     For Colorized images, this will be equal to Width * 2 (2 bytes per pixel)
    /// </remarks>
    public uint ByteWidth { get; set; }

    /// <summary>
    ///     The X coordinate of the anchor point in canvas space. Used for alignment when rendering (e.g. projectile sprites).
    ///     Only valid when <see cref="HasCenterPoint" /> is true. Stored at TOC+0x08 in the file.
    /// </summary>
    public short CenterX { get; set; }

    /// <summary>
    ///     The Y coordinate of the anchor point in canvas space. Used for alignment when rendering (e.g. projectile sprites).
    ///     Only valid when <see cref="HasCenterPoint" /> is true. Stored at TOC+0x0A in the file.
    /// </summary>
    public short CenterY { get; set; }

    /// <summary>
    ///     If colorized, the colorized pixel data of the frame (the RGB565 data scaled to RGB888)
    /// </summary>
    public SKColor[]? ColorData { get; set; }

    /// <summary>
    ///     If palettized, the palettized pixel data of the frame (the palette indexes)
    /// </summary>
    public byte[]? Data { get; set; }

    /// <summary>
    ///     Whether this frame has valid center point data in <see cref="CenterX" /> and <see cref="CenterY" />.
    /// </summary>
    public bool HasCenterPoint { get; set; }

    /// <summary>
    ///     The number of byte per image
    /// </summary>
    /// <remarks>
    ///     For Palettized images, this will be equal to Height * Width
    ///     <br />
    ///     For Colorized images, this will be equal to Height * Width * 2 (2 bytes per pixel)
    /// </remarks>
    public uint ImageByteCount { get; set; }

    /// <summary>
    ///     The lowest X coordinate of the frame.
    /// </summary>
    public ushort Left { get; set; }

    /// <summary>
    ///     The highest X coordinate of the frame.
    /// </summary>
    public ushort Right { get; set; }

    /// <summary>
    ///     The address within the data segment of the file where the image data for this frame can be found
    /// </summary>
    public uint StartAddress { get; set; }

    /// <summary>
    ///     The lowest Y coordinate of the frame.
    /// </summary>
    public ushort Top { get; set; }

    /// <summary>
    ///     The pixel height of the frame
    /// </summary>
    public int PixelHeight => Bottom - Top;

    /// <summary>
    ///     The pixel width of the frame
    /// </summary>
    public int PixelWidth => Right - Left;
}