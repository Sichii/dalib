using System;
using DALib.Definitions;
using SkiaSharp;

namespace DALib.Extensions;

/// <summary>
///     Provides extension methods for the <see cref="SKColor" /> class.
/// </summary>
public static class SKColorExtensions
{
    /// <summary>
    ///     Adjusts the brightness of an image by a percentage
    /// </summary>
    public static SKBitmap AdjustBrightness(this SKBitmap bitmap, float percent)
    {
        // @formatter:off
        using var filter = SKColorFilter.CreateColorMatrix([ percent, 0, 0, 0, 0,
                                                             0, percent, 0, 0, 0,
                                                             0, 0, percent, 0, 0,
                                                             0, 0, 0, 1, 0 ]);
        // @formatter:on

        var newBitmap = bitmap.Copy();
        using var canvas = new SKCanvas(newBitmap);

        using var paint = new SKPaint();
        paint.ColorFilter = filter;

        canvas.DrawBitmap(
            bitmap,
            0,
            0,
            paint);

        return newBitmap;
    }

    /// <summary>
    ///     Calculates the luminance of a color using the provided coefficient.
    /// </summary>
    /// <param name="color">
    ///     The color whose luminance is being calculated.
    /// </param>
    /// <param name="coefficient">
    ///     The coefficient to multiply the luminance by. Default value is 1.0f.
    /// </param>
    public static float GetLuminance(this SKColor color, float coefficient = 1.0f)
    {
        var gamma = 2.0f;

        // Convert from [0..255] to [0..1].
        var r = color.Red / 255f;
        var g = color.Green / 255f;
        var b = color.Blue / 255f;

        // Convert to linear space (approx).
        r = MathF.Pow(r, gamma);
        g = MathF.Pow(g, gamma);
        b = MathF.Pow(b, gamma);

        // Compute luminance in linear space.
        // (Either the older 0.299/0.587/0.114 or the Rec. 709 ones: 0.2126/0.7152/0.0722)
        /*var lumLinear = 0.2126f * r + 0.7152f * g + 0.0722f * b;*/
        var lumLinear = 0.299f * r + 0.587f * g + 0.114f * b;

        // Convert back to sRGB if needed.
        var lumSrgb = MathF.Pow(lumLinear, 1f / gamma);

        return (byte)Math.Clamp(MathF.Round(lumSrgb * 255f * coefficient), 0, 255);
    }

    public static SKColor GetRandomVividColor(Random? random = null)
    {
        random ??= Random.Shared;

        var hue = (float)(random.NextDouble() * 360.0f); // full hue range
        var saturation = 80 + (float)(random.NextDouble() * 20); // 80–100%
        var value = 80 + (float)(random.NextDouble() * 20); // 80–100%

        return SKColor.FromHsv(hue, saturation, value);
    }

    /// <summary>
    ///     Calculates the luminance of a color using the provided coefficient.
    /// </summary>
    /// <param name="color">
    ///     The color whose luminance is being calculated.
    /// </param>
    /// <param name="coefficient">
    ///     The coefficient to multiply the luminance by. Default value is 1.0f.
    /// </param>
    public static float GetSimpleLuminance(this SKColor color, float coefficient = 1.0f)
        => (0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue) * coefficient;

    /// <summary>
    ///     Checks if a color is close to black
    /// </summary>
    /// <param name="color">
    ///     The color to check
    /// </param>
    public static bool IsNearBlack(this SKColor color)
        => color is
        {
            Alpha: 255,
            Red: <= CONSTANTS.RGB555_COLOR_LOSS_FACTOR,
            Green: <= CONSTANTS.RGB555_COLOR_LOSS_FACTOR,
            Blue: <= CONSTANTS.RGB555_COLOR_LOSS_FACTOR
        };

    /// <summary>
    ///     Generates a random color within a given percent(positive or negative) of the given color
    /// </summary>
    /// <param name="color">
    /// </param>
    /// <param name="percent">
    /// </param>
    /// <returns>
    /// </returns>
    public static SKColor Randomize(this SKColor color, float percent = 0.1f)
    {
        var random = new Random();
        var halfPercent = percent / 2f;

        var rFactor = 1f + (float)(random.NextDouble() * percent - halfPercent);
        var gFactor = 1f + (float)(random.NextDouble() * percent - halfPercent);
        var bFactor = 1f + (float)(random.NextDouble() * percent - halfPercent);

        var r = (byte)Math.Clamp(color.Red * rFactor, 0, 255);
        var g = (byte)Math.Clamp(color.Green * gFactor, 0, 255);
        var b = (byte)Math.Clamp(color.Blue * bFactor, 0, 255);

        return new SKColor(
            r,
            g,
            b,
            color.Alpha);
    }

    /// <summary>
    ///     Returns a new SKColor with the alpha set based on the luminance of the color
    /// </summary>
    /// <param name="color">
    ///     An SKColor
    /// </param>
    /// <param name="coefficient">
    ///     The coefficient to multiply the luminance alpha by. Default value is 1.0f.
    /// </param>
    /// <returns>
    ///     A new SKColor with the alpha set.
    /// </returns>
    public static SKColor WithLuminanceAlpha(this SKColor color, float coefficient = 1.0f)
    {
        var luminance = color.GetLuminance(coefficient);

        return color.WithAlpha((byte)Math.Clamp(luminance, 0, byte.MaxValue));
    }
}