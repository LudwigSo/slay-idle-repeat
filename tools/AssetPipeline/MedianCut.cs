using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// The managed colour reduction `15` §B4 step 6 substitutes for pngquant.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 A median cut is <b>not</b> pngquant. pngquant scores its output with a perceptual metric,
/// remaps with dithering and writes a PNG-8 palette; this reduces the colour count and hands the
/// result straight back to a PNG-32 encoder, so the only saving is what zlib gets out of fewer
/// distinct colours. <see cref="ExportStep"/> declares that as a deviation on every run.
/// </para>
/// <para>
/// 🔒 <b>Alpha is never quantised.</b> Only the colour channels are reduced, and only for pixels
/// that are visible at all — `15` §C's delivery format is straight alpha and a semi-transparent
/// pixel must survive the export byte for byte.
/// </para>
/// <para>
/// Deterministic throughout: the colours are gathered in row-major order, boxes split on the widest
/// channel at the population median, and ties break toward the lower packed colour. Nothing here
/// iterates a hash table's own order.
/// </para>
/// </remarks>
internal static class MedianCut
{
    /// <summary>A reduced image and how far it moved the pixels to get there.</summary>
    /// <param name="Image">The reduced image.</param>
    /// <param name="MeanError">The mean absolute per-channel difference, over visible pixels.</param>
    internal sealed record Reduction(Raster Image, double MeanError);

    /// <summary>How many distinct visible colours an image holds.</summary>
    /// <param name="image">The image to count.</param>
    internal static int DistinctColourCount(Raster image)
    {
        var seen = new HashSet<uint>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var colour = image.ColourAt(x, y);
                if (colour.Alpha > 0)
                {
                    seen.Add((uint)colour);
                }
            }
        }

        return seen.Count;
    }

    /// <summary>Reduces an image's visible colours to at most a stated budget.</summary>
    /// <param name="image">The image to reduce. Left untouched.</param>
    /// <param name="budget">How many colours the palette may hold. At least one.</param>
    internal static Reduction Reduce(Raster image, int budget)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (budget < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budget),
                budget,
                $"A palette of {budget} colours cannot represent an image. " +
                $"'{ThresholdKeys.ExportColourBudget}' is the pngquant substitute's palette size.");
        }

        var population = Population(image);
        if (population.Count == 0)
        {
            return new Reduction(image.Copy(), 0d);
        }

        var boxes = Split(population, budget);
        var palette = new Dictionary<uint, SKColor>();
        foreach (var box in boxes)
        {
            var representative = Representative(box);
            foreach (var entry in box)
            {
                palette[entry.Packed] = representative;
            }
        }

        var reduced = image.Copy();
        var error = 0d;
        var counted = 0L;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var colour = image.ColourAt(x, y);
                if (colour.Alpha == 0)
                {
                    continue;
                }

                var mapped = palette[(uint)colour];
                reduced.SetRgb(x, y, mapped.Red, mapped.Green, mapped.Blue);

                error += Math.Abs(colour.Red - mapped.Red)
                         + Math.Abs(colour.Green - mapped.Green)
                         + Math.Abs(colour.Blue - mapped.Blue);
                counted++;
            }
        }

        return new Reduction(reduced, counted == 0 ? 0d : error / (counted * 3d));
    }

    /// <summary>One distinct colour and how many pixels carry it.</summary>
    /// <param name="Packed">The colour, packed, so a box can key its palette by it.</param>
    /// <param name="Colour">The colour.</param>
    /// <param name="Weight">How many pixels carry it.</param>
    private sealed record Entry(uint Packed, SKColor Colour, int Weight);

    /// <summary>Every distinct visible colour, in ascending packed order.</summary>
    /// <param name="image">The image to gather from.</param>
    private static List<Entry> Population(Raster image)
    {
        var weights = new Dictionary<uint, int>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var colour = image.ColourAt(x, y);
                if (colour.Alpha == 0)
                {
                    continue;
                }

                var packed = (uint)colour;
                weights[packed] = weights.TryGetValue(packed, out var seen) ? seen + 1 : 1;
            }
        }

        return
        [
            .. weights
                .OrderBy(entry => entry.Key)
                .Select(entry => new Entry(entry.Key, (SKColor)entry.Key, entry.Value)),
        ];
    }

    /// <summary>Splits the population into at most <paramref name="budget"/> boxes.</summary>
    /// <param name="population">Every distinct colour, in ascending packed order.</param>
    /// <param name="budget">How many boxes to end with.</param>
    private static List<List<Entry>> Split(List<Entry> population, int budget)
    {
        var boxes = new List<List<Entry>> { population };

        while (boxes.Count < budget)
        {
            var chosen = -1;
            var widest = 0;

            for (var index = 0; index < boxes.Count; index++)
            {
                if (boxes[index].Count < 2)
                {
                    continue;
                }

                var extent = LongestAxis(boxes[index]).Extent;
                if (extent > widest)
                {
                    widest = extent;
                    chosen = index;
                }
            }

            if (chosen < 0)
            {
                // Every box holds one colour, or a run of identical ones: there is nothing left to
                // divide, and the palette is simply smaller than the budget allows.
                break;
            }

            var box = boxes[chosen];
            var axis = LongestAxis(box).Axis;
            var ordered = box
                .OrderBy(entry => Channel(entry.Colour, axis))
                .ThenBy(entry => entry.Packed)
                .ToList();
            var half = ordered.Count / 2;

            boxes[chosen] = ordered.GetRange(0, half);
            boxes.Insert(chosen + 1, ordered.GetRange(half, ordered.Count - half));
        }

        return boxes;
    }

    /// <summary>The channel a box varies most along, and by how much.</summary>
    /// <param name="box">The colours in the box. Never empty.</param>
    private static (int Axis, int Extent) LongestAxis(List<Entry> box)
    {
        var best = 0;
        var bestExtent = -1;

        for (var axis = 0; axis < 3; axis++)
        {
            var low = 255;
            var high = 0;
            foreach (var entry in box)
            {
                var value = Channel(entry.Colour, axis);
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }

            var extent = high - low;
            if (extent > bestExtent)
            {
                best = axis;
                bestExtent = extent;
            }
        }

        return (best, bestExtent);
    }

    /// <summary>The colour a box collapses to: its pixel-weighted mean.</summary>
    /// <param name="box">The colours in the box. Never empty.</param>
    private static SKColor Representative(List<Entry> box)
    {
        var red = 0d;
        var green = 0d;
        var blue = 0d;
        var weight = 0d;

        foreach (var entry in box)
        {
            red += entry.Colour.Red * (double)entry.Weight;
            green += entry.Colour.Green * (double)entry.Weight;
            blue += entry.Colour.Blue * (double)entry.Weight;
            weight += entry.Weight;
        }

        return new SKColor(
            Raster.ToChannel(red / weight),
            Raster.ToChannel(green / weight),
            Raster.ToChannel(blue / weight));
    }

    /// <summary>One channel of a colour, by axis index.</summary>
    /// <param name="colour">The colour.</param>
    /// <param name="axis">0 red, 1 green, 2 blue.</param>
    private static int Channel(SKColor colour, int axis) => axis switch
    {
        0 => colour.Red,
        1 => colour.Green,
        _ => colour.Blue,
    };
}
