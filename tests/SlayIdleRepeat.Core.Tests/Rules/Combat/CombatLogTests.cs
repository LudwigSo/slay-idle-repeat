using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 The emission contract of <see cref="CombatLog"/> — the rules M2-08, M2-09, M2-10 and M2-12
/// have to honour, each with the failure that proves it is enforced rather than documented.
/// </summary>
public sealed class CombatLogTests
{
    private const byte Enemy0 = CombatActor.FirstEnemy;

    /// <summary>A log with a <c>BattleStart</c> and nothing else — the minimum a fight can produce.</summary>
    private static CombatLog Started()
    {
        var log = new CombatLog();
        log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);
        return log;
    }

    // ---------------------------------------------------------------- ordering

    /// <summary>Events come out in the order they went in — the log is the replay.</summary>
    [Fact]
    public void Events_are_kept_in_emission_order()
    {
        var log = Started();
        log.Append(1, CombatEventType.Attack, CombatActor.Hero, Enemy0);
        log.Append(1, CombatEventType.Crit, CombatActor.Hero, Enemy0);
        log.Append(1, CombatEventType.Hit, CombatActor.Hero, Enemy0, 41.2536);

        log.Events.Select(e => e.Type).ShouldBe(
        [
            CombatEventType.BattleStart,
            CombatEventType.Attack,
            CombatEventType.Crit,
            CombatEventType.Hit,
        ]);
    }

    /// <summary>
    /// 🔒 A tick may repeat — `05` §3.1 puts many state changes in one tick — but it may never go
    /// backwards. That is what makes the log seekable (`05` §8) and what catches a batched flush.
    /// </summary>
    [Fact]
    public void A_tick_may_repeat_but_never_go_backwards()
    {
        var log = Started();
        log.Append(5, CombatEventType.Hit, CombatActor.Hero, Enemy0, 1.0);
        log.Append(5, CombatEventType.Hit, CombatActor.Hero, Enemy0, 1.0);

        var refusal = Should.Throw<InvalidOperationException>(
            () => log.Append(4, CombatEventType.Hit, CombatActor.Hero, Enemy0, 1.0));

        // Ordered, so the message names the offending tick BEFORE the one it followed — a
        // failure that reported them the other way round would send the reader to the wrong event.
        refusal.Message.ShouldMatchWildcard("*tick 4*tick 5*05 §3.1*");
    }

    /// <summary>A tick outside the 90 s cap is refused at both ends (`05` §3).</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(CombatLog.MaxTicks)]
    [InlineData(CombatLog.MaxTicks + 1)]
    public void A_tick_outside_the_fight_is_refused(int tick)
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
                () => log.Append(tick, CombatEventType.Hit, CombatActor.Hero, Enemy0, 1.0))
            .Message.ShouldMatchWildcard($"*tick {tick}*outside 0..1799*`05` §3*");
    }

    /// <summary>The last tick of the fight is admissible — the cap is exclusive, not off by one.</summary>
    [Fact]
    public void The_last_tick_of_the_fight_is_admissible()
    {
        var log = Started();

        Should.NotThrow(() => log.Append(CombatLog.MaxTicks - 1, CombatEventType.Hit, CombatActor.Hero, Enemy0, 1.0));
    }

    // ---------------------------------------------------------------- values

    /// <summary>
    /// 🔒 `05` §1.1 — a value that reaches the log unrounded is refused, and the refusal names the
    /// event rather than "some double" (S2).
    /// </summary>
    [Fact]
    public void An_unrounded_value_is_refused_and_the_event_is_named()
    {
        var log = Started();

        var refusal = Should.Throw<InvalidOperationException>(
            () => log.Append(3, CombatEventType.Hit, CombatActor.Hero, Enemy0, 41.25361234));

        // S2 — the refusal has to identify WHICH event, not merely that some double in the log was
        // unrounded. Ordered, so the message reads as "the Value of a Hit at tick 3 is 41.253…".
        refusal.Message.ShouldMatchWildcard("*Hit*tick 3*41.25361234*`05` §1.1*");
    }

    /// <summary>NaN and the infinities are not combat numbers.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_value_is_refused(double value)
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
            () => log.Append(3, CombatEventType.Hit, CombatActor.Hero, Enemy0, value));
    }

    /// <summary>
    /// 🔒 Negative zero is refused. It equals <c>0.0</c> in C# but carries a different bit pattern,
    /// so two logs the language calls identical would carry different <c>LogHash</c>es — a false
    /// divergence in `11` §6's tamper check.
    /// </summary>
    [Fact]
    public void Negative_zero_is_refused()
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
                () => log.Append(3, CombatEventType.Heal, CombatActor.Hero, CombatActor.Hero, -0.0))
            .Message.ShouldContain("negative zero", Case.Sensitive);

        Should.NotThrow(() => log.Append(3, CombatEventType.Heal, CombatActor.Hero, CombatActor.Hero, 0.0));
    }

    /// <summary>A rounded four-decimal value is exactly what the log is for.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(41.2536)]
    [InlineData(-0.5)]
    [InlineData(8388609.0001)]
    public void A_rounded_finite_value_is_admitted(double value)
    {
        var log = Started();

        Should.NotThrow(() => log.Append(3, CombatEventType.Hit, CombatActor.Hero, Enemy0, value));
    }

    // ---------------------------------------------------------------- the bookends

    /// <summary>
    /// 🔒 `05` §3.1 step 0d emits exactly one <c>BattleStart</c>, and the pre-tick's own events —
    /// the ward grants and opening buffs of step 0b — precede it at the same tick.
    /// </summary>
    /// <remarks>
    /// The ordering is counter-intuitive and is the document's, not a convenience: a replayer that
    /// assumed <c>BattleStart</c> was the first entry would drop <c>PK_WARDED</c>'s opening shield
    /// off the front of every fight.
    /// </remarks>
    [Fact]
    public void The_pre_tick_events_precede_BattleStart_at_tick_zero()
    {
        var log = new CombatLog();
        log.Append(0, CombatEventType.Shield, CombatActor.Hero, CombatActor.Hero, 120.0);
        log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);

        log.Events[0].Type.ShouldBe(CombatEventType.Shield);
        log.Events[1].Type.ShouldBe(CombatEventType.BattleStart);
    }

    /// <summary>A second <c>BattleStart</c> is refused — a replay has one beginning.</summary>
    [Fact]
    public void A_second_BattleStart_is_refused()
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
                () => log.Append(1, CombatEventType.BattleStart, CombatActor.None, CombatActor.None))
            .Message.ShouldContain("second BattleStart", Case.Sensitive);
    }

    /// <summary><c>BattleStart</c> belongs to tick 0, because the pre-tick runs before tick 0.</summary>
    [Fact]
    public void BattleStart_outside_tick_zero_is_refused()
    {
        var log = new CombatLog();

        Should.Throw<InvalidOperationException>(
                () => log.Append(1, CombatEventType.BattleStart, CombatActor.None, CombatActor.None))
            .Message.ShouldContain("pre-tick", Case.Sensitive);
    }

    /// <summary>
    /// <c>BattleEnd</c> is <see cref="CombatLog.Complete"/>'s alone, so "the last event of every log
    /// is BattleEnd" holds by construction.
    /// </summary>
    [Fact]
    public void BattleEnd_may_not_be_appended_directly()
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
                () => log.Append(1, CombatEventType.BattleEnd, CombatActor.None, CombatActor.None))
            .Message.ShouldContain("Complete", Case.Sensitive);
    }

    /// <summary>A log with no <c>BattleStart</c> cannot be completed.</summary>
    [Fact]
    public void A_log_with_no_BattleStart_cannot_be_completed()
    {
        var log = new CombatLog();

        Should.Throw<InvalidOperationException>(() => log.Complete(heroWon: true, 10, 100.0))
            .Message.ShouldContain("BattleStart", Case.Sensitive);
    }

    // ---------------------------------------------------------------- completion

    /// <summary><see cref="CombatLog.Complete"/> appends the terminal event and fills the result.</summary>
    [Fact]
    public void Complete_seals_the_log_with_BattleEnd_and_returns_the_result()
    {
        var log = Started();
        log.Append(4, CombatEventType.Hit, CombatActor.Hero, Enemy0, 41.2536);
        log.Append(9, CombatEventType.ActorDeath, CombatActor.Hero, Enemy0);

        var result = log.Complete(heroWon: true, 10, 214.5);

        result.HeroWon.ShouldBeTrue();
        result.DurationTicks.ShouldBe(10);
        result.HeroHpRemaining.ShouldBe(214.5);
        result.Log.Count.ShouldBe(4);
        result.Log[^1].Type.ShouldBe(CombatEventType.BattleEnd);
        result.Log[^1].Tick.ShouldBe(9);
        result.LogHash.ShouldBe(CanonicalStateWriter.HashCombatLog(result.Log));
    }

    /// <summary>
    /// 🔒 `05` §8 — the log is sealed once the result exists. Skip is safe because
    /// <em>"the outcome is already determined"</em>; a log that can still grow makes that false.
    /// </summary>
    [Fact]
    public void Appending_after_Complete_is_refused()
    {
        var log = Started();
        var result = log.Complete(heroWon: true, 10, 100.0);

        var refusal = Should.Throw<InvalidOperationException>(
            () => log.Append(9, CombatEventType.Hit, CombatActor.Hero, Enemy0, 1.0));

        refusal.Message.ShouldContain("sealed", Case.Sensitive);
        result.Log.Count.ShouldBe(2);
    }

    /// <summary>One battle produces one result.</summary>
    [Fact]
    public void Complete_may_not_be_called_twice()
    {
        var log = Started();
        log.Complete(heroWon: true, 10, 100.0);

        Should.Throw<InvalidOperationException>(() => log.Complete(heroWon: false, 11, 0.0))
            .Message.ShouldContain("twice", Case.Sensitive);
    }

    /// <summary>
    /// The result the caller hands back must cover the log it summarises: a fight cannot be
    /// reported as shorter than its own last event.
    /// </summary>
    [Fact]
    public void A_duration_shorter_than_the_log_is_refused()
    {
        var log = Started();
        log.Append(40, CombatEventType.Hit, CombatActor.Hero, Enemy0, 1.0);

        Should.Throw<InvalidOperationException>(() => log.Complete(heroWon: true, 20, 100.0))
            .Message.ShouldContain("tick 40", Case.Sensitive);
    }

    /// <summary>A duration outside the 90 s cap is refused (`05` §3).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CombatLog.MaxTicks + 1)]
    public void A_duration_outside_the_fight_is_refused(int durationTicks)
    {
        Should.Throw<InvalidOperationException>(() => Started().Complete(heroWon: true, durationTicks, 100.0));
    }

    /// <summary>The hero's remaining HP is rounded like every other combat number (`05` §1.1).</summary>
    [Fact]
    public void An_unrounded_hero_hp_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => Started().Complete(heroWon: true, 10, 214.512345))
            .Message.ShouldContain("heroHpRemaining", Case.Sensitive);
    }

    /// <summary>
    /// The returned log is a snapshot, not a live view: appending to the builder afterwards is
    /// already refused, and the result would not see it even if it were not.
    /// </summary>
    [Fact]
    public void The_result_carries_its_own_copy_of_the_log()
    {
        var log = Started();
        var result = log.Complete(heroWon: true, 10, 100.0);

        result.Log.ShouldNotBeSameAs(log.Events);
    }

    // ---------------------------------------------------------------- telegraphs

    /// <summary>🔒 `17` §1 — a wind-up inside the 1.0–1.5 s band is admitted.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.2)]
    [InlineData(1.3)]
    [InlineData(1.5)]
    public void A_telegraph_inside_the_documented_band_is_admitted(double leadSeconds)
    {
        var log = Started();

        Should.NotThrow(() => log.AppendTelegraph(600, Enemy0, CombatActor.Hero, 41, leadSeconds));

        log.Events[^1].Type.ShouldBe(CombatEventType.Telegraph);
        log.Events[^1].Value.ShouldBe(leadSeconds);
        log.Events[^1].DataId.ShouldBe((ushort)41);
    }

    /// <summary>
    /// 🔒 `17` §1 — and one outside it is refused at both ends. Too short cannot be read; too long
    /// stops reading as a wind-up.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.9)]
    [InlineData(1.6)]
    [InlineData(3.0)]
    public void A_telegraph_outside_the_documented_band_is_refused(double leadSeconds)
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
                () => log.AppendTelegraph(600, Enemy0, CombatActor.Hero, 41, leadSeconds))
            .Message.ShouldMatchWildcard("*wind-up*`17` §1*1–1.5 s band*");
    }

    // ---------------------------------------------------------------- seeking

    /// <summary>
    /// 🔒 `05` §8's speed toggle and skip both seek the log by tick.
    /// <see cref="CombatLog.FirstIndexAtOrAfter"/> finds the first event of a tick, the tick's
    /// whole run of events, and the end of the log for a tick past it.
    /// </summary>
    [Fact]
    public void The_log_can_be_sought_by_tick()
    {
        var log = Started();
        log.Append(5, CombatEventType.Hit, CombatActor.Hero, Enemy0, 1.0);
        log.Append(5, CombatEventType.Crit, CombatActor.Hero, Enemy0);
        log.Append(9, CombatEventType.Hit, Enemy0, CombatActor.Hero, 2.0);
        var events = log.Complete(heroWon: true, 12, 100.0).Log;

        CombatLog.FirstIndexAtOrAfter(events, 0).ShouldBe(0);
        CombatLog.FirstIndexAtOrAfter(events, 1).ShouldBe(1);
        CombatLog.FirstIndexAtOrAfter(events, 5).ShouldBe(1);
        CombatLog.FirstIndexAtOrAfter(events, 6).ShouldBe(3);
        CombatLog.FirstIndexAtOrAfter(events, 9).ShouldBe(3);
        CombatLog.FirstIndexAtOrAfter(events, 12).ShouldBe(events.Count);
    }

    /// <summary>
    /// The seek agrees with a linear scan for every tick of the fight — the binary search is only
    /// valid because ticks are non-decreasing, so the two are pinned against each other rather than
    /// the search being trusted.
    /// </summary>
    [Fact]
    public void Seeking_agrees_with_a_linear_scan_at_every_tick()
    {
        var log = Started();
        for (var tick = 2; tick < 60; tick += 3)
        {
            log.Append(tick, CombatEventType.Hit, CombatActor.Hero, Enemy0, 1.0);
            log.Append(tick, CombatEventType.Attack, Enemy0, CombatActor.Hero);
        }

        var events = log.Complete(heroWon: true, 60, 100.0).Log;

        for (var tick = 0; tick <= 60; tick++)
        {
            var scanned = 0;
            while (scanned < events.Count && events[scanned].Tick < tick)
            {
                scanned++;
            }

            CombatLog.FirstIndexAtOrAfter(events, tick).ShouldBe(scanned, $"at tick {tick}");
        }
    }

    /// <summary>Seeking an empty log finds nothing rather than throwing.</summary>
    [Fact]
    public void Seeking_an_empty_log_finds_the_end()
    {
        CombatLog.FirstIndexAtOrAfter([], 7).ShouldBe(0);
    }

    // ---------------------------------------------------------------- the enrage

    /// <summary>
    /// 🔒 `05` §3.1's <c>SYS_ENRAGE</c> — <c>PERIODIC {interval: 1.0, startDelay: 70.0}</c> — fires
    /// at 1 Hz from 70 s. Across the 90 s cap that is twenty events, not a pathological log.
    /// </summary>
    /// <remarks>
    /// Worth pinning because the enrage is the one effect in the game that fires on a fixed wall
    /// clock rather than in response to something, so it is the one whose log cost can be computed
    /// exactly rather than estimated: <c>(90 − 70) × 1 Hz = 20</c>. Twenty events against a fight's
    /// few thousand.
    /// </remarks>
    [Fact]
    public void Seventy_seconds_of_enrage_stacking_costs_twenty_events()
    {
        var log = Started();

        // 70 s is tick 1400 at 20 ticks/second; the cap is tick 1799.
        var fired = 0;
        for (var tick = 1400; tick < CombatLog.MaxTicks; tick += 20)
        {
            log.Append(tick, CombatEventType.StatusApplied, Enemy0, Enemy0, 1.08, 900);
            fired++;
        }

        fired.ShouldBe(20);
        log.Count.ShouldBe(21);
    }
}
