namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>
/// A 5×7 bitmap font, declared in code, for stamping an asset id onto a placeholder.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why not <c>SKTypeface</c>.</b> Skia's default typeface is whatever the host operating
/// system provides. On CI's <c>ubuntu-24.04</c> runner a container can carry no font at all, in
/// which case Skia silently draws nothing — the stamp that makes a missing asset self-identifying
/// would vanish and the batch would still report success. Even where a font exists, Windows and
/// Linux resolve different ones, so two runs of the same generator would produce different pixels.
/// A font declared here has neither problem and needs no native binary.
/// </para>
/// <para>
/// 🔒 <b>Uppercase glyph shapes for a lowercase id.</b> `15` §D1 ids are snake_case; the glyphs
/// below are drawn in capitals because a 5×7 cell cannot carry a legible descender, and the stamp
/// has to survive `15` §B4 step 5's downscale to as little as 96×96. The letters read as the id;
/// the case is a rendering choice, not a different string.
/// </para>
/// <para>
/// ⚠️ <b>This stamp is a knowing departure from `15` §A3</b> — <em>"Never render text inside a
/// generated image"</em> — and from Part F item 8. It is authorised for placeholders specifically,
/// by the M8 kickoff: a placeholder exists so that a missing asset is identifiable on screen, and an
/// unlabelled grey box on a battle screen tells nobody which of 641 slots is empty.
/// <see cref="PlaceholderBatchReport.Departures"/> carries it as
/// <see cref="PlaceholderBatchReport.IdStampDeparture"/> in every batch that drew anything, so it
/// can never be mistaken for something the doc permits in delivered art — Part F item 8 will not
/// report it, because it is a <see cref="AssetPipeline.Qa.QaClassification.Human"/> item and returns
/// <see cref="AssetPipeline.Qa.QaVerdict.HumanGapOnly"/> on every asset.
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

    // 🔒 Declared as the concrete Dictionary, not IReadOnlyDictionary: only Dictionary<,>.Keys is a
    // collection rather than a bare IEnumerable, and `Characters` returning it should not depend on
    // an unchecked cast that happens to succeed. The field is private and never mutated.
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

    /// <summary>
    /// Every character this font draws as itself. 🔒 Read by the suite as an S3 floor: a font that
    /// silently lost its glyphs would stamp a page of question marks and still report success.
    /// </summary>
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
