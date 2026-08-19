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
/// exactly N"</em>. The theories run over every <c>N</c> authored anywhere in <c>luck.json</c>.
/// ⚠️ They pin the primitive at each authored number, not which draw of each rule that number names:
/// <c>DRAFT</c>'s quality floor and upgrade famine author the drafts that pass <em>before</em> the
/// next one is floored, and <c>DraftGuaranteeTests</c> is where those readings are pinned.
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

    /// <summary>
    /// The counter holds misses <em>before</em> the draw, so the N-th draw is the one taken with
    /// <c>N − 1</c> misses on the clock — off by one, every guarantee in the game shifts by a draw.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAuthoredN))]
    public void The_Nth_draw_since_the_last_reset_is_forced(string source, string reference, int everyNth)
    {
        HardPity.Fires(everyNth - 1, everyNth).ShouldBeTrue(
            $"{source} authors {N(everyNth)} at {LuckDocuments.DocumentPath}{reference}. A rung of " +
            $"{N(everyNth)} forces the {N(everyNth)}-th draw since its counter last reset — the one " +
            $"taken with {N(everyNth - 1)} misses on the clock (24 §1 M1).");
    }

    /// <summary>
    /// The half that makes "at exactly N" a claim rather than "at N or earlier": a predicate that
    /// always answered true would pass the case above for every rule in the game.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAuthoredN))]
    public void The_draw_before_the_Nth_is_not_forced(string source, string reference, int everyNth)
    {
        HardPity.Fires(everyNth - 2, everyNth).ShouldBeFalse(
            $"{source} authors {N(everyNth)} at {LuckDocuments.DocumentPath}{reference}. A rung of " +
            $"{N(everyNth)} firing on the {N(everyNth - 1)}-th draw would be a guarantee the " +
            "disclosure page (24 §1.1) does not state — pity that fires early is still pity that is " +
            "mis-stated.");
    }

    [Theory]
    [MemberData(nameof(EveryAuthoredN))]
    public void A_counter_that_has_just_reset_does_not_immediately_fire_again(
        string source, string reference, int everyNth)
    {
        HardPity.Fires(HardPity.Reset(), everyNth).ShouldBeFalse(
            $"{source} authors {N(everyNth)} at {LuckDocuments.DocumentPath}{reference}. A reset " +
            "counter that fired on the very next draw would make the guarantee the drop rate — 24 " +
            "§10 E3 caps a guarantee at 30% of grants in its class.");
    }

    [Fact]
    public void A_fired_counter_goes_back_to_zero()
    {
        HardPity.Reset().ShouldBe(
            0,
            "24 §1 M1: 'counter resets to 0'. Resetting to 1 would shorten every subsequent cycle by " +
            "a draw and resetting to N would fire the guarantee forever.");
    }

    // ---------------------------------------------------------------- isolation between rungs

    /// <summary>
    /// One rung's <c>N</c> never fires another rung's counter — the negative control that keeps the
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
    /// Reachable rather than theoretical: a retune that lowers an <c>N</c> leaves live players
    /// standing above the new rung.
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

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 10)]
    [InlineData(159, 160)]
    public void A_missed_draw_advances_the_counter_by_one(int misses, int expected)
    {
        HardPity.Advance(misses).ShouldBe(expected);
    }

    /// <summary>Stated against the input rather than a literal, so a constant cannot satisfy it.</summary>
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

    /// <summary>
    /// The parameter is pinned, not just the exception type: both arguments state a range, so a
    /// guard that reported <c>everyNth</c> for a bad counter would satisfy a bare type assertion.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_negative_miss_count_is_refused(int missesBeforeDraw)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => HardPity.Fires(missesBeforeDraw, 10))
            .ParamName.ShouldBe("missesBeforeDraw");
        Should.Throw<ArgumentOutOfRangeException>(() => HardPity.Advance(missesBeforeDraw))
            .ParamName.ShouldBe("misses");
    }

    /// <summary>
    /// <c>N = 0</c> is what an unauthored rung reads as, and under <c>misses + 1 &gt;= N</c> it would
    /// force <em>every</em> draw.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_N_below_one_is_refused(int everyNth)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => HardPity.Fires(0, everyNth))
            .ParamName.ShouldBe(
                "everyNth",
                "the counter passed here is legal, so a refusal naming it would be the wrong guard " +
                "firing and the rung would stay unchecked.");
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
