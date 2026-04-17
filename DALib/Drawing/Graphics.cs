#region
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
using DALib.Memory;
using DALib.Utility;
using SkiaSharp;
#endregion

namespace DALib.Drawing;

/// <summary>
///     Graphics class provides various methods for rendering images
/// </summary>
public static class Graphics
{
    private static Encoding? KoreanEncoding;

    /// <summary>
    ///     Decodes a per-channel (compression type 5) alpha surface. Each pixel is a uint16 in RGB555 format where each
    ///     channel is an independent alpha value (0-31). Since SKColor has only a single alpha channel, we approximate with
    ///     the max of the three channel alphas
    /// </summary>
    private static byte[] DecodePerChannelAlphaSurface(byte[] alphaData, int width, int height)
    {
        var pixelCount = width * height;
        var result = new byte[pixelCount];
        var offset = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            if ((offset + 1) >= alphaData.Length)
                break;

            var alphaPx = alphaData[offset] | (alphaData[offset + 1] << 8);
            offset += 2;

            var alphaR = (alphaPx >> 10) & 0x1F;
            var alphaG = (alphaPx >> 5) & 0x1F;
            var alphaB = alphaPx & 0x1F;
            var maxAlpha = Math.Max(alphaR, Math.Max(alphaG, alphaB));

            result[i] = (byte)Math.Min(255, maxAlpha * 255 / 31);
        }

        return result;
    }

    /// <summary>
    ///     Decodes a raw (compression type 7) alpha surface. Each pixel is a uint16 with a scalar alpha value (0-31)
    /// </summary>
    private static byte[] DecodeRawAlphaSurface(byte[] alphaData, int width, int height)
    {
        var pixelCount = width * height;
        var result = new byte[pixelCount];
        var offset = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            if ((offset + 1) >= alphaData.Length)
                break;

            var alpha16 = alphaData[offset] | (alphaData[offset + 1] << 8);
            result[i] = (byte)Math.Min(255, alpha16 * 255 / 31);
            offset += 2;
        }

        return result;
    }

    /// <summary>
    ///     Decodes an RLE-encoded (compression type 4) alpha surface. Format: uint32[height] row offset table, followed by
    ///     per-row streams of uint16 words where bits[15:8] = run count, bits[7:0] = alpha value (0-31)
    /// </summary>
    private static byte[] DecodeRleAlphaSurface(byte[] alphaData, int width, int height)
    {
        var result = new byte[width * height];

        //row offset table: uint32 per row
        var tableSize = height * 4;

        if (alphaData.Length < tableSize)
            return result;

        for (var row = 0; row < height; row++)
        {
            var rowOffset = alphaData[row * 4]
                            | (alphaData[row * 4 + 1] << 8)
                            | (alphaData[row * 4 + 2] << 16)
                            | (alphaData[row * 4 + 3] << 24);

            var col = 0;
            var rleOffset = rowOffset;

            while (col < width)
            {
                if ((rleOffset + 1) >= alphaData.Length)
                    break;

                var word = alphaData[rleOffset] | (alphaData[rleOffset + 1] << 8);
                rleOffset += 2;

                var count = (word >> 8) & 0xFF;
                var alpha = word & 0xFF;
                var scaledAlpha = (byte)Math.Min(255, alpha * 255 / 31);

                for (var i = 0; (i < count) && (col < width); i++, col++)
                    result[row * width + col] = scaledAlpha;
            }
        }

        return result;
    }

    /// <summary>
    ///     Draws a single glyph from a bitmap font into a premultiplied RGBA8888 pixel buffer. Position (x, y) is where column
    ///     0 of the glyph cell is placed
    /// </summary>
    /// <param name="font">
    ///     The bitmap font containing the glyph to draw
    /// </param>
    /// <param name="pixelBuffer">
    ///     The destination RGBA8888 pixel buffer
    /// </param>
    /// <param name="bufferWidth">
    ///     The pixel width of the destination buffer. Height is derived from buffer length
    /// </param>
    /// <param name="glyphIndex">
    ///     The index of the glyph to draw
    /// </param>
    /// <param name="x">
    ///     The x position in the buffer where column 0 of the glyph cell is placed
    /// </param>
    /// <param name="y">
    ///     The y position in the buffer where the top of the glyph is placed
    /// </param>
    /// <param name="color">
    ///     The color to draw the glyph in. Alpha is premultiplied internally
    /// </param>
    public static void DrawGlyph(
        FntFile font,
        Span<byte> pixelBuffer,
        int bufferWidth,
        int glyphIndex,
        int x,
        int y,
        SKColor color)
    {
        if (!font.IsValidIndex(glyphIndex))
            return;

        var bufferHeight = pixelBuffer.Length / (bufferWidth * 4);

        var a = color.Alpha;
        var r = (byte)(color.Red * a / 255);
        var g = (byte)(color.Green * a / 255);
        var b = (byte)(color.Blue * a / 255);

        var bytesPerRow = (font.GlyphWidth + 7) / 8;
        var bytesPerGlyph = bytesPerRow * font.GlyphHeight;
        var glyphOffset = glyphIndex * bytesPerGlyph;

        for (var row = 0; row < font.GlyphHeight; row++)
        {
            var pixelY = y + row;

            if ((uint)pixelY >= (uint)bufferHeight)
                continue;

            var rowOffset = glyphOffset + row * bytesPerRow;

            for (var byteIdx = 0; byteIdx < bytesPerRow; byteIdx++)
            {
                var dataByte = font.Data[rowOffset + byteIdx];

                if (dataByte == 0)
                    continue;

                for (var bit = 7; bit >= 0; bit--)
                {
                    if ((dataByte & (1 << bit)) == 0)
                        continue;

                    var pixelX = x + byteIdx * 8 + (7 - bit);

                    if ((uint)pixelX >= (uint)bufferWidth)
                        continue;

                    var pixelOffset = (pixelY * bufferWidth + pixelX) * 4;
                    pixelBuffer[pixelOffset] = r;
                    pixelBuffer[pixelOffset + 1] = g;
                    pixelBuffer[pixelOffset + 2] = b;
                    pixelBuffer[pixelOffset + 3] = a;
                }
            }
        }
    }

    /// <summary>
    ///     Maps a character to a glyph index within the specified font. Returns -1 if the character is not supported. English
    ///     fonts (94 glyphs) map ASCII 33-126. Korean fonts (2401 glyphs) map via EUC-KR codepage 949
    /// </summary>
    private static int GetGlyphIndex(FntFile font, char c)
    {
        //english
        if (font.GlyphCount == 94)
        {
            if (c is >= (char)33 and <= (char)126)
                return c - 33;

            return -1;
        }

        //hangul
        if (font.GlyphCount == 2401)
        {
            if (c <= 127)
                return -1;

            KoreanEncoding ??= Encoding.GetEncoding(949);

            Span<char> chars = [c];
            Span<byte> bytes = stackalloc byte[2];

            var count = KoreanEncoding.GetBytes(chars, bytes);

            if (count != 2)
                return -1;

            var lead = bytes[0];
            var trail = bytes[1];

            // Hangul Jamo: lead 0xA4, trail 0xA1-0xD3 -> indices 0-50
            if ((lead == 0xA4) && trail is >= 0xA1 and <= 0xD3)
                return trail - 0xA1;

            // Hangul syllables: lead 0xB0-0xC8, trail 0xA1-0xFE -> indices 51-2400
            if (lead is >= 0xB0 and <= 0xC8 && trail is >= 0xA1 and <= 0xFE)
                return 51 + (lead - 0xB0) * 94 + (trail - 0xA1);

            return -1;
        }

        // Unknown font type — try direct ASCII offset
        var index = c - 33;

        return font.IsValidIndex(index) ? index : -1;
    }

    /// <summary>
    ///     Measures the pixel width of a text string rendered with the specified bitmap font
    /// </summary>
    /// <param name="font">
    ///     The bitmap font to use for measurement
    /// </param>
    /// <param name="text">
    ///     The text string to measure
    /// </param>
    public static int MeasureText(FntFile font, string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var advance = font.GlyphWidth - 2;
        var maxWidth = 0;
        var currentWidth = 0;

        foreach (var c in text)
        {
            if (c == '\n')
            {
                if (currentWidth > maxWidth)
                    maxWidth = currentWidth;

                currentWidth = 0;

                continue;
            }

            currentWidth += advance;
        }

        if (currentWidth > maxWidth)
            maxWidth = currentWidth;

        return maxWidth;
    }

    /// <summary>
    ///     Renders a single HeaFile layer as an alpha overlay suitable for compositing onto a rendered map. Dark areas produce
    ///     a semi-opaque black overlay, and lit areas produce transparent or semi-transparent regions
    /// </summary>
    /// <param name="hea">
    ///     The HEA file to render
    /// </param>
    /// <param name="layerIndex">
    ///     The layer index to render (0 to LayerCount - 1)
    /// </param>
    /// <param name="darknessOpacity">
    ///     The alpha value (0-255) applied to fully dark pixels. 255 = fully opaque black overlay, 0 = no darkness overlay.
    ///     Light values reduce the alpha proportionally
    /// </param>
    public static SKImage RenderDarknessOverlay(HeaFile hea, int layerIndex, byte darknessOpacity = 200)
    {
        if ((layerIndex < 0) || (layerIndex >= hea.LayerCount))
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        var width = hea.GetLayerWidth(layerIndex);
        var height = hea.ScanlineCount;

        using var bitmap = new SKBitmap(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul);
        using var pixMap = bitmap.PeekPixels();
        var pixelBuffer = pixMap.GetPixelSpan<SKColor>();

        pixelBuffer.Fill(
            new SKColor(
                0,
                0,
                0,
                darknessOpacity));

        Span<byte> rowBuffer = stackalloc byte[width];

        for (var y = 0; y < height; y++)
        {
            hea.DecodeScanline(layerIndex, y, rowBuffer);

            var rowOffset = y * width;

            for (var x = 0; x < width; x++)
            {
                var value = rowBuffer[x];

                if (value == 0)
                    continue;

                var lightRatio = Math.Min(1.0f, (float)value / HeaFile.MAX_LIGHT_VALUE);
                var alpha = (byte)(darknessOpacity * (1.0f - lightRatio));

                pixelBuffer[rowOffset + x] = new SKColor(
                    0,
                    0,
                    0,
                    alpha);
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Renders all layers of a HeaFile stitched together as a full-width alpha overlay
    /// </summary>
    /// <param name="hea">
    ///     The HEA file to render
    /// </param>
    /// <param name="darknessOpacity">
    ///     The alpha value (0-255) applied to fully dark pixels. 255 = fully opaque black overlay, 0 = no darkness overlay.
    ///     Light values reduce the alpha proportionally
    /// </param>
    public static SKImage RenderDarknessOverlay(HeaFile hea, byte darknessOpacity = 200)
    {
        var totalWidth = hea.ScanlineWidth;
        var height = hea.ScanlineCount;

        using var bitmap = new SKBitmap(
            totalWidth,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul);
        using var pixMap = bitmap.PeekPixels();
        var pixelBuffer = pixMap.GetPixelSpan<SKColor>();

        pixelBuffer.Fill(
            new SKColor(
                0,
                0,
                0,
                darknessOpacity));

        Span<byte> rowBuffer = stackalloc byte[HeaFile.LAYER_STRIP_WIDTH];

        for (var layer = 0; layer < hea.LayerCount; layer++)
        {
            var layerWidth = hea.GetLayerWidth(layer);
            var xOffset = hea.Thresholds[layer];

            for (var y = 0; y < height; y++)
            {
                hea.DecodeScanline(layer, y, rowBuffer);

                var rowOffset = y * totalWidth;

                for (var x = 0; x < layerWidth; x++)
                {
                    var value = rowBuffer[x];

                    if (value == 0)
                        continue;

                    var lightRatio = Math.Min(1.0f, (float)value / HeaFile.MAX_LIGHT_VALUE);
                    var alpha = (byte)(darknessOpacity * (1.0f - lightRatio));

                    pixelBuffer[rowOffset + xOffset + x] = new SKColor(
                        0,
                        0,
                        0,
                        alpha);
                }
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Renders an EpfFrame
    /// </summary>
    /// <param name="frame">
    ///     The frame to render
    /// </param>
    /// <param name="palette">
    ///     A palette containing colors used by the frame
    /// </param>
    /// <param name="alphaType">
    ///     Alpha blending type. Defaults to Premul. Should be set to Unpremul for palettes >= 1000
    /// </param>
    public static SKImage RenderImage(EpfFrame frame, Palette palette, SKAlphaType alphaType = SKAlphaType.Premul)
    {
        //empty-frame marker (PixelWidth==0 || PixelHeight==0): return a 1x1 transparent image so
        //callers that iterate all frames of an EPF don't crash on SKBitmap(0,0). Equipment
        //renderers should short-circuit on PixelWidth/PixelHeight before reaching here.
        if ((frame.PixelWidth <= 0) || (frame.PixelHeight <= 0))
        {
            using var emptyBitmap = new SKBitmap(1, 1, SKColorType.Bgra8888, alphaType);
            emptyBitmap.Erase(CONSTANTS.Transparent);

            return SKImage.FromBitmap(emptyBitmap);
        }

        return SimpleRender(
            frame.Left,
            frame.Top,
            frame.PixelWidth,
            frame.PixelHeight,
            frame.Data,
            palette,
            alphaType);
    }

    /// <summary>
    ///     Renders an MpfFrame
    /// </summary>
    /// <param name="frame">
    ///     The frame to render
    /// </param>
    /// <param name="palette">
    ///     A palette containing colors used by the frame
    /// </param>
    public static SKImage RenderImage(MpfFrame frame, Palette palette)
        => SimpleRender(
            frame.Left,
            frame.Top,
            frame.PixelWidth,
            frame.PixelHeight,
            frame.Data,
            palette);

    /// <summary>
    ///     Renders an HpfFile
    /// </summary>
    /// <param name="hpf">
    ///     The file to render
    /// </param>
    /// <param name="palette">
    ///     A palette containing colors used by the frame
    /// </param>
    /// <param name="yOffset">
    ///     An optional custom offset used to move the image down, since these images are rendered from the bottom up
    /// </param>
    /// <remarks>
    ///     Some foreground tiles are marked as transparent via <see cref="TileFlags.Transparent" /> in sotp.dat. These tiles
    ///     should be blended using <see cref="SKBlendMode.Screen" /> (output = src + dst * (1 - src) per channel) rather than
    ///     the default SrcOver blend
    /// </remarks>
    public static SKImage RenderImage(HpfFile hpf, Palette palette, int yOffset = 0)
        => SimpleRender(
            0,
            yOffset,
            hpf.PixelWidth,
            hpf.PixelHeight,
            hpf.Data,
            palette);

    /// <summary>
    ///     Renders a palettized SPF frame
    /// </summary>
    /// <param name="spf">
    ///     The frame to render. Must be a palettized SpfFrame
    /// </param>
    /// <param name="spfPrimaryColorPalette">
    ///     The primary color palette of the SpfFile. (see SpfFile.Format)
    /// </param>
    public static SKImage RenderImage(SpfFrame spf, Palette spfPrimaryColorPalette)
        => SimpleRender(
            spf.Left,
            spf.Top,
            spf.PixelWidth,
            spf.PixelHeight,
            spf.Data!,
            spfPrimaryColorPalette);

    /// <summary>
    ///     Renders a palette
    /// </summary>
    public static SKImage RenderImage(Palette palette)
    {
        using var bitmap = new SKBitmap(16 * 5, 16 * 5);

        using (var canvas = new SKCanvas(bitmap))
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                {
                    var color = palette[x + y * 16];

                    using var paint = new SKPaint();
                    paint.Color = color;
                    paint.IsAntialias = true;

                    canvas.DrawRect(
                        x * 5,
                        y * 5,
                        5,
                        5,
                        paint);
                }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Renders a colorized SpfFrame
    /// </summary>
    /// <param name="spf">
    ///     The frame to render. Must be a colorized SpfFrame. (see SpfFile.Format)
    /// </param>
    public static SKImage RenderImage(SpfFrame spf)
        => SimpleRender(
            spf.Left,
            spf.Top,
            spf.PixelWidth,
            spf.PixelHeight,
            spf.ColorData!);

    /// <summary>
    ///     Renders an EfaFrame
    /// </summary>
    /// <param name="efa">
    ///     The frame to render
    /// </param>
    /// <param name="efaBlendingType">
    ///     The alpha blending type to use
    /// </param>
    public static SKImage RenderImage(EfaFrame efa, EfaBlendingType efaBlendingType = EfaBlendingType.Additive)
    {
        if ((efa.ByteCount == 0) || (efa.ByteWidth == 0))
        {
            using var emptyBitmap = new SKBitmap(
                Math.Max(1, (int)efa.ImagePixelWidth),
                Math.Max(1, (int)efa.ImagePixelHeight),
                SKColorType.Bgra8888,
                SKAlphaType.Unpremul);

            emptyBitmap.Erase(CONSTANTS.Transparent);

            return SKImage.FromBitmap(emptyBitmap);
        }

        //we will iterate over the data to render the image
        var dataWidth = efa.ByteWidth / 2;
        var dataHeight = efa.ByteCount / efa.ByteWidth;

        //when left/top are negative, skip the padding and shift pixels to 0
        var dstOffsetX = Math.Max(0, (int)efa.Left);
        var dstOffsetY = Math.Max(0, (int)efa.Top);
        var bitmapWidth = Math.Max(efa.ImagePixelWidth, dataWidth + dstOffsetX);
        var bitmapHeight = Math.Max(efa.ImagePixelHeight, dataHeight + dstOffsetY);

        using var bitmap = new SKBitmap(
            bitmapWidth,
            bitmapHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul);

        using var pixMap = bitmap.PeekPixels();
        var pixelBuffer = pixMap.GetPixelSpan<SKColor>();
        pixelBuffer.Fill(CONSTANTS.Transparent);

        //decode alpha surface before the pixel loop for SeparateAlpha and PerChannelAlpha blend types
        byte[]? perPixelAlpha = null;

        if (efa.AlphaData is { Length: > 0 } && efaBlendingType is EfaBlendingType.SeparateAlpha or EfaBlendingType.PerChannelAlpha)
            perPixelAlpha = efaBlendingType switch
            {
                //compression type 4 = RLE-encoded alpha, compression type 7 = raw uint16-per-pixel alpha
                EfaBlendingType.SeparateAlpha when efa.Unknown4 == 4 => DecodeRleAlphaSurface(efa.AlphaData, dataWidth, dataHeight),

                EfaBlendingType.SeparateAlpha => DecodeRawAlphaSurface(efa.AlphaData, dataWidth, dataHeight),

                //compression type 5 = per-channel alpha as RGB555 (2 bytes per pixel)
                EfaBlendingType.PerChannelAlpha => DecodePerChannelAlphaSurface(efa.AlphaData, dataWidth, dataHeight),

                // ReSharper disable once UnreachableSwitchArmDueToIntegerAnalysis
                _ => null
            };

        var reader = new SpanReader(Encoding.Default, efa.Data, Endianness.LittleEndian);

        for (var y = 0; y < dataHeight; y++)
            for (var x = 0; x < dataWidth; x++)
            {
                var xActual = x + dstOffsetX;
                var yActual = y + dstOffsetY;

                //read the RGB565 color
                var color = reader.ReadRgb565Color();

                //for some reason these images can have extra trash data on the right and bottom
                //we avoid it by obeying the frame pixel width/height vs padded x/y
                if ((x + efa.Left) >= efa.FramePixelWidth)
                    continue;

                if ((y + efa.Top) >= efa.FramePixelHeight)
                    continue;

                // the client uses different blend modes per type, all in RGB555 space
                // since pre-rendered images can't represent destination-dependent blending,
                // we approximate with max(R,G,B) as alpha (dark = transparent, bright = opaque)
                // which visually matches the result of both additive and self-alpha blends
                switch (efaBlendingType)
                {
                    case EfaBlendingType.Additive:
                    {
                        // Additive: keep full RGB, set alpha to 255. The renderer draws these
                        // with BlendState.Additive (src + dst), so black pixels add nothing
                        color = color.WithAlpha(255);

                        break;
                    }
                    case EfaBlendingType.SelfAlpha:
                    {
                        // SelfAlpha: original DA does per-channel blend (output = src + dst * (1 - src/255)).
                        color = color.WithAlpha(255);

                        break;
                    }
                    case EfaBlendingType.SeparateAlpha:
                    case EfaBlendingType.PerChannelAlpha:
                    {
                        if (perPixelAlpha is not null)
                        {
                            var alphaIndex = y * dataWidth + x;

                            color = color.WithAlpha(alphaIndex < perPixelAlpha.Length ? perPixelAlpha[alphaIndex] : (byte)0);
                        } else
                        {
                            //no alpha surface data available, fall back to max-channel approximation
                            var alpha = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
                            color = color.WithAlpha(alpha);
                        }

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(efaBlendingType), efaBlendingType, null);
                }

                pixelBuffer[yActual * bitmapWidth + xActual] = color;
            }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Renders a single horizontal strip layer from a HeaFile as a grayscale light map image. Light values are scaled from
    ///     the HEA range (0 to <see cref="HeaFile.MAX_LIGHT_VALUE" />) to full 0-255 intensity
    /// </summary>
    /// <param name="hea">
    ///     The HEA file to render
    /// </param>
    /// <param name="layerIndex">
    ///     The layer index to render (0 to LayerCount - 1)
    /// </param>
    public static SKImage RenderImage(HeaFile hea, int layerIndex)
    {
        if ((layerIndex < 0) || (layerIndex >= hea.LayerCount))
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        var width = hea.GetLayerWidth(layerIndex);
        var height = hea.ScanlineCount;

        using var bitmap = new SKBitmap(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul);
        using var pixMap = bitmap.PeekPixels();
        var pixelBuffer = pixMap.GetPixelSpan<SKColor>();
        pixelBuffer.Fill(SKColors.Black);

        Span<byte> rowBuffer = stackalloc byte[width];

        for (var y = 0; y < height; y++)
        {
            hea.DecodeScanline(layerIndex, y, rowBuffer);

            var rowOffset = y * width;

            for (var x = 0; x < width; x++)
            {
                var value = rowBuffer[x];

                if (value == 0)
                    continue;

                var intensity = (byte)Math.Min(255, value * 255 / HeaFile.MAX_LIGHT_VALUE);

                pixelBuffer[rowOffset + x] = new SKColor(
                    intensity,
                    intensity,
                    intensity,
                    255);
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Renders all layers of a HeaFile stitched together horizontally as a single grayscale light map image. The resulting
    ///     image has dimensions <see cref="HeaFile.ScanlineWidth" /> x <see cref="HeaFile.ScanlineCount" />
    /// </summary>
    /// <param name="hea">
    ///     The HEA file to render
    /// </param>
    public static SKImage RenderImage(HeaFile hea)
    {
        var totalWidth = hea.ScanlineWidth;
        var height = hea.ScanlineCount;

        using var bitmap = new SKBitmap(
            totalWidth,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul);
        using var pixMap = bitmap.PeekPixels();
        var pixelBuffer = pixMap.GetPixelSpan<SKColor>();
        pixelBuffer.Fill(SKColors.Black);

        Span<byte> rowBuffer = stackalloc byte[HeaFile.LAYER_STRIP_WIDTH];

        for (var layer = 0; layer < hea.LayerCount; layer++)
        {
            var layerWidth = hea.GetLayerWidth(layer);
            var xOffset = hea.Thresholds[layer];

            for (var y = 0; y < height; y++)
            {
                hea.DecodeScanline(layer, y, rowBuffer);

                var rowOffset = y * totalWidth;

                for (var x = 0; x < layerWidth; x++)
                {
                    var value = rowBuffer[x];

                    if (value == 0)
                        continue;

                    var intensity = (byte)Math.Min(255, value * 255 / HeaFile.MAX_LIGHT_VALUE);

                    pixelBuffer[rowOffset + xOffset + x] = new SKColor(
                        intensity,
                        intensity,
                        intensity,
                        255);
                }
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Renders a MapFile, given the archives that contain required data
    /// </summary>
    /// <param name="map">
    ///     The map file to render.
    /// </param>
    /// <param name="seoDat">
    ///     The SEO archive.
    /// </param>
    /// <param name="iaDat">
    ///     The IA archive.
    /// </param>
    /// <param name="foregroundPadding">
    ///     The amount of padding to add to the height of the file and beginning rendering position
    /// </param>
    /// <param name="cache">
    ///     A <see cref="MapImageCache" /> that can be reused to share <see cref="SKImageCache{TKey}" /> caches between
    ///     multiple map renderings.
    /// </param>
    public static SKImage RenderMap(
        MapFile map,
        DataArchive seoDat,
        DataArchive iaDat,
        int foregroundPadding = 512,
        MapImageCache? cache = null)
        => RenderMap(
            map,
            Tileset.FromArchive("tilea", seoDat),
            PaletteLookup.FromArchive("mpt", seoDat)
                         .Freeze(),
            PaletteLookup.FromArchive("stc", iaDat)
                         .Freeze(),
            iaDat,
            foregroundPadding,
            cache);

    /// <summary>
    ///     Renders a MapFile, given already extracted information
    /// </summary>
    /// <param name="map">
    ///     The <see cref="MapFile" /> to render
    /// </param>
    /// <param name="tiles">
    ///     A <see cref="Tileset" /> representing a collection of background tiles
    /// </param>
    /// <param name="bgPaletteLookup">
    ///     <see cref="PaletteLookup" /> for background tiles
    /// </param>
    /// <param name="fgPaletteLookup">
    ///     <see cref="PaletteLookup" /> for foreground tiles
    /// </param>
    /// <param name="iaDat">
    ///     IA <see cref="DataArchive" /> for reading foreground tile files
    /// </param>
    /// <param name="foregroundPadding">
    ///     The amount of padding to add to the height of the file and beginning rendering position
    /// </param>
    /// <param name="cache">
    ///     A <see cref="MapImageCache" /> that can be reused to share <see cref="SKImageCache{TKey}" /> caches between
    ///     multiple map renderings.
    /// </param>
    public static SKImage RenderMap(
        MapFile map,
        Tileset tiles,
        PaletteLookup bgPaletteLookup,
        PaletteLookup fgPaletteLookup,
        DataArchive iaDat,
        int foregroundPadding = 512,
        MapImageCache? cache = null)
    {
        var dispose = cache is null;
        cache ??= new MapImageCache();

        //create lookups so we only render each tile piece once
        using var bgCache = new SKImageCache<int>();
        using var lfgCache = new SKImageCache<int>();
        using var rfgCache = new SKImageCache<int>();

        //calculate width and height
        var width = (map.Width + map.Height + 1) * CONSTANTS.HALF_TILE_WIDTH;
        var height = (map.Width + map.Height + 1) * CONSTANTS.HALF_TILE_HEIGHT + foregroundPadding;
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        //the first tile drawn is the center tile at the top (0, 0)
        var bgInitialDrawX = (map.Height - 1) * CONSTANTS.HALF_TILE_WIDTH;
        var bgInitialDrawY = foregroundPadding;

        try
        {
            //render background tiles and draw them to the canvas
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var bgIndex = map.Tiles[x, y].Background;

                    if (bgIndex > 0)
                        --bgIndex;

                    var bgImage = cache.BackgroundCache.GetOrCreate(
                        bgIndex,
                        index =>
                        {
                            var palette = bgPaletteLookup.GetPaletteForId(index + 2);

                            return RenderTile(tiles[index], palette);
                        });

                    //for each X axis iteration, we want to move the draw position half a tile to the right and down from the initial draw position
                    var drawX = bgInitialDrawX + x * CONSTANTS.HALF_TILE_WIDTH;
                    var drawY = bgInitialDrawY + x * CONSTANTS.HALF_TILE_HEIGHT;
                    canvas.DrawImage(bgImage, drawX, drawY);
                }

                //for each Y axis iteration, we want to move the draw position half a tile to the left and down from the initial draw position
                bgInitialDrawX -= CONSTANTS.HALF_TILE_WIDTH;
                bgInitialDrawY += CONSTANTS.HALF_TILE_HEIGHT;
            }

            //load sotp flags for screen blend detection
            var sotpData = iaDat.TryGetValue("sotp.dat", out var sotpEntry)
                ? sotpEntry.ToSpan()
                           .ToArray()
                : [];

            using var screenBlendPaint = new SKPaint();
            screenBlendPaint.BlendMode = SKBlendMode.Screen;

            //render left and right foreground tiles and draw them to the canvas
            var fgInitialDrawX = (map.Height - 1) * CONSTANTS.HALF_TILE_WIDTH;
            var fgInitialDrawY = foregroundPadding;

            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var tile = map.Tiles[x, y];
                    var lfgIndex = tile.LeftForeground;
                    var rfgIndex = tile.RightForeground;

                    //render left foreground
                    var lfgImage = cache.ForegroundCache.GetOrCreate(
                        lfgIndex,
                        index =>
                        {
                            var hpf = HpfFile.FromArchive($"stc{index:D5}.hpf", iaDat);
                            var palette = fgPaletteLookup.GetPaletteForId(index + 1);

                            return RenderImage(hpf, palette);
                        });

                    //for each X axis iteration, we want to move the draw position half a tile to the right and down from the initial draw position
                    var lfgDrawX = fgInitialDrawX + x * CONSTANTS.HALF_TILE_WIDTH;

                    var lfgDrawY = fgInitialDrawY + (x + 1) * CONSTANTS.HALF_TILE_HEIGHT - lfgImage.Height + CONSTANTS.HALF_TILE_HEIGHT;

                    if (lfgIndex.IsRenderedTileIndex())
                        canvas.DrawImage(
                            lfgImage,
                            lfgDrawX,
                            lfgDrawY,
                            IsScreenBlendTile(lfgIndex) ? screenBlendPaint : null);

                    //render right foreground
                    var rfgImage = cache.ForegroundCache.GetOrCreate(
                        rfgIndex,
                        index =>
                        {
                            var hpf = HpfFile.FromArchive($"stc{index:D5}.hpf", iaDat);
                            var palette = fgPaletteLookup.GetPaletteForId(index + 1);

                            return RenderImage(hpf, palette);
                        });

                    //for each X axis iteration, we want to move the draw position half a tile to the right and down from the initial draw position
                    var rfgDrawX = fgInitialDrawX + (x + 1) * CONSTANTS.HALF_TILE_WIDTH;

                    var rfgDrawY = fgInitialDrawY + (x + 1) * CONSTANTS.HALF_TILE_HEIGHT - rfgImage.Height + CONSTANTS.HALF_TILE_HEIGHT;

                    if (rfgIndex.IsRenderedTileIndex())
                        canvas.DrawImage(
                            rfgImage,
                            rfgDrawX,
                            rfgDrawY,
                            IsScreenBlendTile(rfgIndex) ? screenBlendPaint : null);
                }

                //for each Y axis iteration, we want to move the draw position half a tile to the left and down from the initial draw position
                fgInitialDrawX -= CONSTANTS.HALF_TILE_WIDTH;
                fgInitialDrawY += CONSTANTS.HALF_TILE_HEIGHT;
            }

            return SKImage.FromBitmap(bitmap);

            bool IsScreenBlendTile(int fgIndex)
                => (fgIndex > 0) && ((fgIndex - 1) < sotpData.Length) && ((TileFlags)sotpData[fgIndex - 1]).HasFlag(TileFlags.Transparent);
        } finally
        {
            if (dispose)
                cache.Dispose();
        }
    }

    /// <summary>
    ///     Renders a text string to an SKImage using the specified bitmap font and color. The font type is detected
    ///     automatically from its glyph count: 94 glyphs = English (ASCII 33-126), 2401 glyphs = Korean (EUC-KR codepage 949
    ///     Jamo + syllables). Characters not supported by the font are rendered as blank space
    /// </summary>
    /// <param name="font">
    ///     The bitmap font to render with
    /// </param>
    /// <param name="text">
    ///     The text string to render
    /// </param>
    /// <param name="color">
    ///     The color to render the text in
    /// </param>
    public static SKImage RenderText(FntFile font, string text, SKColor color)
    {
        if (string.IsNullOrEmpty(text))
            text = " ";

        var advance = font.GlyphWidth - 2;
        var lineCount = 1 + text.Count(c => c == '\n');
        var totalWidth = Math.Max(1, MeasureText(font, text));
        var totalHeight = font.GlyphHeight * lineCount;

        var pixelBuffer = new byte[totalWidth * totalHeight * 4];
        var cursorX = 0;
        var cursorY = 0;

        foreach (var c in text)
        {
            if (c == '\n')
            {
                cursorX = 0;
                cursorY += font.GlyphHeight;

                continue;
            }

            var glyphIndex = GetGlyphIndex(font, c);

            if (glyphIndex >= 0)
                DrawGlyph(
                    font,
                    pixelBuffer,
                    totalWidth,
                    glyphIndex,
                    cursorX,
                    cursorY,
                    color);

            cursorX += advance;
        }

        using var bitmap = new SKBitmap(
            totalWidth,
            totalHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        Marshal.Copy(
            pixelBuffer,
            0,
            bitmap.GetPixels(),
            pixelBuffer.Length);

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Renders a Tile
    /// </summary>
    /// <param name="tile">
    ///     The tile to render
    /// </param>
    /// <param name="palette">
    ///     A palette containing colors used by the tile
    /// </param>
    public static SKImage RenderTile(Tile tile, Palette palette)
        => SimpleRender(
            0,
            0,
            tile.PixelWidth,
            tile.PixelHeight,
            tile.Data,
            palette);

    private static SKImage SimpleRender(
        int left,
        int top,
        int width,
        int height,
        SKColor[] data)
    {
        //when left/top are negative, skip the padding and shift pixels to 0
        var dstOffsetX = Math.Max(0, left);
        var dstOffsetY = Math.Max(0, top);
        var bitmapWidth = width + dstOffsetX;
        var bitmapHeight = height + dstOffsetY;

        using var bitmap = new SKBitmap(bitmapWidth, bitmapHeight);
        using var pixMap = bitmap.PeekPixels();

        var pixelBuffer = pixMap.GetPixelSpan<SKColor>();
        pixelBuffer.Fill(CONSTANTS.Transparent);

        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var xActual = x + dstOffsetX;
                var yActual = y + dstOffsetY;

                var pixelIndex = y * width + x;
                var color = data[pixelIndex];

                if ((color == CONSTANTS.Transparent) || (color == SKColors.Black))
                    continue;

                pixelBuffer[yActual * bitmapWidth + xActual] = color;
            }

        return SKImage.FromBitmap(bitmap);
    }

    private static SKImage SimpleRender(
        int left,
        int top,
        int width,
        int height,
        byte[] data,
        Palette palette,
        SKAlphaType alphaType = SKAlphaType.Premul)
    {
        //when left/top are negative, skip the padding and shift pixels to 0
        var dstOffsetX = Math.Max(0, left);
        var dstOffsetY = Math.Max(0, top);
        var bitmapWidth = width + dstOffsetX;
        var bitmapHeight = height + dstOffsetY;

        using var bitmap = new SKBitmap(
            bitmapWidth,
            bitmapHeight,
            SKColorType.Bgra8888,
            alphaType);
        using var pixMap = bitmap.PeekPixels();

        var pixelBuffer = pixMap.GetPixelSpan<SKColor>();
        pixelBuffer.Fill(CONSTANTS.Transparent);

        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var xActual = x + dstOffsetX;
                var yActual = y + dstOffsetY;

                var pixelIndex = y * width + x;
                var paletteIndex = data[pixelIndex];

                //if the paletteIndex is 0, and that color is pure black or transparent black
                var color = paletteIndex == 0 ? CONSTANTS.Transparent : palette[paletteIndex];

                pixelBuffer[yActual * bitmapWidth + xActual] = color;
            }

        return SKImage.FromBitmap(bitmap);
    }
}