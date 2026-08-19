using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// The <c>ENHANCE</c> class's guarantee: <c>effectiveRate = min(cap, baseRate + slope × failures)</c>,
/// plus whatever bonus is riding on the attempt.
/// </summary>
/// <remarks>
/// Driven through <c>LuckService</c> rather than the primitive, because the façade is the only door
/// a caller has onto it — a case that reached past the façade would be asserting over a path no
/// production type is allowed to take.
/// </remarks>
public sealed class EnhanceMercyTests
{
    private static EnhanceRule Rule { get; } = LuckTuning.Read(LuckDocuments.LuckOnly()).Enhance;

    /// <summary>The authored slope and ceiling reach the rule rather than a compiled-in pair.</summary>
    [Fact]
    public void The_rule_carries_the_slope_and_ceiling_the_document_authors()
    {
        Rule.MercySlopePerConsecutiveFailure.ShouldBe(LuckDocuments.ShippedEnhanceMercySlope);
        Rule.EffectiveRateCap.ShouldBe(LuckDocuments.ShippedEnhanceRateCap);
        Rule.AdEnhanceLuckStacksAdditively.ShouldBe(LuckDocuments.ShippedEnhanceAdStacksAdditively);
        Rule.AdEnhanceLuckAdvancesCounter.ShouldBe(LuckDocuments.ShippedEnhanceAdAdvancesCounter);
    }

    /// <summary>An item with no run of failures behind it is drawn against its level's own chance.</summary>
    [Fact]
    public void An_item_with_no_failures_behind_it_carries_its_levels_own_chance()
    {
        LuckService.EnhanceSuccessRate(0.25, 0, 0.0, Rule).ShouldBe(0.25);
    }

    /// <summary>
    /// Each consecutive failure adds the authored slope. The rows stop short of the ceiling on
    /// purpose: where the ramp reaches certainty is its own case below.
    /// </summary>
    [Theory]
    [InlineData(0, 0.25)]
    [InlineData(1, 0.33)]
    [InlineData(2, 0.41)]
    [InlineData(5, 0.65)]
    [InlineData(9, 0.97)]
    public void Each_consecutive_failure_raises_the_next_attempt_by_the_authored_slope(
        int failures, double rate)
    {
        LuckService.EnhanceSuccessRate(0.25, failures, 0.0, Rule).ShouldBe(rate, 1e-12);
    }

    /// <summary>The ramp is clamped at the ceiling rather than climbing past certainty.</summary>
    [Fact]
    public void The_ramp_stops_at_the_authored_ceiling()
    {
        LuckService.EnhanceSuccessRate(0.25, 50, 0.0, Rule).ShouldBe(1.0);
    }

    /// <summary>
    /// The counter holds the failures BEFORE the attempt, so ten failures make the ELEVENTH attempt
    /// certain (<c>0.25 + 0.08 × 10 = 1.05</c>, clamped) while the tenth stands at 0.97 — a slope
    /// applied to the attempt's ordinal would answer 1.0 one attempt early.
    /// </summary>
    [Fact]
    public void The_hardest_levels_ramp_is_certain_on_the_eleventh_attempt_and_not_the_tenth()
    {
        LuckService.EnhanceSuccessRate(0.25, 9, 0.0, Rule).ShouldBe(0.97, 1e-12);
        LuckService.EnhanceSuccessRate(0.25, 10, 0.0, Rule).ShouldBe(1.0);
    }

    /// <summary>
    /// 🔒 A lucky bonus stacks ON TOP of the mercy rather than replacing it. The fixture is
    /// discriminating: the two halves are different numbers, so a rule that took one and dropped the
    /// other lands somewhere else.
    /// </summary>
    [Fact]
    public void A_lucky_bonus_stacks_on_top_of_the_mercy_the_item_earned()
    {
        LuckService.EnhanceSuccessRate(0.25, 2, 0.15, Rule).ShouldBe(0.56, 1e-12);
    }

    /// <summary>The ceiling is applied once, at the end — a bonus cannot push the rate past it.</summary>
    [Fact]
    public void A_lucky_bonus_cannot_push_the_rate_past_the_ceiling()
    {
        LuckService.EnhanceSuccessRate(0.9, 2, 0.15, Rule).ShouldBe(1.0);
    }

    /// <summary>
    /// The mercy's own contribution is answered separately, because the client shows it broken out —
    /// <em>"Success 41% (+16% mercy)"</em> — and a caller subtracting two numbers itself would
    /// disagree the moment the ramp clips.
    /// </summary>
    [Fact]
    public void The_mercys_own_contribution_is_answered_apart_from_the_rate()
    {
        LuckService.EnhanceMercyShare(0.25, 2, Rule).ShouldBe(0.16, 1e-12);
    }

    /// <summary>Where the ramp clips, the share reported is what the mercy actually contributed.</summary>
    [Fact]
    public void A_clipped_ramp_reports_only_the_share_it_actually_contributed()
    {
        LuckService.EnhanceMercyShare(0.9, 5, Rule).ShouldBe(0.1, 1e-12);
    }

    /// <summary>A failure advances the counter; a success clears it.</summary>
    /// <param name="before">The counter before the attempt.</param>
    /// <param name="succeeded">Whether the attempt landed.</param>
    /// <param name="after">The counter to store.</param>
    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(3, false, 4)]
    [InlineData(0, true, 0)]
    [InlineData(7, true, 0)]
    public void A_failure_advances_the_counter_and_a_success_clears_it(
        int before, bool succeeded, int after)
    {
        LuckService.EnhanceFailuresAfter(before, succeeded, carriedLuckyBonus: false, Rule)
            .ShouldBe(after);
    }

    /// <summary>
    /// 🔒 An attempt carrying a lucky bonus neither advances nor consumes the counter — the bonus
    /// rides on top of the mercy the item has built up rather than spending it.
    /// </summary>
    [Fact]
    public void A_failed_attempt_carrying_a_lucky_bonus_leaves_the_counter_standing()
    {
        LuckService.EnhanceFailuresAfter(3, succeeded: false, carriedLuckyBonus: true, Rule)
            .ShouldBe(3);
    }

    /// <summary>
    /// …and a document that authors <c>adEnhanceLuckAdvancesCounter = true</c> advances it, so the
    /// standing case above is a claim about the <b>data</b>: the shipped value is false, and with
    /// only that value driven the flag's negation could be deleted with the whole file staying green.
    /// </summary>
    [Fact]
    public void A_document_that_advances_on_a_lucky_bonus_advances_the_counter()
    {
        var advancing = LuckTuning.Read(
            LuckDocuments.LuckOnly(enhanceAdAdvancesCounter: ContentValue.True)).Enhance;

        advancing.AdEnhanceLuckAdvancesCounter.ShouldBeTrue("the fixture's premise");
        Rule.AdEnhanceLuckAdvancesCounter.ShouldBeFalse(
            "…and the shipped block says otherwise, which is what makes this pair a pair.");

        LuckService.EnhanceFailuresAfter(3, succeeded: false, carriedLuckyBonus: true, advancing)
            .ShouldBe(
                4,
                "with the flag authored true a helped failure counts like any other. The case above " +
                "asserts the opposite over the shipped block, so between them the rule is pinned to " +
                "the document rather than to a hard-coded answer.");
    }

    /// <summary>A success clears the counter whether or not the attempt was helped.</summary>
    [Fact]
    public void A_successful_attempt_clears_the_counter_however_it_was_helped()
    {
        LuckService.EnhanceFailuresAfter(3, succeeded: true, carriedLuckyBonus: true, Rule)
            .ShouldBe(0);
    }

    /// <summary>A negative bonus would be a penalty dressed as a reward.</summary>
    [Fact]
    public void A_negative_lucky_bonus_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => _ = LuckService.EnhanceSuccessRate(0.25, 0, -0.1, Rule));
    }

    /// <summary>A negative failure count is not a state an item can be in.</summary>
    [Fact]
    public void A_negative_failure_count_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => _ = LuckService.EnhanceSuccessRate(0.25, -1, 0.0, Rule));
    }

    /// <summary>
    /// 🔒 The <c>ENHANCE</c> class states its protection in a shape the ladder path does not serve,
    /// so asking the façade to resolve it is refused by name rather than drawn against a table
    /// nobody authored.
    /// </summary>
    [Fact]
    public void The_ladder_path_still_refuses_the_enhance_class_by_name()
    {
        var tuning = LuckTuning.Read(LuckDocuments.LuckOnly());

        Should.Throw<InvalidTunableException>(() => _ = tuning.Ladder(SourceClass.ENHANCE))
            .Message.ShouldContain("enhance");
    }
}
