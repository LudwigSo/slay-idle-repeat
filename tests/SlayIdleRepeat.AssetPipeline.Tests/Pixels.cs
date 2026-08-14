using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// Reading pixels out of a bitmap so a case can state an exact expectation about them.
/// </summary>
/// <remarks>
/// 🔒 <see cref="AlphaBytes"/> and <see cref="RgbBytes"/> go through the raw pixel buffer rather
/// than <c>GetPixel</c>: "the alpha channel is bit-identical" is a claim about bytes, and a
/// per-pixel accessor that round-tripped through <see cref="SKColor"/> could hide a conversion.
/// </remarks>
internal static class Pixels
{
    /// <summary>Every alpha byte, row-major.</summary>
    /// <param name="bitmap">An Rgba8888 bitmap.</param>
    internal static byte[] AlphaBytes(SKBitmap bitmap) => ChannelBytes(bitmap, [3]);

    /// <summary>Every red, green and blue byte, row-major.</summary>
    /// <param name="bitmap">An Rgba8888 bitmap.</param>
    internal static byte[] RgbBytes(SKBitmap bitmap) => ChannelBytes(bitmap, [0, 1, 2]);

    /// <summary>Every pixel as a colour, row-major.</summary>
    /// <param name="bitmap">Any bitmap.</param>
    internal static IReadOnlyList<SKColor> AllPixels(SKBitmap bitmap)
    {
        var colours = new SKColor[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                colours[(y * bitmap.Width) + x] = bitmap.GetPixel(x, y);
            }
        }

        return colours;
    }

    /// <summary>How many distinct colours a bitmap holds — a vacuity floor for "pixel-identical".</summary>
    /// <param name="bitmap">Any bitmap.</param>
    internal static int DistinctColourCount(SKBitmap bitmap) => AllPixels(bitmap).Distinct().Count();

    /// <summary>Euclidean distance between two colours in RGB, ignoring alpha.</summary>
    /// <param name="first">One colour.</param>
    /// <param name="second">The other.</param>
    internal static double RgbDistance(SKColor first, SKColor second)
    {
        double dr = first.Red - second.Red;
        double dg = first.Green - second.Green;
        double db = first.Blue - second.Blue;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    /// <summary>The mean RGB distance from a set of pixels to one colour.</summary>
    /// <param name="bitmap">The image to sample.</param>
    /// <param name="points">The pixels to sample. Must not be empty.</param>
    /// <param name="target">The colour to measure against.</param>
    internal static double MeanRgbDistance(
        SKBitmap bitmap, IReadOnlyList<SKPointI> points, SKColor target) =>
        points.Count == 0
            ? throw new InvalidOperationException(
                "MeanRgbDistance over no pixels would make any claim true. Floor the collection " +
                "in the case before calling this.")
            : points.Average(p => RgbDistance(bitmap.GetPixel(p.X, p.Y), target));

    /// <summary>The colours at a set of pixels, in the order given.</summary>
    /// <param name="bitmap">The image to sample.</param>
    /// <param name="points">The pixels to sample.</param>
    internal static IReadOnlyList<SKColor> ColoursAt(
        SKBitmap bitmap, IReadOnlyList<SKPointI> points) =>
        [.. points.Select(p => bitmap.GetPixel(p.X, p.Y))];

    /// <summary>The alpha values at a set of pixels, in the order given.</summary>
    /// <param name="bitmap">The image to sample.</param>
    /// <param name="points">The pixels to sample.</param>
    internal static IReadOnlyList<byte> AlphaAt(SKBitmap bitmap, IReadOnlyList<SKPointI> points) =>
        [.. points.Select(p => bitmap.GetPixel(p.X, p.Y).Alpha)];

    /// <summary>
    /// The bounding box of every pixel with a non-zero alpha: left/top inclusive, right/bottom
    /// exclusive. Empty when nothing is visible, which a case must floor against.
    /// </summary>
    /// <param name="bitmap">Any bitmap.</param>
    internal static SKRectI OpaqueBounds(SKBitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = 0;
        var bottom = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return right == 0 && bottom == 0 ? SKRectI.Empty : new SKRectI(left, top, right, bottom);
    }

    /// <summary>
    /// Decodes PNG bytes back to Rgba8888 / Unpremul, so a straight-alpha round trip can be
    /// compared byte for byte.
    /// </summary>
    /// <param name="png">The encoded bytes.</param>
    internal static SKBitmap DecodePngAsStraightAlpha(byte[] png)
    {
        using var data = SKData.CreateCopy(png);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidOperationException(
                $"The {png.Length} bytes step 6 produced are not a decodable image at all.");

        var info = new SKImageInfo(
            codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());

        return result == SKCodecResult.Success
            ? bitmap
            : throw new InvalidOperationException(
                $"Decoding step 6's output as straight alpha returned {result}.");
    }

    private static byte[] ChannelBytes(SKBitmap bitmap, int[] offsets)
    {
        var buffer = bitmap.Bytes;
        var stride = bitmap.RowBytes;
        var bytes = new byte[bitmap.Width * bitmap.Height * offsets.Length];
        var next = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                foreach (var offset in offsets)
                {
                    bytes[next++] = buffer[(y * stride) + (x * 4) + offset];
                }
            }
        }

        return bytes;
    }
}
