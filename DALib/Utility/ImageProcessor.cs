using System;
using System.Collections.Generic;
using System.Linq;
using DALib.Definitions;
using DALib.Extensions;
using KGySoft.Drawing.Imaging;
using KGySoft.Drawing.SkiaSharp;
using SkiaSharp;
using Palette = DALib.Drawing.Palette;

namespace DALib.Utility;

/// <summary>
///     Provides methods for processing images
/// </summary>
public static class ImageProcessor
{
    /// <summary>
    ///     Creates a mosaic image by combining multiple images horizontally.
    /// </summary>
    /// <param name="padding">
    ///     The padding between each image in pixels. Default value is 1.
    /// </param>
    /// <param name="images">
    ///     The images to be combined into a mosaic.
    /// </param>
    public static SKImage CreateMosaic(int padding = 1, params SKImage[] images)
    {
        var width = images.Sum(img => img.Width) + (images.Length - 1) * padding;
        var height = images.Max(img => img.Height);

        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            var x = 0;

            foreach (var image in images)
            {
                canvas.DrawImage(image, x, 0);

                x += image.Width + padding;
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Preserves non-transparent black pixels in the given image by converting them to a very dark gray (1, 1, 1)
    /// </summary>
    /// <param name="image">
    ///     The image whose black pixels to preserve
    /// </param>
    public static SKImage PreserveNonTransparentBlacks(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);

        PreserveNonTransparentBlacks(bitmap);

        var ret = SKImage.FromBitmap(bitmap);
        
        return ret;
    }

    /// <summary>
    ///     Preserves non-transparent black pixels in the given image by converting them to a very dark gray (1, 1, 1)
    /// </summary>
    /// <param name="bitmap">
    ///     The image whose black pixels to preserve
    /// </param>
    public static void PreserveNonTransparentBlacks(SKBitmap bitmap)
    {
        using var canvas = new SKCanvas(bitmap);

        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);

                if (color.IsNearBlack())
                    canvas.DrawPoint(x, y, CONSTANTS.RGB555_ALMOST_BLACK);
            }
    }

    /// <summary>
    ///     Preserves non-transparent black pixels in the given images by converting them to a very dark gray (1, 1, 1)
    /// </summary>
    /// <param name="images">
    ///     The images whose black pixels to preserve
    /// </param>
    public static void PreserveNonTransparentBlacks(IList<SKImage> images)
    {
        for (var i = 0; i < images.Count; i++)
            images[i] = PreserveNonTransparentBlacks(images[i]);
    }
    
    
    /// <summary>
    ///     Crops an image to remove transparent pixels from all edges and returns the top-left offset
    /// </summary>
    /// <param name="image">
    ///     The image to crop
    /// </param>
    /// <param name="topLeft">
    ///     The top-left offset of the cropped content relative to the original image
    /// </param>
    /// <returns>
    ///     A new image with transparent pixels removed from all edges, or the original image if it's already fully cropped
    /// </returns>
    public static SKImage CropTransparentPixels(SKImage image, out SKPoint topLeft)
    {
        using var bitmap = SKBitmap.FromImage(image);

        var transparentColor = SKColors.Black;
        
        var width = bitmap.Width;
        var height = bitmap.Height;
        
        // Find the bounding box of non-transparent pixels
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                
                // Check if pixel is not transparent
                if (pixel != transparentColor)
                {
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }
        
        // If no non-transparent pixels found, return a 1x1 transparent image
        if ((maxX < minX) || (maxY < minY))
        {
            using var emptyBitmap = new SKBitmap(1, 1);
            emptyBitmap.SetPixel(0, 0, transparentColor);
            
            topLeft = new SKPoint(0, 0);
            image.Dispose();
            
            return SKImage.FromBitmap(emptyBitmap);
        }
        
        // Store the top-left offset
        topLeft = new SKPoint(minX, minY);
        
        // Calculate the cropped dimensions
        var croppedWidth = maxX - minX + 1;
        var croppedHeight = maxY - minY + 1;
        
        // If the image is already fully cropped, return the original
        if ((minX == 0) && (minY == 0) && (croppedWidth == width) && (croppedHeight == height))
        {
            topLeft = new SKPoint(0, 0);
            return image;
        }

        // Create a new bitmap with the cropped dimensions
        using var croppedBitmap = new SKBitmap(croppedWidth, croppedHeight);
        
        // Extract the subset from the original bitmap
        bitmap.ExtractSubset(
            croppedBitmap,
            new SKRectI(minX, minY, maxX + 1, maxY + 1));
        
        var result = SKImage.FromBitmap(croppedBitmap);
        
        // Dispose the original image since we're returning a new one
        image.Dispose();
        
        return result;
    }

    /// <summary>
    /// Crops transparent pixels from all edges of each image in the provided list and returns the top-left offsets.
    /// </summary>
    /// <param name="images">
    /// The list of images to process, where each image will have its transparent pixels cropped.
    /// </param>
    /// <returns>
    /// An array of SKPoint containing the top-left offset for each cropped image
    /// </returns>
    public static SKPoint[] CropTransparentPixels(IList<SKImage> images)
    {
        var offsets = new SKPoint[images.Count];
        
        for (var i = 0; i < images.Count; i++)
            images[i] = CropTransparentPixels(images[i], out offsets[i]);

        return offsets;
    }

    /// <summary>
    ///     Quantizes the given image to the specified color palette.
    /// </summary>
    /// <param name="image">
    ///     The image to be quantized.
    /// </param>
    /// <param name="palette">
    ///     The target color palette to quantize the image to.
    /// </param>
    /// <param name="ditherer">
    ///     Optional ditherer to apply during quantization. If null, no dithering will be applied.
    /// </param>
    /// <returns>
    ///     A new SKImage quantized to the specified palette.
    /// </returns>
    /// <remarks>
    ///     This method maps each pixel in the source image to the closest color in the provided palette.
    /// </remarks>
    public static SKImage QuantizeToPalette(SKImage image, Palette palette, IDitherer? ditherer = null)
    {
        using var bitmap = SKBitmap.FromImage(image);

        // Convert DALib Palette to KGySoft Color32 array
        var paletteColors = palette.Select(c => c.ToColor32()).ToArray();

        // Create a KGySoft Palette from Color32 array
        var kgPalette = new KGySoft.Drawing.Imaging.Palette(paletteColors);

        // Create a quantizer with the predefined palette
        IQuantizer quantizer = PredefinedColorsQuantizer.FromCustomPalette(kgPalette);
        var source = bitmap.GetReadableBitmapData();

        using var qSession = quantizer.Initialize(source);
        using var quantizedBitmap = new SKBitmap(image.Info);
        using var quantizedPixMap = quantizedBitmap.PeekPixels();

        var pixelBuffer = quantizedPixMap.GetPixelSpan<SKColor>();
        pixelBuffer.Fill(CONSTANTS.Transparent);

        if (ditherer is not null)
        {
            using var dSession = ditherer.Initialize(source, qSession);

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);

                    var ditheredColor = dSession.GetDitheredColor(color.ToColor32(), x, y)
                                                .ToSKColor();

                    pixelBuffer[y * image.Width + x] = ditheredColor;
                }
            }
        }
        else
        {
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);

                    pixelBuffer[y * image.Width + x] = qSession.GetQuantizedColor(color.ToColor32())
                                                               .ToSKColor();
                }
            }
        }

        return SKImage.FromBitmap(quantizedBitmap);
    }

    /// <summary>
    ///     Quantizes the given image using the specified quantizer options.
    /// </summary>
    /// <param name="options">
    ///     The quantizer options.
    /// </param>
    /// <param name="image">
    ///     The image to be quantized.
    /// </param>
    /// <remarks>
    ///     Quantization is the process of reducing the number of colors in an image. This method uses the Wu algorithm
    /// </remarks>
    public static Palettized<SKImage> Quantize(QuantizerOptions options, SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        IQuantizer quantizer = OptimizedPaletteQuantizer.Wu(options.MaxColors, alphaThreshold: 0);
        var source = bitmap.GetReadableBitmapData();

        using var qSession = quantizer.Initialize(source);
        using var quantizedBitmap = new SKBitmap(image.Info);
        var existingPixels = bitmap.Pixels;

        var colorCount = existingPixels.Distinct()
                                       .Count();

        using var quantizedPixMap = quantizedBitmap.PeekPixels();

        var pixelBuffer = quantizedPixMap.GetPixelSpan<SKColor>();
        pixelBuffer.Fill(CONSTANTS.Transparent);

        //don't quantize/dither if the image already has less than maximum number of colors
        if (colorCount <= options.MaxColors)
        {
            //all colors must be fully opaque or fully transparent
            existingPixels = existingPixels.Select(
                                               pixel =>
                                               {
                                                   //normally the quantizer would set all fully transparent pixels to black
                                                   //but since we didn't quantize the image, we have to do it manually
                                                   if (pixel.Alpha == 0)
                                                       return SKColors.Black;

                                                   //all other colors are set to be fully opaque
                                                   return pixel.WithAlpha(byte.MaxValue);
                                               })
                                           .ToArray();

            existingPixels.CopyTo(pixelBuffer);
        } else if (options.Ditherer is not null)
        {
            using var dSession = options.Ditherer!.Initialize(source, qSession);

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);

                    var ditheredColor = dSession.GetDitheredColor(color.ToColor32(), x, y)
                                                .ToSKColor();

                    pixelBuffer[y * image.Width + x] = ditheredColor;
                }
            }
        } else
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);

                    pixelBuffer[y * image.Width + x] = qSession.GetQuantizedColor(color.ToColor32())
                                                               .ToSKColor();
                }
            }

        var quantizedImage = SKImage.FromBitmap(quantizedBitmap);
        Palette? palette;

        if (colorCount <= options.MaxColors)
            palette = new Palette(
                existingPixels.Distinct()
                              .OrderBy(c => c.GetLuminance()));
        else
            palette = qSession.Palette!.ToDALibPalette();

        return new Palettized<SKImage>
        {
            Entity = quantizedImage,
            Palette = palette
        };
    }

    /// <summary>
    /// Merges multiple palettes into a single palette.
    /// </summary>
    /// <param name="palettes">
    /// The collection of palettes to be merged. Each palette is represented as an enumerable of colors.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the merged palette exceeds the maximum number of colors allowed.
    /// </exception>
    public static Palette MergePalettes(params IEnumerable<Palette> palettes)
    {
        var uniqueColors = palettes.SelectMany(p => p)
                                   .Distinct()
                                   .ToList();

        if (uniqueColors.Count > CONSTANTS.COLORS_PER_PALETTE)
            throw new InvalidOperationException("Merged palette has more than 256 colors");

        return new Palette(uniqueColors);
    }


    /// <summary>
    ///     Quantizes multiple images to the specified color palette.
    /// </summary>
    /// <param name="palette">
    ///     The target color palette to quantize the images to.
    /// </param>
    /// <param name="ditherer">
    ///     Optional ditherer to apply during quantization. If null, no dithering will be applied.
    /// </param>
    /// <param name="images">
    ///     The images to be quantized.
    /// </param>
    /// <returns>
    ///     A collection of SKImages quantized to the specified palette.
    /// </returns>
    /// <remarks>
    ///     All provided images will be quantized together to ensure consistent color mapping across all images.
    /// </remarks>
    public static SKImageCollection QuantizeMultipleToPalette(Palette palette, IDitherer? ditherer = null, params SKImage[] images)
    {
        const int PADDING = 1;

        //create a mosaic of all the individual images
        using var mosaic = CreateMosaic(PADDING, images);
        using var quantizedMosaic = QuantizeToPalette(mosaic, palette, ditherer);
        using var bitmap = SKBitmap.FromImage(quantizedMosaic);

        var quantizedImages = new SKImageCollection([]);
        var x = 0;

        for (var i = 0; i < images.Length; i++)
        {
            var originalImage = images[i];
            using var quantizedBitmap = new SKBitmap(originalImage.Info);

            //extract the quantized parts out of the mosaic
            bitmap.ExtractSubset(
                quantizedBitmap,
                new SKRectI(
                    x,
                    0,
                    x + originalImage.Width,
                    originalImage.Height));

            x += quantizedBitmap.Width + PADDING;

            var image = SKImage.FromBitmap(quantizedBitmap);
            quantizedImages.Add(image);
        }

        return quantizedImages;
    }

    /// <summary>
    ///     Quantizes the given images using the specified quantizer options
    /// </summary>
    /// <param name="options">
    ///     The quantizer options.
    /// </param>
    /// <param name="images">
    ///     The images to be quantized.
    /// </param>
    /// <remarks>
    ///     Quantization is the process of reducing the number of colors in an image. This method uses the Wu algorithm. All
    ///     provided images will be quantized together so that the resulting palette is the same for all images
    /// </remarks>
    public static Palettized<SKImageCollection> QuantizeMultiple(QuantizerOptions options, params SKImage[] images)
    {
        const int PADDING = 1;

        //create a mosaic of all the individual images
        using var mosaic = CreateMosaic(PADDING, images);
        using var quantizedMosaic = Quantize(options, mosaic);
        using var bitmap = SKBitmap.FromImage(quantizedMosaic.Entity);

        var quantizedImages = new SKImageCollection([]);
        var x = 0;

        for (var i = 0; i < images.Length; i++)
        {
            var originalImage = images[i];
            using var quantizedBitmap = new SKBitmap(originalImage.Info);

            //extract the quantized parts out of the mosaic
            bitmap.ExtractSubset(
                quantizedBitmap,
                new SKRectI(
                    x,
                    0,
                    x + originalImage.Width,
                    originalImage.Height));

            x += quantizedBitmap.Width + PADDING;

            var image = SKImage.FromBitmap(quantizedBitmap);
            quantizedImages.Add(image);
        }

        return new Palettized<SKImageCollection>
        {
            Entity = quantizedImages,
            Palette = quantizedMosaic.Palette
        };
    }
}