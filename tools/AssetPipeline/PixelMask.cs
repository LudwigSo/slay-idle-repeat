namespace SlayIdleRepeat.AssetPipeline;

/// <summary>A per-pixel boolean set, with the set operations the pipeline steps need.</summary>
/// <remarks>
/// Morphology is expressed through one exact Euclidean distance transform rather than a hand-rolled
/// structuring-element loop: dilation by <c>r</c> is "within <c>r</c> of the set" and erosion by
/// <c>r</c> is "further than <c>r</c> from the complement" — exact at every radius instead of
/// approximating a disc with a square.
/// </remarks>
internal sealed class PixelMask
{
    private readonly bool[] cells;

    /// <summary>An empty mask of the given size.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    internal PixelMask(int width, int height)
    {
        Width = width;
        Height = height;
        cells = new bool[width * height];
    }

    private PixelMask(bool[] cells, int width, int height)
    {
        this.cells = cells;
        Width = width;
        Height = height;
    }

    /// <summary>The width in pixels.</summary>
    internal int Width { get; }

    /// <summary>The height in pixels.</summary>
    internal int Height { get; }

    /// <summary>How many pixels are set.</summary>
    internal int Count
    {
        get
        {
            var total = 0;
            foreach (var cell in cells)
            {
                if (cell)
                {
                    total++;
                }
            }

            return total;
        }
    }

    /// <summary>
    /// Reads or writes one cell. Reading outside the mask yields false — every neighbourhood loop in
    /// this project relies on that — but writing outside it is a loud failure.
    /// </summary>
    /// <remarks>
    /// The asymmetry is deliberate: there is no correspondingly true answer for a write, and the
    /// row-major index is not injective across the edges — <c>[-1, y]</c> lands on the last cell of
    /// row <c>y - 1</c>, so an unguarded write would silently corrupt a neighbouring row.
    /// </remarks>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    internal bool this[int x, int y]
    {
        get => Contains(x, y) && cells[(y * Width) + x];

        set
        {
            if (!Contains(x, y))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x), $"({x}, {y}) is outside a {Width}×{Height} mask.");
            }

            cells[(y * Width) + x] = value;
        }
    }

    /// <summary>Every set pixel, row-major — the one enumeration order this project relies on.</summary>
    internal IEnumerable<(int X, int Y)> Pixels()
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (cells[(y * Width) + x])
                {
                    yield return (x, y);
                }
            }
        }
    }

    /// <summary>True when the coordinates are inside the mask.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    internal bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>The mask of every pixel this one does not hold.</summary>
    internal PixelMask Complement()
    {
        var result = new bool[cells.Length];
        for (var index = 0; index < cells.Length; index++)
        {
            result[index] = !cells[index];
        }

        return new PixelMask(result, Width, Height);
    }

    /// <summary>This mask minus another.</summary>
    /// <param name="other">The mask to subtract. Must be the same size.</param>
    internal PixelMask Except(PixelMask other)
    {
        RequireSameSize(other);
        var result = new bool[cells.Length];
        for (var index = 0; index < cells.Length; index++)
        {
            result[index] = cells[index] && !other.cells[index];
        }

        return new PixelMask(result, Width, Height);
    }

    /// <summary>This mask without the listed pixels.</summary>
    /// <remarks>
    /// The list overload exists because <see cref="Except(PixelMask)"/> costs a whole frame per
    /// call, and callers often subtract one small connected group at a time from a much larger set.
    /// </remarks>
    /// <param name="pixels">The pixels to clear. Each must be inside the mask.</param>
    internal PixelMask Without(IReadOnlyList<(int X, int Y)> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var result = new PixelMask((bool[])cells.Clone(), Width, Height);
        foreach (var (x, y) in pixels)
        {
            result[x, y] = false;
        }

        return result;
    }

    /// <summary>
    /// The Euclidean distance from every pixel to the nearest <b>set</b> pixel, in pixel centres.
    /// </summary>
    /// <remarks>
    /// <see cref="double.PositiveInfinity"/> everywhere when the mask is empty, which every caller
    /// then compares against rather than dividing by.
    /// </remarks>
    internal double[] DistanceToSet()
    {
        // Larger than any squared distance inside the frame, so it stands in for "unreachable"
        // without ever putting an infinity into the envelope arithmetic (where it would subtract
        // to NaN). Mapped back to a real infinity on the way out.
        var unreachable = ((double)Width * Width) + ((double)Height * Height) + 1d;
        var squared = SquaredDistanceTransform(cells, Width, Height, unreachable);
        var result = new double[squared.Length];
        for (var index = 0; index < squared.Length; index++)
        {
            result[index] = squared[index] >= unreachable
                ? double.PositiveInfinity
                : Math.Sqrt(squared[index]);
        }

        return result;
    }

    /// <summary>Morphological dilation by a Euclidean disc: every pixel within the radius.</summary>
    /// <param name="radius">The disc's radius in pixels.</param>
    internal PixelMask Dilate(double radius)
    {
        var distance = DistanceToSet();
        var result = new bool[cells.Length];
        for (var index = 0; index < cells.Length; index++)
        {
            result[index] = distance[index] <= radius;
        }

        return new PixelMask(result, Width, Height);
    }

    /// <summary>Morphological erosion by a Euclidean disc: every pixel the disc fits inside.</summary>
    /// <param name="radius">The disc's radius in pixels.</param>
    internal PixelMask Erode(double radius)
    {
        var distance = Complement().DistanceToSet();
        var result = new bool[cells.Length];
        for (var index = 0; index < cells.Length; index++)
        {
            result[index] = distance[index] > radius;
        }

        return new PixelMask(result, Width, Height);
    }

    /// <summary>Morphological closing: dilate, then erode, by the same disc.</summary>
    /// <param name="radius">The disc's radius in pixels.</param>
    internal PixelMask Close(double radius) => Dilate(radius).Erode(radius);

    /// <summary>
    /// The 4-connected region reachable from the image border over pixels this mask holds.
    /// </summary>
    /// <remarks>
    /// A flood fill rather than a component labelling because every caller wants exactly this one
    /// component: what the outside of the image can reach.
    /// </remarks>
    internal PixelMask FloodFromBorder()
    {
        var reached = new bool[cells.Length];
        var queue = new Queue<(int X, int Y)>();

        void Seed(int x, int y)
        {
            var index = (y * Width) + x;
            if (!cells[index] || reached[index])
            {
                return;
            }

            reached[index] = true;
            queue.Enqueue((x, y));
        }

        for (var x = 0; x < Width; x++)
        {
            Seed(x, 0);
            Seed(x, Height - 1);
        }

        for (var y = 0; y < Height; y++)
        {
            Seed(0, y);
            Seed(Width - 1, y);
        }

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if (x > 0)
            {
                Seed(x - 1, y);
            }

            if (y > 0)
            {
                Seed(x, y - 1);
            }

            if (x < Width - 1)
            {
                Seed(x + 1, y);
            }

            if (y < Height - 1)
            {
                Seed(x, y + 1);
            }
        }

        return new PixelMask(reached, Width, Height);
    }

    /// <summary>
    /// The exact squared Euclidean distance transform (Felzenszwalb and Huttenlocher's lower
    /// envelope, one pass per axis).
    /// </summary>
    /// <param name="source">The set to measure distance to.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="unreachable">The finite stand-in for "no set pixel anywhere".</param>
    private static double[] SquaredDistanceTransform(
        bool[] source, int width, int height, double unreachable)
    {
        var distance = new double[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            distance[index] = source[index] ? 0d : unreachable;
        }

        var column = new double[height];
        var row = new double[width];

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                column[y] = distance[(y * width) + x];
            }

            var transformed = LowerEnvelope(column);
            for (var y = 0; y < height; y++)
            {
                distance[(y * width) + x] = transformed[y];
            }
        }

        for (var y = 0; y < height; y++)
        {
            Array.Copy(distance, y * width, row, 0, width);
            var transformed = LowerEnvelope(row);
            Array.Copy(transformed, 0, distance, y * width, width);
        }

        return distance;
    }

    /// <summary>
    /// The one-dimensional distance transform of a sampled function: the lower envelope of the
    /// parabolas <c>(q - p)² + f(p)</c>.
    /// </summary>
    /// <param name="values">The sampled function. Finite everywhere.</param>
    private static double[] LowerEnvelope(double[] values)
    {
        var length = values.Length;
        var result = new double[length];
        var vertices = new int[length];
        var boundaries = new double[length + 1];
        var rightmost = 0;

        vertices[0] = 0;
        boundaries[0] = double.NegativeInfinity;
        boundaries[1] = double.PositiveInfinity;

        for (var q = 1; q < length; q++)
        {
            var intersection = Intersection(values, q, vertices[rightmost]);
            while (intersection <= boundaries[rightmost])
            {
                rightmost--;
                intersection = Intersection(values, q, vertices[rightmost]);
            }

            rightmost++;
            vertices[rightmost] = q;
            boundaries[rightmost] = intersection;
            boundaries[rightmost + 1] = double.PositiveInfinity;
        }

        var current = 0;
        for (var q = 0; q < length; q++)
        {
            while (boundaries[current + 1] < q)
            {
                current++;
            }

            var p = vertices[current];
            result[q] = ((q - p) * (double)(q - p)) + values[p];
        }

        return result;
    }

    /// <summary>Where the parabolas rooted at two samples cross.</summary>
    /// <param name="values">The sampled function.</param>
    /// <param name="q">The later sample.</param>
    /// <param name="p">The earlier sample.</param>
    private static double Intersection(double[] values, int q, int p) =>
        ((values[q] + (q * (double)q)) - (values[p] + (p * (double)p))) / (2d * (q - p));

    private void RequireSameSize(PixelMask other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Width != Width || other.Height != Height)
        {
            throw new InvalidOperationException(
                $"A {Width}×{Height} mask cannot be combined with a {other.Width}×{other.Height} one.");
        }
    }
}
