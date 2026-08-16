using Shouldly;
using SlayIdleRepeat.Core.Rules.Hero;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Hero;

/// <summary>
/// <see cref="NameNormalisation"/> — the five steps a candidate name goes through before it is
/// matched, each asserted on its own.
/// </summary>
/// <remarks>
/// Stated step by step rather than only through <c>HeroNameRule</c>, because a normaliser that
/// dropped one step still refuses the obvious spellings: every case here is an evasion that costs a
/// player nothing to try, and the one the step closes.
/// </remarks>
public sealed class NameNormalisationTests
{
    /// <summary>Case folding is invariant, so the filter behaves the same on a Turkish device.</summary>
    /// <remarks>
    /// The dotted capital İ is the discriminating input: a culture-sensitive fold on a Turkish
    /// locale lowercases <c>I</c> to a dotless <c>ı</c>, which the <c>a</c>–<c>z</c> filter then
    /// drops — so a name containing it would normalise differently depending on the device.
    /// </remarks>
    [Theory]
    [InlineData("LUDWIG", "ludwig")]
    [InlineData("LuDwIg", "ludwig")]
    [InlineData("İstanbul", "istanbul")]
    public void Case_is_folded_invariantly(string candidate, string expected)
    {
        NameNormalisation.Normalise(candidate).ShouldBe(expected);
    }

    /// <summary>
    /// The ligatures a Unicode decomposition cannot reach are expanded by hand — <c>ß</c> above all.
    /// </summary>
    /// <remarks>
    /// <c>ß</c> is the one `27` §1's German list depends on: it is a letter rather than a decorated
    /// <c>s</c>, so <c>FormD</c> leaves it alone and the <c>a</c>–<c>z</c> filter would simply
    /// delete it — turning <c>Scheiße</c> into <c>scheie</c>, which no authored spelling matches.
    /// </remarks>
    [Theory]
    [InlineData("Straße", "strasse")]
    [InlineData("Æther", "aether")]
    [InlineData("Sølv", "solv")]
    public void The_ligatures_a_decomposition_cannot_reach_are_expanded(string candidate, string expected)
    {
        NameNormalisation.Normalise(candidate).ShouldBe(expected);
    }

    /// <summary>Diacritics are stripped by decomposition, so no table of pairs has to be maintained.</summary>
    [Theory]
    [InlineData("Bädwörd", "badword")]
    [InlineData("Ǹâïvë", "naive")]
    [InlineData("Łódź", "lodz")]
    public void Diacritics_are_stripped(string candidate, string expected)
    {
        NameNormalisation.Normalise(candidate).ShouldBe(expected);
    }

    /// <summary>Digits and symbols that read as letters are folded to those letters.</summary>
    /// <remarks>
    /// The negative control is the unmapped digit: <c>2</c> and <c>6</c> read as no letter, so they
    /// are dropped rather than folded — which is what proves the substitution table is a table
    /// rather than "every digit becomes something".
    /// </remarks>
    [Theory]
    [InlineData("b4dw0rd", "badword")]
    [InlineData("$h1t", "shit")]
    [InlineData("l33t", "leet")]
    [InlineData("a2b6c", "abc")]
    public void The_common_substitutions_are_folded(string candidate, string expected)
    {
        NameNormalisation.Normalise(candidate).ShouldBe(expected);
    }

    /// <summary>Everything that is not a letter is dropped, so inserted separators stop evading.</summary>
    [Theory]
    [InlineData("b a d w o r d", "badword")]
    [InlineData("b.a-d_w o r d", "badword")]
    [InlineData("», «", "")]
    public void Everything_that_is_not_a_letter_is_dropped(string candidate, string expected)
    {
        NameNormalisation.Normalise(candidate).ShouldBe(expected);
    }

    /// <summary>Collapsing squeezes a run of one letter to a single letter, and touches nothing else.</summary>
    /// <remarks>
    /// The last two cases are the negative controls: a string with no runs is returned unchanged,
    /// and two runs of DIFFERENT letters stay two letters. A collapse that deduplicated the whole
    /// string rather than adjacent runs would pass the first three cases and fail these.
    /// </remarks>
    [Theory]
    [InlineData("aaa", "a")]
    [InlineData("baaadwooord", "badword")]
    [InlineData("aabbaa", "aba")]
    [InlineData("badword", "badword")]
    [InlineData("abab", "abab")]
    [InlineData("", "")]
    public void Collapsing_squeezes_a_run_of_one_letter(string normalised, string expected)
    {
        NameNormalisation.Collapse(normalised).ShouldBe(expected);
    }

    /// <summary>Null is a caller defect rather than an empty name.</summary>
    [Fact]
    public void Null_is_refused_rather_than_normalised_to_nothing()
    {
        Should.Throw<ArgumentNullException>(() => NameNormalisation.Normalise(null!));
        Should.Throw<ArgumentNullException>(() => NameNormalisation.Collapse(null!));
    }
}
