using System;
using System.Linq;
using System.Text;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
using DALib.Memory;
using DALib.Utility;
using SkiaSharp;

namespace DALib.Drawing;

/// <summary>
///     Graphics class provides various methods for rendering images
/// </summary>
public static class Graphics
{
    /// <summary>
    ///     Renders an EpfFrame
    /// </summary>
    /// <param name="frame">
    ///     The frame to render
    /// </param>
    /// <param name="palette">
    ///     A palette containing colors used by the frame
    /// </param>
    public static SKImage RenderImage(EpfFrame frame, Palette palette)
        => SimpleRender(
            frame.Left,
            frame.Top,
            frame.PixelWidth,
            frame.PixelHeight,
            frame.Data,
            palette);

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
    /// <param name="transparency">
    ///     An optional flag to enable transparency. Some foreground images has luminosity based transparency. This is
    ///     controlled via SOTP with the <see cref="TileFlags.Transparent" /> flag
    /// </param>
    public static SKImage RenderImage(
        HpfFile hpf,
        Palette palette,
        int yOffset = 0,
        bool transparency = false)
    {
        if (transparency)
        {
            var semiTransparentColors = palette.Select(color => color.WithLuminanceAlpha());
            palette = new Palette(semiTransparentColors);
        }

        return SimpleRender(
            0,
            yOffset,
            hpf.PixelWidth,
            hpf.PixelHeight,
            hpf.Data,
            palette);
    }

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
    public static SKImage RenderImage(EfaFrame efa, EfaBlendingType efaBlendingType = EfaBlendingType.Luminance)
    {
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

        if (efa.ByteCount == 0)
            return SKImage.FromBitmap(bitmap);

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
                if (x + efa.Left >= efa.FramePixelWidth)
                    continue;

                if (y + efa.Top >= efa.FramePixelHeight)
                    continue;

                // set alpha based on luminance
                // this may be slightly off, but it's close enough for now
                var coefficient = efaBlendingType switch
                {
                    EfaBlendingType.Luminance     => 1f,
                    EfaBlendingType.LessLuminance => 1.25f, //more alpha is less transparent
                    EfaBlendingType.NotSure       => -1f,
                    _                             => throw new ArgumentOutOfRangeException(nameof(efaBlendingType), efaBlendingType, null)
                };

                //if the coefficient is positive, add luminance alpha to the color
                if (coefficient > 0)
                    color = color.WithLuminanceAlpha(coefficient);

                pixelBuffer[yActual * bitmapWidth + xActual] = color;
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
                        canvas.DrawImage(lfgImage, lfgDrawX, lfgDrawY);

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
                        canvas.DrawImage(rfgImage, rfgDrawX, rfgDrawY);
                }

                //for each Y axis iteration, we want to move the draw position half a tile to the left and down from the initial draw position
                fgInitialDrawX -= CONSTANTS.HALF_TILE_WIDTH;
                fgInitialDrawY += CONSTANTS.HALF_TILE_HEIGHT;
            }

            return SKImage.FromBitmap(bitmap);
        } finally
        {
            if (dispose)
                cache.Dispose();
        }
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
        Palette palette)
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
                var paletteIndex = data[pixelIndex];

                //if the paletteIndex is 0, and that color is pure black or transparent black
                var color = paletteIndex == 0 ? CONSTANTS.Transparent : palette[paletteIndex];

                pixelBuffer[yActual * bitmapWidth + xActual] = color;
            }

        return SKImage.FromBitmap(bitmap);
    }
}