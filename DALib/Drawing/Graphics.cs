﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DALib.Data;
using DALib.Definitions;
using DALib.Memory;
using DALib.Utility;
using SkiaSharp;

namespace DALib.Drawing;

public class Graphics
{
    public static SKImage RenderTile(
        Tile tile,
        Palette palette
    ) => SimpleRender(tile.Width, tile.Height, tile.Data, palette);
    
    public static SKImage RenderImage(
        MpfFrame frame,
        Palette palette
    ) => SimpleRender(frame.Width, frame.Height, frame.Data, palette);
    
    public static SKImage RenderImage(
        EpfFrame frame,
        Palette palette
    ) => SimpleRender(frame.Width, frame.Height, frame.Data, palette);
    
    public static SKImage RenderImage(
        HpfFile hpf,
        Palette palette
    ) => SimpleRender(hpf.Width, hpf.Height, hpf.Data, palette);

    public static SKImage RenderImage(EfaFrame efa)
    {
        using var bitmap = new SKBitmap(
            efa.ByteWidth / 2,
            efa.ByteCount / efa.ByteWidth,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        
        var reader = new SpanReader(Encoding.Default, efa.Data, Endianness.LittleEndian);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                //read the RGB565 color
                var color = reader.ReadUInt16();
            
                //scale the color to RGB888
                //@formatter:off
                var r = MathEx.ScaleRange<byte, byte>((byte)(color >> 11), 0, CONSTANTS.FIVE_BIT_MASK, 0, byte.MaxValue);
                var g = MathEx.ScaleRange<byte, byte>((byte)((color >> 5) & CONSTANTS.SIX_BIT_MASK), 0, CONSTANTS.SIX_BIT_MASK, 0, byte.MaxValue);
                var b = MathEx.ScaleRange<byte, byte>((byte)(color & CONSTANTS.FIVE_BIT_MASK), 0, CONSTANTS.FIVE_BIT_MASK, 0, byte.MaxValue);
                //@formatter:on

                // get perceived luminance of pixel
                var luminance = 0.299f * r + 0.587f * g + 0.114f * b;
                
                // set alpha based on luminance
                // TODO: may need adjusting
                var adjustedColor = new SKColor(
                    r,
                    g,
                    b,
                    Convert.ToByte(luminance));

                bitmap.SetPixel(x, y, adjustedColor);
            }
        }
        
        return SKImage.FromBitmap(bitmap);
    }
    
    public static SKImage RenderMap(
        MapFile map,
        DataArchive seoDat,
        DataArchive iaDat
    ) => RenderMap(
        map,
        Tileset.FromArchive("tilea", seoDat),
        PaletteLookup.FromArchive("mpt", seoDat),
        PaletteLookup.FromArchive("stc", iaDat),
        iaDat);

    public static SKImage RenderMap(
        MapFile map,
        Tileset tiles,
        PaletteLookup bgPaletteLookup,
        PaletteLookup fgPaletteLookup,
        DataArchive iaDat
    )
    {
        const int FOREGROUND_PADDING = 256;
        
        //create lookups so we only render each tile piece once
        var bgLookup = new Dictionary<int, SKImage>();
        var lfgLookup = new Dictionary<int, SKImage>();
        var rfgLookup = new Dictionary<int, SKImage>();
        
        //calculate width and height based on orthogonal view
        var width = CONSTANTS.TILE_WIDTH + (map.Width - 1) * CONSTANTS.TILE_WIDTH + FOREGROUND_PADDING;
        var height = CONSTANTS.PADDED_TILE_HEIGHT + (map.Height - 1) * CONSTANTS.PADDED_TILE_HEIGHT + FOREGROUND_PADDING;
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        //the first tile drawn is the center tile at the top (0, 0)
        var bgInitialDrawX = width / 2 - CONSTANTS.TILE_WIDTH / 2;
        var bgInitialDrawY = FOREGROUND_PADDING;
        
        //render background tiles and draw them to the canvas
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var bgIndex = map.Tiles[x, y].Background;

                if (bgIndex > 0)
                    --bgIndex;

                if (!bgLookup.TryGetValue(bgIndex, out var bgImage))
                {
                    var palette = bgPaletteLookup.GetPaletteForId(bgIndex + 2);
                    bgImage = RenderTile(tiles[bgIndex], palette);

                    bgLookup[bgIndex] = bgImage;
                }

                //for each X axis iteration, we want to move the draw position half a tile to the right and down from the initial draw position
                var drawX = bgInitialDrawX + x * (CONSTANTS.TILE_WIDTH / 2);
                var drawY = bgInitialDrawY + x * (CONSTANTS.PADDED_TILE_HEIGHT / 2);

                canvas.DrawImage(bgImage, drawX, drawY);
            }
            
            //for each Y axis iteration, we want to move the draw position half a tile to the left and down from the initial draw position
            bgInitialDrawX -= CONSTANTS.TILE_WIDTH / 2;
            bgInitialDrawY += CONSTANTS.PADDED_TILE_HEIGHT / 2;
        }
        
        //render left and right foreground tiles and draw them to the canvas
        var fgInitialDrawX = width / 2 - CONSTANTS.TILE_WIDTH / 2;
        var fgInitialDrawY = FOREGROUND_PADDING;
        
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var tile = map.Tiles[x, y];
                var lfgIndex = tile.LeftForeground;
                var rfgIndex = tile.RightForeground;
                
                //render left foreground
                if (!lfgLookup.TryGetValue(lfgIndex, out var lfgImage))
                {
                    var hpf = HpfFile.FromArchive($"stc{lfgIndex:D5}.hpf", iaDat);
                    lfgImage = RenderImage(hpf, fgPaletteLookup.GetPaletteForId(lfgIndex + 1));

                    lfgLookup[lfgIndex] = lfgImage;
                }
                
                //for each X axis iteration, we want to move the draw position half a tile to the right and down from the initial draw position
                var lfgDrawX = fgInitialDrawX + x * (CONSTANTS.TILE_WIDTH / 2);
                var lfgDrawY = fgInitialDrawY + (x + 1) * (CONSTANTS.PADDED_TILE_HEIGHT / 2) - lfgImage.Height + CONSTANTS.PADDED_TILE_HEIGHT / 2;

                if (lfgIndex % 10000 > 1)
                    canvas.DrawImage(lfgImage, lfgDrawX, lfgDrawY);

                //render right foreground
                if (!rfgLookup.TryGetValue(rfgIndex, out var rfgImage))
                {
                    var hpf = HpfFile.FromArchive($"stc{rfgIndex:D5}.hpf", iaDat);
                    rfgImage = RenderImage(hpf, fgPaletteLookup.GetPaletteForId(rfgIndex + 1));

                    rfgLookup[rfgIndex] = rfgImage;
                }

                //for each X axis iteration, we want to move the draw position half a tile to the right and down from the initial draw position
                var rfgDrawX = fgInitialDrawX + (x + 1) * (CONSTANTS.TILE_WIDTH / 2);
                var rfgDrawY = fgInitialDrawY + (x + 1) * (CONSTANTS.PADDED_TILE_HEIGHT / 2) - rfgImage.Height + CONSTANTS.PADDED_TILE_HEIGHT / 2;

                if (rfgIndex % 10000 > 1)
                    canvas.DrawImage(rfgImage, rfgDrawX, rfgDrawY);
            }
            
            //for each Y axis iteration, we want to move the draw position half a tile to the left and down from the initial draw position
            fgInitialDrawX -= CONSTANTS.TILE_WIDTH / 2;
            fgInitialDrawY += CONSTANTS.PADDED_TILE_HEIGHT / 2;
        }
        
        //canvas.Flush();
        return SKImage.FromBitmap(bitmap);
    }
    
    private static SKImage SimpleRender(
        int width,
        int height,
        byte[] data,
        Palette palette
    )
    {
        using var bitmap = new SKBitmap(width, height);
        
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixelIndex = y * width + x;
                var paletteIndex = data[pixelIndex];
                //apparently palette index 0 is transparent
                //TODO: check if this is also the case for SpfFile
                var color = paletteIndex == 0 ? SKColors.Transparent : palette[paletteIndex];
                
                bitmap.SetPixel(x, y, color);
            }
        }
        
        return SKImage.FromBitmap(bitmap);
    }
}