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

    /// <summary>
    /// 🔒 The account the sweep exists to notice is exactly the one whose numbers are large enough
    /// to overflow a naive <c>long</c> cross-multiply — and a wrapped product goes negative, which
    /// reads as "well inside the envelope".
    /// </summary>
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MaxValue / 2)]
    [InlineData(1_000_000_000_000L)]
    public void A_gain_far_too_large_for_a_long_product_is_still_a_breach(long gained)
    {
        new PlausibilityEnvelope(MaxCurrencyPerDay: 1_000)
            .Breaches(Delta(TimeSpan.FromHours(1), currency: gained))
            .ShouldHaveSingleItem()
            .Gained
            .ShouldBe(
                gained,
                $"{gained} in an hour is astronomically over 1 000/day. A long product of gained × "
                + "one day in ticks wraps above roughly ten million, so the naive arithmetic would "
                + "silently clear the largest cheater in the game.");
    }

    [Fact]
    public void A_measure_that_went_backwards_is_measured_as_a_fall_and_never_flagged()
    {
        var envelope = new PlausibilityEnvelope(
            MaxCurrencyPerDay: 1, MaxLegendXpPerDay: 1, MaxBattleHashMismatchesPerDay: 1);

        var spent = new PlausibilityObservation(Player, Start, WalletTotal: 5_000, LegendXp: 100, BattleHashMismatches: 3);
        var later = new PlausibilityObservation(Player, Start.AddDays(1), WalletTotal: 10, LegendXp: 90, BattleHashMismatches: 1);

        var delta = PlausibilityDelta.Between(spent, later);

        delta.CurrencyGained.ShouldBe(
            -4_990,
            "a wallet is a balance and falls whenever the player spends. Clamping that to zero would "
            + "hide it; refusing it outright would let one ordinary purchase stop the sweep.");
        delta.LegendXpGained.ShouldBe(-10);
        delta.BattleHashMismatchesGained.ShouldBe(-2);

        envelope.Breaches(delta).ShouldBeEmpty(
            "and no fall can ever exceed a non-negative threshold, so a storage fault in a "
            + "monotone measure is never amplified into a flag against a real player.");
    }

    /// <summary>
    /// 🔒 A negative threshold is exceeded by an account that gained NOTHING, so it would turn
    /// "nobody has authored this" into "raise a review entry for every account on every sweep".
    /// </summary>
    [Theory]
    [InlineData(-1L, null, null, nameof(PlausibilityEnvelope.MaxCurrencyPerDay))]
    [InlineData(null, -1L, null, nameof(PlausibilityEnvelope.MaxLegendXpPerDay))]
    [InlineData(null, null, -1L, nameof(PlausibilityEnvelope.MaxBattleHashMismatchesPerDay))]
    public void A_negative_threshold_is_refused_and_the_refusal_names_which_one(
        long? currency, long? xp, long? mismatches, string blamed)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PlausibilityEnvelope(currency, xp, mismatches))
            .ParamName.ShouldBe(
                blamed,
                "three independent thresholds throw the same type; without the name an operator "
                + "cannot tell which of their three settings was refused.");
    }

    [Fact]
    public void A_threshold_of_zero_is_authored_and_flags_any_gain_at_all()
    {
        var envelope = new PlausibilityEnvelope(MaxCurrencyPerDay: 0);

        envelope.AuthoredThresholds.ShouldBe(
            1, "zero is a number somebody wrote down; null is the absence of one.");
        envelope.Breaches(Delta(TimeSpan.FromDays(1), currency: 1)).ShouldHaveSingleItem();
        envelope.Breaches(Delta(TimeSpan.FromDays(1), currency: 0)).ShouldBeEmpty(
            "and zero gained is still not over zero — the negative control at the boundary.");
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
