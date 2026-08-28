using Shouldly;
using SlayIdleRepeat.Application.Moderation;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Moderation;

/// <summary>
/// 🔒 The envelope's central claim: the shipped one has no thresholds at all, so it flags nothing —
/// and the machinery underneath it demonstrably would flag, given a number, so "flags nothing" is a
/// statement about the missing thresholds rather than about broken code.
/// </summary>
public sealed class PlausibilityEnvelopeTests
{
    private static readonly PlayerId Player = new("PLAYER_subject");
    private static readonly DateTimeOffset Start = new(2026, 8, 20, 5, 0, 0, TimeSpan.Zero);

    private static PlausibilityDelta Delta(
        TimeSpan window, long currency = 0, long xp = 0, long mismatches = 0) =>
        new(Player, window, currency, xp, mismatches);

    [Fact]
    public void The_shipped_envelope_authors_no_threshold_at_all()
    {
        var envelope = PlausibilityEnvelope.Unauthored;

        envelope.MaxCurrencyPerDay.ShouldBeNull(
            "🔒 no design document states a plausible currency rate for this game. A number here "
            + "would look exactly as authoritative as a measured one while flagging real players.");
        envelope.MaxLegendXpPerDay.ShouldBeNull();
        envelope.MaxBattleHashMismatchesPerDay.ShouldBeNull();
        envelope.AuthoredThresholds.ShouldBe(
            0,
            "this count is what the composition root reads to say out loud at startup that the "
            + "sweep is watching nothing — a deployment must never quietly believe otherwise.");
    }

    [Fact]
    public void An_unauthored_envelope_flags_nothing_no_matter_how_extreme_the_delta()
    {
        var absurd = Delta(TimeSpan.FromMinutes(1), currency: long.MaxValue / 2, xp: long.MaxValue / 2, mismatches: 10_000);

        PlausibilityEnvelope.Unauthored.Breaches(absurd).ShouldBeEmpty(
            "null is 'nobody has authored this number', never 'no limit' and never 'use a default'. "
            + "Coercing it to anything at read time is exactly the hole this shape exists to keep open.");
    }

    /// <summary>
    /// The negative control that stops the test above from being vacuous: given a threshold, the
    /// very same code path does flag.
    /// </summary>
    [Fact]
    public void An_authored_threshold_flags_the_measure_it_names_and_only_that_measure()
    {
        var envelope = new PlausibilityEnvelope(MaxCurrencyPerDay: 1_000);

        var breaches = envelope.Breaches(Delta(TimeSpan.FromDays(1), currency: 1_001, xp: 999_999_999));

        breaches.Count.ShouldBe(1, "the two unauthored measures stay unauthored.");
        breaches[0].Measure.ShouldBe(PlausibilityMeasure.CURRENCY_PER_DAY);
        breaches[0].Gained.ShouldBe(1_001);
        breaches[0].ThresholdPerDay.ShouldBe(1_000);
        breaches[0].Window.ShouldBe(TimeSpan.FromDays(1));
        breaches[0].Reason.ShouldContain(
            "1001",
            Case.Sensitive,
            "the reason is what a reviewer re-derives the flag from, so it carries the observed "
            + "number rather than a verdict.");
    }

    [Fact]
    public void A_delta_exactly_on_the_threshold_is_not_a_breach()
    {
        new PlausibilityEnvelope(MaxCurrencyPerDay: 1_000)
            .Breaches(Delta(TimeSpan.FromDays(1), currency: 1_000))
            .ShouldBeEmpty("the threshold is the highest plausible rate, not the lowest implausible one.");
    }

    /// <summary>
    /// The window arithmetic, at three very different window lengths: the comparison is
    /// gained × oneDay > threshold × window, so no window rounds a rate into or out of a flag.
    /// </summary>
    [Theory]
    [InlineData(1, 1_000, true)]         // 1 h × 1 000 = 24 000/day, over
    [InlineData(1, 833, false)]          // 1 h × 833 = 19 992/day, under
    [InlineData(24, 20_001, true)]       // one day, one over
    [InlineData(24, 20_000, false)]      // one day, exactly on
    [InlineData(720, 600_001, true)]     // 30 days, one unit over 600 000 — no window rounds this away
    [InlineData(720, 600_000, false)]    // 30 days, exactly 20 000/day
    public void The_breach_test_is_exact_at_every_window_length(
        int windowHours, long gained, bool breached)
    {
        var breaches = new PlausibilityEnvelope(MaxLegendXpPerDay: 20_000)
            .Breaches(Delta(TimeSpan.FromHours(windowHours), xp: gained));

        breaches.Any().ShouldBe(
            breached,
            $"{gained} XP over {windowHours} h against 20 000/day. The inequality is integer "
            + "cross-multiplication, so a 1 h window and a 30 d window are judged identically.");
    }

    [Fact]
    public void A_window_that_is_not_positive_is_judged_by_nothing()
    {
        new PlausibilityEnvelope(MaxCurrencyPerDay: 1)
            .Breaches(Delta(TimeSpan.Zero, currency: 1_000_000))
            .ShouldBeEmpty(
                "two observations at one instant carry no rate at all. Dividing by that window "
                + "would flag every account the first time a sweep ran twice in a tick.");
    }

    [Fact]
    public void Each_of_the_three_measures_can_be_authored_independently()
    {
        var all = new PlausibilityEnvelope(
            MaxCurrencyPerDay: 1, MaxLegendXpPerDay: 1, MaxBattleHashMismatchesPerDay: 1);

        all.AuthoredThresholds.ShouldBe(3);

        var breaches = all.Breaches(Delta(TimeSpan.FromDays(1), currency: 2, xp: 2, mismatches: 2));

        breaches.Select(b => b.Measure).ShouldBe(
            new[]
            {
                PlausibilityMeasure.CURRENCY_PER_DAY,
                PlausibilityMeasure.LEGEND_XP_PER_DAY,
                PlausibilityMeasure.BATTLE_HASH_MISMATCHES_PER_DAY,
            },
            ignoreOrder: true,
            "the battle-hash tally is a measure in its own right — it is the one signal the "
            + "anti-cheat design already counts, and a sweep that ignored it would ignore the only "
            + "evidence of manipulation the server itself produces.");
    }

    [Fact]
    public void A_delta_is_the_movement_between_two_readings_of_one_account()
    {
        var previous = new PlausibilityObservation(Player, Start, WalletTotal: 100, LegendXp: 50, BattleHashMismatches: 1);
        var current = new PlausibilityObservation(Player, Start.AddHours(6), WalletTotal: 400, LegendXp: 90, BattleHashMismatches: 4);

        var delta = PlausibilityDelta.Between(previous, current);

        delta.Player.ShouldBe(Player);
        delta.Window.ShouldBe(TimeSpan.FromHours(6));
        delta.CurrencyGained.ShouldBe(300);
        delta.LegendXpGained.ShouldBe(40);
        delta.BattleHashMismatchesGained.ShouldBe(3);
    }

    [Fact]
    public void A_delta_across_two_different_accounts_is_refused_rather_than_computed()
    {
        var previous = new PlausibilityObservation(Player, Start, 100, 50, 1);
        var other = new PlausibilityObservation(new PlayerId("PLAYER_other"), Start.AddHours(1), 400, 90, 4);

        Should.Throw<ArgumentException>(() => PlausibilityDelta.Between(previous, other));
    }

    [Fact]
    public void A_delta_that_runs_backwards_in_time_is_refused_rather_than_computed()
    {
        var previous = new PlausibilityObservation(Player, Start.AddHours(1), 100, 50, 1);
        var current = new PlausibilityObservation(Player, Start, 400, 90, 4);

        Should.Throw<ArgumentException>(() => PlausibilityDelta.Between(previous, current));
    }
}
