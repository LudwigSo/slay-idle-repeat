using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Hero;

/// <summary>
/// <see cref="HeroNames"/> — the public door onto the name filter, and the one thing it refuses to
/// carry back out.
/// </summary>
/// <remarks>
/// <see cref="HeroNameRuleTests"/> proves the filter is right about a word list. This file proves the
/// public seam reaches it — every refusal arrives intact rather than collapsed into "refused" — and
/// that the matched term stays behind.
/// </remarks>
public sealed class HeroNamesTests
{
    [Fact]
    public void Decide_returns_an_accepted_name_exactly_as_it_was_typed()
    {
        var decision = HeroNames.Decide("Ludwig", ProfanityDocuments.Shipped);

        decision.Name!.Value.ShouldBe(
            "Ludwig",
            "normalisation decides whether a name is allowed, never what it is, and the public seam " +
            "is where a rewritten name would first become a player's problem.");
        decision.Refusal.ShouldBeNull();
    }

    /// <summary>Each of the five refusals is reachable through the public seam and arrives as itself.</summary>
    /// <remarks>
    /// A seam that answered a single "refused" would satisfy every case here but one, and four of the
    /// five checks could then be broken with nothing red — which is the failure the refusal vocabulary
    /// exists to prevent and is worth re-proving at the boundary that crosses out of Core.
    /// </remarks>
    [Theory]
    [InlineData(null, HeroNameRefusal.BLANK)]
    [InlineData("   ", HeroNameRefusal.BLANK)]
    [InlineData("aaaaaaaaaaaaa", HeroNameRefusal.TOO_LONG)]
    [InlineData("Lud\nwig", HeroNameRefusal.DISALLOWED_CHARACTER)]
    [InlineData(ProfanityDocuments.EnglishTerm, HeroNameRefusal.PROFANE_EN)]
    [InlineData(ProfanityDocuments.GermanTerm, HeroNameRefusal.PROFANE_DE)]
    public void Decide_reports_which_check_refused_the_name(string? candidate, HeroNameRefusal refusal)
    {
        var decision = HeroNames.Decide(candidate, ProfanityDocuments.Shipped);

        decision.Refusal.ShouldBe(refusal);
        decision.Name.ShouldBeNull();
    }

    /// <summary>🔒 A refusal never carries the term back out, though the filter underneath it knows it.</summary>
    /// <remarks>
    /// The first assertion is the control: the term really is available one layer down, so the second
    /// is a deliberate omission rather than a filter that happened to match nothing. Asserted against
    /// the rendered decision because that is the shape a slur would actually escape in — a response
    /// body, a log line, a crash report.
    /// </remarks>
    [Fact]
    public void Decide_never_carries_the_matched_term_out_of_Core()
    {
        var lexicon = ProfanityLexicon.Read(ProfanityDocuments.Shipped);

        HeroNameRule.Validate(ProfanityDocuments.EnglishTerm, lexicon).Match.ShouldBe(
            ProfanityDocuments.EnglishTerm,
            "the rule stopped naming the term it matched, so the omission below is nothing being " +
            "dropped and this case has no teeth.");

        HeroNames.Decide(ProfanityDocuments.EnglishTerm, ProfanityDocuments.Shipped)
            .ToString()
            .ShouldNotContain(
                ProfanityDocuments.EnglishTerm,
                Case.Insensitive,
                "the public decision renders the matched term. Everything that crosses this seam ends " +
                "up in a response body or a log line, and echoing the word back is the one outcome " +
                "the refusal vocabulary exists to make unnecessary.");
    }

    /// <summary>The word lists come from the snapshot the caller passed, not from one held anywhere else.</summary>
    /// <remarks>
    /// One name, two content sets, two answers. A seam that loaded its own lists — or cached the first
    /// snapshot it ever saw — answers the same way twice, and a term the curator adds tomorrow would
    /// never reach a player's name.
    /// </remarks>
    [Fact]
    public void Decide_reads_the_word_lists_off_the_snapshot_it_was_handed()
    {
        var hostile = ProfanityDocuments.With(english: ProfanityDocuments.Terms("ludwig"));

        HeroNames.Decide("Ludwig", ProfanityDocuments.Shipped).Refusal.ShouldBeNull(
            "the shipped fixture already refuses this name, so the refusal below says nothing about " +
            "which snapshot was read.");

        HeroNames.Decide("Ludwig", hostile).Refusal.ShouldBe(HeroNameRefusal.PROFANE_EN);
    }

    [Fact]
    public void Default_is_the_authored_name_a_player_who_has_not_chosen_one_carries()
    {
        HeroNames.Default(ProfanityDocuments.Shipped).Value.ShouldBe("Wanderer");
    }
}
