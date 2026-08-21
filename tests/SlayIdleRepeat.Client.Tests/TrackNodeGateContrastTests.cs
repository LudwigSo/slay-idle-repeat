using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The gate mark's two non-colour channels, measured from the scene file rather than described.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This case exists because its assertion was once missing and the screen shipped wrong.</b>
/// The collar was authored in the ground's own colour, byte for byte, so the frame channel of a mark
/// whose entire purpose is to say what a colour may not drew at 1.0:1 and was not there at all — and
/// the board's tile colours have no test, while the tile-kind table beside them has its count and
/// order pinned. Nothing in this repo parses a scene, so nothing could fail: the defect was found by
/// reading colour literals by hand, twice, four phases apart.
/// </para>
/// <para>
/// 🔒 <b>The scene is read as the text file it is</b>, so this needs no engine and stays inside a
/// suite whose whole premise is that nothing here is a Node. Its scope is the mark this branch
/// added, not the board's palette: a contrast FLOOR, so retuning the mark stays allowed and retuning
/// it until one of its channels disappears does not.
/// </para>
/// <para>
/// ⚠️ <b>Each channel is measured against the surface it is actually drawn on</b>, which is the
/// distinction the original defect turned on. The collar draws in the gap outside its pip, so it is
/// read against the screen's ground — parsed from the board scene, not transcribed. The bars cross
/// the pip, so they are read against the fill; and against the token colour too, because the pip is
/// repainted when the run stands on it and a mark that vanished there would vanish at exactly the
/// moment the player is looking at it.
/// </para>
/// </remarks>
public sealed class TrackNodeGateContrastTests
{
    /// <summary>
    /// What every channel of the mark has to clear against its own backdrop.
    /// </summary>
    /// <remarks>
    /// The ordinary contrast floor for anything a player has to be able to make out. The mark clears
    /// it with room on all three counts; the historical defect scored 1.0.
    /// </remarks>
    private const double ContrastFloor = 4.5;

    private const string TrackNodeScene = "src/SlayIdleRepeat.Client/game/scenes/TrackNode.tscn";
    private const string BoardScene = "src/SlayIdleRepeat.Client/game/scenes/Board.tscn";
    private const string BoardScript = "src/SlayIdleRepeat.Client/game/scenes/Board.cs";

    private const string CollarStyleBox = "id=\"StyleBoxFlat_gate_collar\"";
    private const string GroundRect = "name=\"Ground\"";
    private const string BorderColourKey = "border_color";
    private const string ColourKey = "color";

    /// <summary>
    /// The two literals this file transcribes rather than reads, and the text they must still appear
    /// in for the transcription to mean anything.
    /// </summary>
    /// <remarks>
    /// 🔒 The fill a pip is drawn in is a private colour of a Godot <c>Control</c>, which this suite
    /// cannot name without loading the engine. So it is copied here — and a copy that agrees only
    /// with itself is worth nothing, which is why the case below asserts the copy is still present
    /// verbatim in the script it came from. Retuning either colour there fails that case and says to
    /// re-transcribe, rather than leaving these numbers quietly measuring a shade the screen has
    /// stopped drawing.
    /// </remarks>
    private const string MiniBossFillLiteral = "0.91f, 0.45f, 0.38f";

    /// <summary>And the colour a pip is repainted in while the run stands on it.</summary>
    private const string TokenLiteral = "0.93f, 0.93f, 0.96f";

    private static readonly Rgb MiniBossFill = new(0.91, 0.45, 0.38);

    private static readonly Rgb Token = new(0.93, 0.93, 0.96);

    private static readonly string[] BarRects = ["name=\"LeftBar\"", "name=\"RightBar\""];

    private static readonly Regex ColourCall = new(
        @"^Color\(\s*(?<r>[-+0-9.eE]+)\s*,\s*(?<g>[-+0-9.eE]+)\s*,\s*(?<b>[-+0-9.eE]+)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// 🔴 The floor under every case here: a text-parsing test that finds nothing must FAIL, never
    /// quietly measure a default. A renamed sub-resource, a renamed node, a reformatted scene or a
    /// moved file all land here first, and say which lookup came back empty.
    /// </summary>
    [Fact]
    public void Every_colour_these_cases_measure_was_really_extracted()
    {
        Should.NotThrow(() => Collar(), "the collar's own colour could not be read from the scene");
        Should.NotThrow(() => Ground(), "the screen's ground colour could not be read from the board scene");

        foreach (var bar in BarRects)
        {
            Should.NotThrow(
                () => Bar(bar),
                $"the bar '{bar}' could not be read from the scene, so nothing measured it");
        }
    }

    /// <summary>
    /// 🔴 <b>The exact defect this case was written for.</b> The collar draws entirely in the band
    /// outside its pip, so the ground is the only surface it is ever seen against.
    /// </summary>
    [Fact]
    public void The_collar_clears_the_floor_against_the_ground_it_is_drawn_on()
    {
        var collar = Collar();
        var ground = Ground();
        var ratio = Contrast(collar, ground);

        ratio.ShouldBeGreaterThanOrEqualTo(
            ContrastFloor,
            $"the gate collar is drawn at {Ratio(ratio)} against the ground it sits on " +
            $"(collar {Describe(collar)}, ground {Describe(ground)}). The collar is the FRAME " +
            "channel of a mark that exists because a rule the player cannot see coming may not be " +
            "signalled by a colour — and it draws entirely in the gap outside its pip, so the " +
            "ground is the only thing it is ever seen against. Below the floor it stops being a " +
            "channel and the mark falls back to hue alone, which is the failure this case was " +
            "written for: it has happened once, at 1.0, with the collar in the ground's own value.");
    }

    /// <summary>
    /// The bars cross the pip, so the fill is their backdrop — and both of them, because a mark that
    /// reads on one side of its own centre and not the other is not symmetric.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Each_bar_clears_the_floor_against_the_fill_a_marked_pip_is_drawn_in(int bar)
    {
        var colour = Bar(BarRects[bar]);
        var ratio = Contrast(colour, MiniBossFill);

        ratio.ShouldBeGreaterThanOrEqualTo(
            ContrastFloor,
            $"the gate bar '{BarRects[bar]}' is drawn at {Ratio(ratio)} against the fill of the pip " +
            $"it crosses (bar {Describe(colour)}, fill {Describe(MiniBossFill)}). The bars are the " +
            "mark's second channel and the only one drawn over the fill, so below the floor the " +
            "mark is a frame alone and a mini-boss differs from the boss by hue plus an outline.");
    }

    /// <summary>
    /// 🔒 And against the token, because the pip the run is standing on is repainted and the mark has
    /// to survive it. This is the half of the rule the original fix was careful about.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Each_bar_clears_the_floor_against_the_token_the_pip_is_repainted_in(int bar)
    {
        var colour = Bar(BarRects[bar]);
        var ratio = Contrast(colour, Token);

        ratio.ShouldBeGreaterThanOrEqualTo(
            ContrastFloor,
            $"the gate bar '{BarRects[bar]}' is drawn at {Ratio(ratio)} against the token colour " +
            $"(bar {Describe(colour)}, token {Describe(Token)}). A pip is repainted in the token " +
            "while the run stands on it, so this is the fill the bars are seen against at the one " +
            "moment the player is certainly looking at that node.");
    }

    /// <summary>
    /// 🔒 The guard on the two colours this file copies instead of reading. A retune in the script
    /// fails here and says to re-transcribe, rather than leaving the cases above measuring a shade
    /// the screen no longer draws.
    /// </summary>
    [Theory]
    [InlineData(MiniBossFillLiteral)]
    [InlineData(TokenLiteral)]
    public void The_fills_these_cases_transcribe_are_still_the_ones_the_screen_draws(string literal)
    {
        Read(BoardScript).ShouldContain(
            literal,
            Case.Sensitive,
            $"'{literal}' no longer appears in {BoardScript}, so the colour this suite measures the " +
            "gate bars against is one the screen has stopped drawing. These two fills are copied " +
            "rather than read because they are private colours of a Godot Control that this suite " +
            "cannot load. Re-transcribe them here, and check the bars still clear the floor against " +
            "whatever replaced them — do not delete this case, which is the only thing keeping the " +
            "copy honest.");
    }

    private static Rgb Collar() => Colour(TrackNodeScene, CollarStyleBox, BorderColourKey);

    private static Rgb Bar(string node) => Colour(TrackNodeScene, node, ColourKey);

    private static Rgb Ground() => Colour(BoardScene, GroundRect, ColourKey);

    /// <summary>
    /// One colour property of one block of a scene file.
    /// </summary>
    /// <remarks>
    /// Blocks rather than a search of the whole file, because a scene holds several colours under
    /// keys that are substrings of each other — <c>color</c>, <c>bg_color</c>, <c>border_color</c>,
    /// <c>font_color</c> — on nodes that are reordered freely. The header locates the block and the
    /// key is matched whole, so neither a reorder nor a neighbouring property can answer for the one
    /// being asked about.
    /// </remarks>
    private static Rgb Colour(string relativePath, string header, string key)
    {
        var block = Blocks(relativePath)
            .SingleOrDefault(candidate => candidate.Header.Contains(header, StringComparison.Ordinal));

        if (block is null)
        {
            throw new InvalidOperationException(
                $"No single block carrying '{header}' in '{relativePath}'. Either it was renamed, or " +
                "the scene now holds more than one — and nothing measured its colour either way.");
        }

        var value = block.Body
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0].Trim(), key, StringComparison.Ordinal))
            .Select(parts => parts[1].Trim())
            .SingleOrDefault();

        if (value is null)
        {
            throw new InvalidOperationException(
                $"The block carrying '{header}' in '{relativePath}' has no single '{key}' property, " +
                "so there was no colour to measure.");
        }

        var match = ColourCall.Match(value);

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"'{key} = {value}' in the block carrying '{header}' of '{relativePath}' is not a " +
                "Color(r, g, b, ...) this case can read.");
        }

        return new Rgb(
            Channel(match, "r", relativePath, key),
            Channel(match, "g", relativePath, key),
            Channel(match, "b", relativePath, key));
    }

    /// <summary>
    /// 🔒 Invariant culture, spelled out. A scene file writes <c>0.66</c> and a comma-decimal culture
    /// would read that as sixty-six — a number that parses, passes every floor, and measures nothing.
    /// </summary>
    private static double Channel(Match match, string group, string relativePath, string key)
    {
        var text = match.Groups[group].Value;

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Channel '{group}' of '{key}' in '{relativePath}' reads '{text}', which is not a number.");
    }

    private static IReadOnlyList<Block> Blocks(string relativePath)
    {
        var blocks = new List<Block>();
        Block? current = null;

        foreach (var line in Read(relativePath).Split('\n').Select(line => line.Trim()))
        {
            if (line.StartsWith('['))
            {
                current = new Block(line);
                blocks.Add(current);

                continue;
            }

            current?.Body.Add(line);
        }

        return blocks;
    }

    private static string Read(string relativePath)
    {
        var path = Path.Combine(RepoPaths.RepositoryRoot, relativePath);

        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException(
                $"No '{relativePath}' under '{RepoPaths.RepositoryRoot}'. These cases read the mark " +
                "out of the checkout's own scene files, so a missing one is not a passing case.",
                path);
    }

    private static double Contrast(Rgb first, Rgb second)
    {
        var high = Math.Max(Luminance(first), Luminance(second));
        var low = Math.Min(Luminance(first), Luminance(second));

        return (high + 0.05) / (low + 0.05);
    }

    private static double Luminance(Rgb colour) =>
        (0.2126 * Linear(colour.R)) + (0.7152 * Linear(colour.G)) + (0.0722 * Linear(colour.B));

    private static double Linear(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static string Ratio(double ratio) =>
        string.Create(CultureInfo.InvariantCulture, $"{ratio:0.00}:1");

    private static string Describe(Rgb colour) =>
        string.Create(CultureInfo.InvariantCulture, $"({colour.R}, {colour.G}, {colour.B})");

    private sealed record Block(string Header)
    {
        internal List<string> Body { get; } = [];
    }

    private readonly record struct Rgb(double R, double G, double B);
}
