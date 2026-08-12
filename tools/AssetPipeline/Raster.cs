using System.Runtime.InteropServices;
using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// A tightly packed RGBA byte buffer, and the only place this project converts to and from
/// <see cref="SKBitmap"/>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Everything here is <see cref="SKColorType.Rgba8888"/> / <see cref="SKAlphaType.Unpremul"/>.
/// Straight alpha is `15` §C's delivery format and round-trips bit-for-bit through Skia's PNG
/// encoder; a premultiplied surface anywhere in the chain destroys colour in low-alpha pixels,
/// which is exactly the halo `15` §B4 step 1 exists to remove. <see cref="From"/> therefore refuses
/// any other surface rather than converting one silently.
/// </para>
/// <para>
/// The bytes are tightly packed (no row padding) so an index is pure arithmetic; Skia's own buffer
/// carries a stride, which is why the two are copied row by row rather than wholesale.
/// </para>
/// </remarks>
internal sealed class Raster
{
    /// <summary>RGBA, one byte each.</summary>
    private const int BytesPerPixel = 4;

    private readonly byte[] pixels;

    private Raster(byte[] pixels, int width, int height)
    {
        this.pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>The width in pixels.</summary>
    internal int Width { get; }

    /// <summary>The height in pixels.</summary>
    internal int Height { get; }

    /// <summary>How many pixels the buffer holds.</summary>
    internal int PixelCount => Width * Height;

    /// <summary>The one image description this pipeline works in.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    internal static SKImageInfo InfoFor(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    /// <summary>A fully transparent buffer.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    internal static Raster Blank(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"A {width}×{height} raster has no pixels to process.");
        }

        return new Raster(new byte[width * height * BytesPerPixel], width, height);
    }

    /// <summary>Reads a bitmap's pixels, or refuses a surface this pipeline does not work in.</summary>
    /// <param name="bitmap">An Rgba8888 / Unpremul bitmap.</param>
    internal static Raster From(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (bitmap.ColorType != SKColorType.Rgba8888 || bitmap.AlphaType != SKAlphaType.Unpremul)
        {
            throw new InvalidOperationException(
                $"This pipeline works in {SKColorType.Rgba8888} / {SKAlphaType.Unpremul} end to " +
                $"end and was handed {bitmap.ColorType} / {bitmap.AlphaType}. `15` §C's delivery " +
                "format is straight alpha, and a premultiplied surface destroys colour in " +
                "low-alpha pixels — the very halo §B4 step 1 removes. Converting here would hide " +
                "that, so it is refused instead.");
        }

        var source = bitmap.Bytes;
        var stride = bitmap.RowBytes;
        var rowLength = bitmap.Width * BytesPerPixel;
        var target = new byte[bitmap.Width * bitmap.Height * BytesPerPixel];

        for (var y = 0; y < bitmap.Height; y++)
        {
            Array.Copy(source, y * stride, target, y * rowLength, rowLength);
        }

        return new Raster(target, bitmap.Width, bitmap.Height);
    }

    /// <summary>An independent copy.</summary>
    internal Raster Copy() => new((byte[])pixels.Clone(), Width, Height);

    /// <summary>Writes these pixels into a new bitmap.</summary>
    internal SKBitmap ToBitmap()
    {
        var bitmap = new SKBitmap(InfoFor(Width, Height));
        var destination = bitmap.GetPixels(out var length);

        if (destination == IntPtr.Zero || (long)length < (long)bitmap.RowBytes * Height)
        {
            throw new InvalidOperationException(
                $"Skia allocated no writable {Width}×{Height} surface for the step's output.");
        }

        var rowLength = Width * BytesPerPixel;
        for (var y = 0; y < Height; y++)
        {
            Marshal.Copy(pixels, y * rowLength, IntPtr.Add(destination, y * bitmap.RowBytes), rowLength);
        }

        return bitmap;
    }

    /// <summary>True when the coordinates are inside the buffer.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    internal bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>The zero-based index of a pixel, for a mask that shares this geometry.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    internal int IndexOf(int x, int y) => (y * Width) + x;

    /// <summary>One pixel's alpha byte.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    internal byte AlphaAt(int x, int y) => pixels[Offset(x, y) + 3];

    /// <summary>One pixel, as a colour.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    internal SKColor ColourAt(int x, int y)
    {
        var offset = Offset(x, y);
        return new SKColor(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    /// <summary>Writes one pixel.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="colour">The colour to write, alpha included.</param>
    internal void SetColour(int x, int y, SKColor colour)
    {
        var offset = Offset(x, y);
        pixels[offset] = colour.Red;
        pixels[offset + 1] = colour.Green;
        pixels[offset + 2] = colour.Blue;
        pixels[offset + 3] = colour.Alpha;
    }

    /// <summary>Writes one pixel's colour channels, leaving its alpha byte exactly as it was.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="red">The red channel.</param>
    /// <param name="green">The green channel.</param>
    /// <param name="blue">The blue channel.</param>
    internal void SetRgb(int x, int y, byte red, byte green, byte blue)
    {
        var offset = Offset(x, y);
        pixels[offset] = red;
        pixels[offset + 1] = green;
        pixels[offset + 2] = blue;
    }

    /// <summary>Copies one pixel out of another raster of any size.</summary>
    /// <param name="source">Where to read from.</param>
    /// <param name="sourceX">The source column.</param>
    /// <param name="sourceY">The source row.</param>
    /// <param name="x">The destination column.</param>
    /// <param name="y">The destination row.</param>
    internal void CopyPixelFrom(Raster source, int sourceX, int sourceY, int x, int y) =>
        SetColour(x, y, source.ColourAt(sourceX, sourceY));

    /// <summary>The bounding box of every pixel with a non-zero alpha, or empty.</summary>
    /// <remarks>Left and top inclusive, right and bottom exclusive, as everywhere in this project.</remarks>
    internal SKRectI OpaqueBounds()
    {
        var left = Width;
        var top = Height;
        var right = 0;
        var bottom = 0;

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (AlphaAt(x, y) == 0)
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

    /// <summary>Euclidean distance between two colours in RGB, ignoring alpha.</summary>
    /// <param name="first">One colour.</param>
    /// <param name="second">The other.</param>
    internal static double RgbDistance(SKColor first, SKColor second)
    {
        double red = first.Red - second.Red;
        double green = first.Green - second.Green;
        double blue = first.Blue - second.Blue;
        return Math.Sqrt((red * red) + (green * green) + (blue * blue));
    }

    /// <summary>A 0-255 channel from a double, rounded away from zero and clamped.</summary>
    /// <param name="value">The value to quantise.</param>
    internal static byte ToChannel(double value) =>
        (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0d, 255d);

    private int Offset(int x, int y)
    {
        if (!Contains(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x), $"({x}, {y}) is outside a {Width}×{Height} raster.");
        }

        return ((y * Width) + x) * BytesPerPixel;
    }
}
