namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>
/// A 5×7 bitmap font, declared in code, for stamping an asset id onto a placeholder.
/// </summary>
/// <remarks>
/// <para>
/// Declared here instead of using <c>SKTypeface</c> because Skia's default typeface depends on
/// whatever font the host OS provides — on a bare CI container that can be none at all, in which
/// case Skia silently draws nothing and the stamp vanishes while the batch still reports success.
/// A font declared in code needs no native binary and renders identically everywhere.
/// </para>
/// <para>
/// Glyphs are uppercase for a lowercase (snake_case) id: a 5×7 cell can't carry a legible
/// descender, and the stamp has to survive downscaling to as little as 96×96. The letters still
/// read as the id; the case is a rendering choice, not a different string.
/// </para>
/// <para>
/// This stamp is a knowing departure from the "never render text inside a generated image" rule,
/// authorised for placeholders specifically by the M8 kickoff so a missing asset is identifiable on
/// screen. <see cref="PlaceholderBatchReport.Departures"/> carries it as
/// <see cref="PlaceholderBatchReport.IdStampDeparture"/> in every batch that drew anything.
/// </para>
/// </remarks>
public static class StampFont
{
    /// <summary>The width of one glyph cell, in font pixels.</summary>
    public const int GlyphWidth = 5;

    /// <summary>The height of one glyph cell, in font pixels.</summary>
    public const int GlyphHeight = 7;

    /// <summary>The gap between two adjacent glyphs, in font pixels.</summary>
    public const int GlyphAdvance = GlyphWidth + 1;

    /// <summary>The gap between two baselines, in font pixels.</summary>
    public const int LineAdvance = GlyphHeight + 2;

    /// <summary>The glyph a character outside the font is drawn as.</summary>
    private const char Fallback = '?';

    // Declared as the concrete Dictionary rather than IReadOnlyDictionary so `Characters` can
    // return `.Keys` directly. Never mutated after initialisation.
    private static readonly Dictionary<char, string[]> Glyphs =
        new Dictionary<char, string[]>
        {
            ['a'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
            ['b'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
            ['c'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
            ['d'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
            ['e'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
            ['f'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
            ['g'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
            ['h'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
            ['i'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
            ['j'] = ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
            ['k'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
            ['l'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
            ['m'] = ["#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#"],
            ['n'] = ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
            ['o'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
            ['p'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
            ['q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
            ['r'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
            ['s'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
            ['t'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
            ['u'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
            ['v'] = ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
            ['w'] = ["#...#", "#...#", "#...#", "#...#", "#.#.#", "##.##", "#...#"],
            ['x'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
            ['y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
            ['z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
            ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
            ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
            ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
            ['3'] = ["#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###."],
            ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
            ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
            ['6'] = ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
            ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
            ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
            ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
            ['_'] = [".....", ".....", ".....", ".....", ".....", ".....", "#####"],
            ['-'] = [".....", ".....", ".....", "#####", ".....", ".....", "....."],
            ['.'] = [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
            [' '] = [".....", ".....", ".....", ".....", ".....", ".....", "....."],
            [Fallback] = [".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.."],
        };

    /// <summary>Every character this font draws as itself.</summary>
    public static IReadOnlyCollection<char> Characters => Glyphs.Keys;

    /// <summary>True when this font draws the character as itself rather than as a fallback.</summary>
    /// <param name="character">The character to look up.</param>
    public static bool Covers(char character) => Glyphs.ContainsKey(character);

    /// <summary>
    /// True when the glyph for a character has ink at a cell.
    /// </summary>
    /// <param name="character">The character. One outside the font draws as <c>?</c>.</param>
    /// <param name="x">The column, 0 to <see cref="GlyphWidth"/> − 1.</param>
    /// <param name="y">The row, 0 to <see cref="GlyphHeight"/> − 1.</param>
    public static bool Ink(char character, int x, int y)
    {
        if (x < 0 || y < 0 || x >= GlyphWidth || y >= GlyphHeight)
        {
            throw new ArgumentOutOfRangeException(
                x is >= 0 and < GlyphWidth ? nameof(y) : nameof(x),
                $"({x}, {y}) is outside a {GlyphWidth}×{GlyphHeight} glyph cell.");
        }

        var glyph = Glyphs.TryGetValue(character, out var found) ? found : Glyphs[Fallback];
        return glyph[y][x] == '#';
    }

    /// <summary>The width of a string in font pixels, with no trailing advance.</summary>
    /// <param name="text">The text to measure.</param>
    public static int MeasureWidth(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length == 0 ? 0 : (text.Length * GlyphAdvance) - (GlyphAdvance - GlyphWidth);
    }
}
