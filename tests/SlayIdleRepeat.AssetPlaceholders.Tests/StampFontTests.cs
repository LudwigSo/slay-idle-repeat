using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPlaceholders.Tests;

/// <summary>
/// The code-drawn 5×7 font: it covers every character a `15` §D1 id can carry, and every glyph in
/// it actually has ink.
/// </summary>
/// <remarks>
/// 🔒 A font that quietly lost half its glyphs would stamp a page of question marks and the batch
/// would still report success — the stamp is the only thing that makes a missing asset
/// self-identifying, and nothing downstream reads it. So the coverage is asserted against the real
/// register's ids rather than against a list typed here.
/// </remarks>
public sealed class StampFontTests
{
    /// <summary>
    /// How many characters the font must draw as themselves. 🔒 26 letters + 10 digits + underscore
    /// + hyphen + full stop + space, and the fallback glyph. An S3 floor: a font that shrank would
    /// otherwise satisfy "every glyph has ink" vacuously.
    /// </summary>
    private const int CharacterFloor = 40;

    [Fact]
    public void Every_character_in_every_register_id_is_covered_by_the_font()
    {
        var ids = PlaceholderFiles.Shipped.Art.Assets.Select(asset => asset.Id).ToArray();
        ids.Length.ShouldBeGreaterThan(900);

        var uncovered = ids
            .SelectMany(id => id.ToCharArray())
            .Distinct()
            .Where(character => !StampFont.Covers(character))
            .OrderBy(character => character)
            .ToArray();

        uncovered.ShouldBeEmpty(
            $"these characters occur in `15` §D1 ids and the font draws them as '?': " +
            $"[{string.Join(", ", uncovered)}]. A stamp nobody can read identifies nothing.");
    }

    [Fact]
    public void The_font_covers_the_banner_and_the_delivery_size_line_too()
    {
        // The stamp is not only the id: it carries the word "placeholder" and a "WxH" size line.
        var required = PlaceholderRenderer.Banner + "0123456789x";

        foreach (var character in required)
        {
            StampFont.Covers(character).ShouldBeTrue(
                $"the stamp writes '{character}' and the font does not carry it.");
        }
    }

    [Fact]
    public void Every_glyph_has_ink_except_the_space()
    {
        StampFont.Characters.Count.ShouldBeGreaterThanOrEqualTo(CharacterFloor);

        foreach (var character in StampFont.Characters)
        {
            var ink = Cells().Count(cell => StampFont.Ink(character, cell.X, cell.Y));

            if (character == ' ')
            {
                ink.ShouldBe(0, "a space is the one glyph that is meant to be blank.");
                continue;
            }

            ink.ShouldBeGreaterThan(
                0,
                $"the glyph for '{character}' is entirely blank, so the stamp silently drops it.");
        }
    }

    [Fact]
    public void A_character_outside_the_font_draws_as_the_fallback_rather_than_as_nothing()
    {
        StampFont.Covers('§').ShouldBeFalse();

        Cells().Count(cell => StampFont.Ink('§', cell.X, cell.Y)).ShouldBeGreaterThan(
            0,
            "an uncovered character must draw as a visible fallback. Drawing it as blank would " +
            "make a stamp silently lose a character rather than show that it had.");
    }

    [Fact]
    public void Measuring_counts_the_gaps_between_glyphs_and_not_after_the_last_one()
    {
        StampFont.MeasureWidth(string.Empty).ShouldBe(0);
        StampFont.MeasureWidth("a").ShouldBe(StampFont.GlyphWidth);
        StampFont.MeasureWidth("ab").ShouldBe(StampFont.GlyphAdvance + StampFont.GlyphWidth);
    }

    [Fact]
    public void Reading_a_cell_outside_a_glyph_is_refused_rather_than_answered()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StampFont.Ink('a', StampFont.GlyphWidth, 0))
            .Message.ShouldContain("outside a", Case.Sensitive);

        Should.Throw<ArgumentOutOfRangeException>(() => StampFont.Ink('a', 0, -1))
            .Message.ShouldContain("outside a", Case.Sensitive);
    }

    private static IEnumerable<(int X, int Y)> Cells() =>
        from y in Enumerable.Range(0, StampFont.GlyphHeight)
        from x in Enumerable.Range(0, StampFont.GlyphWidth)
        select (x, y);
}
