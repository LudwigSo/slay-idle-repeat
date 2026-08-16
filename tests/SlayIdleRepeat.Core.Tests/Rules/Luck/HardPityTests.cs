using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// M1, the mandatory guarantee: after <c>N − 1</c> misses the <b>N-th</b> draw is forced, the draw
/// before it is not, and a fired counter goes back to zero.
/// </summary>
/// <remarks>
/// <c>24</c> §11: <em>"every rule in §4 gets an explicit unit test asserting the guarantee fires at
/// exactly N"</em>. The theories below run over every <c>N</c> authored anywhere in
/// <c>luck.json</c> — all seventeen of them, the five classes that state their rule in another shape
/// included — rather than over a hand-picked few, so the coverage claim is visible rather than
/// asserted.
/// </remarks>
public sealed class HardPityTests
{
    /// <summary>Every authored <c>N</c>, with the class it protects and the pointer it lives at.</summary>
    public static TheoryData<string, string, int> EveryAuthoredN()
    {
        var data = new TheoryData<string, string, int>();

        foreach (var guarantee in LuckDocuments.EveryAuthoredHardPityN)
        {
            data.Add(guarantee.Source, guarantee.Reference, guarantee.EveryNth);
        }

        return data;
    }

    // ---------------------------------------------------------------- the guarantee fires at exactly N

    /// <summary>The N-th draw since the counter last reset is the forced one.</summary>
    /// <remarks>
    /// The counter holds misses <em>before</em> the draw, so the N-th draw is the one taken with
    /// <c>N − 1</c> misses on the clock. Getting that off by one shifts every guarantee in the game
    /// by a draw and turns <c>24</c> §4.1's ten-chest promise into an eleven-chest one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryAuthoredN))]
    public void The_Nth_draw_since_the_last_reset_is_forced(string source, string reference, int everyNth)
    {
        HardPity.Fires(everyNth - 1, everyNth).ShouldBeTrue(
            $"{source} authors N = {N(everyNth)} at {LuckDocuments.DocumentPath}{reference}, so the " +
            $"draw taken with {N(everyNth - 1)} misses on the clock is the {N(everyNth)}-th and must " +
            "be forced (24 §1 M1).");
    }

    /// <summary>The draw before the N-th is not forced.</summary>
    /// <remarks>
    /// The half that makes "at exactly N" a claim rather than "at N or earlier": a predicate that
    /// always answered true would pass the case above for every rule in the game.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryAuthoredN))]
    public void The_draw_before_the_Nth_is_not_forced(string source, string reference, int everyNth)
    {
        HardPity.Fires(everyNth - 2, everyNth).ShouldBeFalse(
            $"{source} authors N = {N(everyNth)} at {LuckDocuments.DocumentPath}{reference}. Firing " +
            $"on the {N(everyNth - 1)}-th draw would be a guarantee the disclosure page (24 §1.1) " +
            "does not state — pity that fires early is still pity that is mis-stated.");
    }

    /// <summary>A fresh counter is not forced, however tight the rule.</summary>
    /// <remarks>
    /// The densest authored rule is <c>CHEST_APEX</c>'s every-3rd, so a counter at zero is never the
    /// forced draw — which is what makes <see cref="HardPity.Reset"/> meaningful rather than a value
    /// that immediately fires again.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryAuthoredN))]
    public void A_counter_that_has_just_reset_does_not_immediately_fire_again(
        string source, string reference, int everyNth)
    {
        HardPity.Fires(HardPity.Reset(), everyNth).ShouldBeFalse(
            $"{source} authors N = {N(everyNth)} at {LuckDocuments.DocumentPath}{reference}. A reset " +
            "counter that fired on the very next draw would make the guarantee the drop rate — 24 " +
            "§10 E3 caps a guarantee at 30% of grants in its class.");
    }

    /// <summary>A fired counter goes back to zero, not to one and not to <c>N</c>.</summary>
    [Fact]
    public void A_fired_counter_goes_back_to_zero()
    {
        HardPity.Reset().ShouldBe(
            0,
            "24 §1 M1: 'counter resets to 0'. Resetting to 1 would shorten every subsequent cycle by " +
            "a draw and resetting to N would fire the guarantee forever.");
    }

    /// <summary>
    /// The floor under the theories above: a source that shrank would run fewer cases and still
    /// report success.
    /// </summary>
    /// <remarks>
    /// Both halves matter — the count, so the list cannot quietly empty, and named members, so it
    /// cannot be padded with seventeen copies of the cheapest rule. Pinned against the shipped file
    /// by <c>LuckTuningMatchesTuningDataTests</c> in <c>Application.Tests</c>.
    /// </remarks>
    [Fact]
    public void The_exact_N_theories_cover_every_rule_24_section_4_states()
    {
        LuckDocuments.EveryAuthoredHardPityN.Count.ShouldBeGreaterThanOrEqualTo(
            17,
            "24 §4 states seventeen Ns across ten classes: chestStandard 10/40/160, chestPremium " +
            "5/25, chestApex 3, dropRun 6 and 4, eggPet 30/150, crateMount 8/30, wheel 60, minigame " +
            "4 and draft 15/3/5.");

        LuckDocuments.EveryAuthoredHardPityN.Select(rule => rule.Source).Distinct().Count().ShouldBe(
            9,
            "every class but ENHANCE states an N — 24 §4.6's mercy is a rate ramp, not a counted " +
            "guarantee, and is covered by SoftPityTests instead.");

        LuckDocuments.EveryAuthoredHardPityN.ShouldContain(
            new AuthoredGuarantee("CHEST_STANDARD", "#/chestStandard/hardPity/2/everyNth", 160),
            "the 160-chest SS rung is the longest guarantee in the game and the one a shortened list " +
            "would drop first");
        LuckDocuments.EveryAuthoredHardPityN.ShouldContain(
            new AuthoredGuarantee("MINIGAME", "#/minigame/chestPick/guaranteeAfterConsecutiveMisses", 4),
            "the classes whose rule is not a rarity ladder are the ones a ladder-shaped list forgets");

        LuckDocuments.EveryAuthoredHardPityN.Select(rule => rule.Reference).ShouldBeUnique(
            "two rules sharing a pointer means one of them is not being covered at all");
    }

    // ---------------------------------------------------------------- isolation between rungs

    /// <summary>
    /// One rung's <c>N</c> never fires another rung's counter. <c>CHEST_STANDARD</c> runs three
    /// ladders simultaneously (<c>24</c> §4.1), so this is the negative control that keeps the
    /// predicate from being read as "the counter is high enough for something".
    /// </summary>
    [Theory]
    [InlineData(9, 40)]
    [InlineData(9, 160)]
    [InlineData(39, 160)]
    public void A_counter_at_another_rungs_N_does_not_fire_this_rung(int missesBeforeDraw, int everyNth)
    {
        HardPity.Fires(missesBeforeDraw, everyNth).ShouldBeFalse(
            "chest #10 satisfies the A-rung and neither of the other two. The three counters run " +
            "independently and simultaneously (24 §4.1).");
    }

    /// <summary>
    /// A counter that somehow stands past its own <c>N</c> still fires. Reachable rather than
    /// theoretical: a retune that lowers an <c>N</c> leaves live players standing above the new rung.
    /// </summary>
    [Theory]
    [InlineData(10, 10)]
    [InlineData(400, 160)]
    [InlineData(int.MaxValue - 1, 3)]
    public void A_counter_standing_past_its_own_N_still_fires(int missesBeforeDraw, int everyNth)
    {
        HardPity.Fires(missesBeforeDraw, everyNth).ShouldBeTrue(
            "a shortened ladder must not strand the players it left above the new rung — the same " +
            "ruling the login calendar applies to a shortened cycle.");
    }

    // ---------------------------------------------------------------- advancing

    /// <summary>A missed draw moves the counter on by exactly one.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 10)]
    [InlineData(159, 160)]
    public void A_missed_draw_advances_the_counter_by_one(int misses, int expected)
    {
        HardPity.Advance(misses).ShouldBe(expected);
    }

    /// <summary>Counters never tick down (<c>24</c> §1.1: no decay).</summary>
    /// <remarks>
    /// Stated as a comparison against the input rather than as a literal, so an implementation that
    /// answered a constant could not satisfy it.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(1_000_000)]
    public void Advancing_never_lowers_a_counter(int misses)
    {
        HardPity.Advance(misses).ShouldBeGreaterThan(
            misses,
            "24 §1.1: 'counters never tick down over time. Bad luck is not a debt that expires.'");
    }

    // ---------------------------------------------------------------- refusals

    /// <summary>A negative counter is not a counter.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_negative_miss_count_is_refused(int missesBeforeDraw)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => HardPity.Fires(missesBeforeDraw, 10));
        Should.Throw<ArgumentOutOfRangeException>(() => HardPity.Advance(missesBeforeDraw));
    }

    /// <summary>
    /// An <c>N</c> below 1 is refused rather than treated as "always" or "never".
    /// </summary>
    /// <remarks>
    /// <c>N = 0</c> is what an unauthored rung reads as, and under <c>misses + 1 &gt;= N</c> it would
    /// force <em>every</em> draw — the loudest possible way to be silently wrong.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_N_below_one_is_refused(int everyNth)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => HardPity.Fires(0, everyNth));
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
