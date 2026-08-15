using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>The emission contract of <see cref="CombatLog"/>, each rule with the failure that proves it is enforced.</summary>
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

    /// <summary>A tick may repeat but may never go backwards — that is what makes the log seekable.</summary>
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

    /// <summary>A tick outside the 90 s cap is refused at both ends.</summary>
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

    /// <summary>A value that reaches the log unrounded is refused, and the refusal names the event.</summary>
    [Fact]
    public void An_unrounded_value_is_refused_and_the_event_is_named()
    {
        var log = Started();

        var refusal = Should.Throw<InvalidOperationException>(
            () => log.Append(3, CombatEventType.Hit, CombatActor.Hero, Enemy0, 41.25361234));

        // Pattern is ordered so the message reads as "the Value of a Hit at tick 3 is 41.253...".
        refusal.Message.ShouldMatchWildcard("*Hit*tick 3*41.25361234*`05` §1.1*");
    }

    /// <summary>
    /// NaN and the infinities are not combat numbers. The message is asserted, not just the
    /// exception type: <c>Math.Round(NaN, 4) != NaN</c> is true, so the 4-dp guard alone would also
    /// throw on a NaN, with the wrong explanation.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_value_is_refused(double value)
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
                () => log.Append(3, CombatEventType.Hit, CombatActor.Hero, Enemy0, value))
            .Message.ShouldContain("A combat number is finite", Case.Sensitive);
    }

    /// <summary>
    /// Negative zero is refused: it equals <c>0.0</c> in C# but carries a different bit pattern, so
    /// two logs the language calls identical could hash to different values.
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
    /// Pre-tick events — opening ward grants and buffs — precede <c>BattleStart</c> at the same tick.
    /// A replayer that assumed <c>BattleStart</c> was the first entry would drop an opening shield off
    /// the front of every fight.
    /// </summary>
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

        // The whole record, not just Type and Tick: BattleEnd is inside the hashed log, so its actor
        // ids and DataId matter as much as its type.
        result.Log[^1].ShouldBe(new CombatEvent(
            9, CombatEventType.BattleEnd, CombatActor.None, CombatActor.None, 0.0, CombatLog.NoDataId));

        result.LogHash.ShouldBe(CanonicalStateWriter.HashCombatLog(result.Log));
    }

    /// <summary>
    /// The event type is a closed vocabulary — an undefined value would be hashed as its ordinal
    /// and replay as nothing.
    /// </summary>
    [Fact]
    public void An_undefined_event_type_is_refused()
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
                () => log.Append(1, (CombatEventType)42, CombatActor.Hero, Enemy0))
            .Message.ShouldMatchWildcard("*42 is not a CombatEventType*closed vocabulary*");
    }

    /// <summary>
    /// The two members with rules of their own cannot be smuggled past those rules through the
    /// general <see cref="CombatLog.Append"/> — otherwise a raw append could put an out-of-band
    /// wind-up or a run-effect target with no meaning into the log.
    /// </summary>
    [Theory]
    [InlineData(nameof(CombatEventType.Telegraph))]
    [InlineData(nameof(CombatEventType.RunEffectQueued))]
    public void A_member_with_its_own_rules_may_not_be_appended_directly(string member)
    {
        var log = Started();
        var type = Enum.Parse<CombatEventType>(member, ignoreCase: false);

        Should.Throw<InvalidOperationException>(() => log.Append(1, type, Enemy0, CombatActor.Hero, 99.0, 7))
            .Message.ShouldMatchWildcard("*was appended through Append*Use AppendTelegraph or AppendRunEffectQueued*");
    }

    /// <summary>
    /// <see cref="CombatEventType.BattleStart"/> names no actor: a client that filled the slots and
    /// a server that did not would compute different log hashes for an identical fight.
    /// </summary>
    [Theory]
    [InlineData(CombatActor.Hero, CombatActor.None)]
    [InlineData(CombatActor.None, CombatActor.Hero)]
    [InlineData(CombatActor.Hero, CombatActor.Hero)]
    public void BattleStart_naming_an_actor_is_refused(byte sourceId, byte targetId)
    {
        var log = new CombatLog();

        Should.Throw<InvalidOperationException>(
                () => log.Append(0, CombatEventType.BattleStart, sourceId, targetId))
            .Message.ShouldMatchWildcard("*BattleStart names actors*`11` §6*");
    }

    /// <summary>The events view cannot be cast back to something appendable.</summary>
    /// <remarks>
    /// <c>Complete</c>'s seal is only as strong as the collection it hands out: a caller that could
    /// cast <see cref="CombatLog.Events"/> to <c>List&lt;CombatEvent&gt;</c> could append past it,
    /// and one that could cast <see cref="SimulationResult.Log"/> to <c>CombatEvent[]</c> could
    /// rewrite a replay after its hash was computed.
    /// </remarks>
    [Fact]
    public void The_log_is_not_writable_through_the_collections_it_hands_out()
    {
        var log = Started();
        var result = log.Complete(heroWon: true, 10, 100.0);

        // Neither is the underlying mutable container, so neither can be cast back to one.
        log.Events.ShouldNotBeAssignableTo<List<CombatEvent>>();
        result.Log.ShouldNotBeAssignableTo<CombatEvent[]>();
        result.Log.ShouldNotBeAssignableTo<List<CombatEvent>>();

        // A ReadOnlyCollection<T> does implement IList<T> — so the guarantee that matters is that
        // writing through it fails rather than that the interface is absent.
        var asList = (IList<CombatEvent>)result.Log;
        Should.Throw<NotSupportedException>(() => asList[0] = default);
        Should.Throw<NotSupportedException>(() => asList.Add(default));
        Should.Throw<NotSupportedException>(() => ((IList<CombatEvent>)log.Events).Add(default));
    }

    /// <summary>The log is sealed once the result exists — a log that can still grow makes replay unsafe.</summary>
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

    /// <summary>
    /// A duration outside the 90 s cap is refused, its own message and not the later
    /// "shorter than the log it summarises" guard, which <c>0</c> and <c>-1</c> would also trip.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CombatLog.MaxTicks + 1)]
    public void A_duration_outside_the_fight_is_refused(int durationTicks)
    {
        Should.Throw<InvalidOperationException>(
                () => Started().Complete(heroWon: true, durationTicks, 100.0))
            .Message.ShouldContain($"outside 1..{CombatLog.MaxTicks}", Case.Sensitive);
    }

    /// <summary>The hero's remaining HP obeys every rule a logged number does.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.0)]
    public void A_hero_hp_that_is_not_a_logged_number_is_refused(double heroHpRemaining)
    {
        Should.Throw<InvalidOperationException>(
                () => Started().Complete(heroWon: true, 10, heroHpRemaining))
            .Message.ShouldContain("heroHpRemaining", Case.Sensitive);
    }

    /// <summary>A negative HP total is an unclamped subtraction upstream, not a legal outcome.</summary>
    [Fact]
    public void A_negative_hero_hp_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => Started().Complete(heroWon: false, 10, -5.0))
            .Message.ShouldContain("unclamped subtraction", Case.Sensitive);

        Should.NotThrow(() => Started().Complete(heroWon: false, 10, 0.0));
    }

    /// <summary>The hero's remaining HP is rounded like every other combat number.</summary>
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

    /// <summary>A wind-up inside the 1.0–1.5 s band is admitted.</summary>
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

    /// <summary>A lead outside the band is refused at both ends: too short cannot be read, too long stops reading as a wind-up.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.9)]
    [InlineData(1.6)]
    [InlineData(3.0)]
    [InlineData(double.NaN)]
    public void A_telegraph_outside_the_documented_band_is_refused(double leadSeconds)
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
                () => log.AppendTelegraph(600, Enemy0, CombatActor.Hero, 41, leadSeconds))
            .Message.ShouldMatchWildcard("*wind-up*`17` §1*1–1.5 s band*");
    }

    /// <summary>
    /// A wind-up must be a whole number of ticks: the simulation is fixed-tick, so <c>1.0001</c> s
    /// (inside the band at 4 dp) lands at tick 20.002 and announces nothing.
    /// </summary>
    [Theory]
    [InlineData(1.0001)]
    [InlineData(1.234)]
    [InlineData(1.4999)]
    public void A_telegraph_that_is_not_a_whole_number_of_ticks_is_refused(double leadSeconds)
    {
        var log = Started();

        Should.Throw<InvalidOperationException>(
                () => log.AppendTelegraph(600, Enemy0, CombatActor.Hero, 41, leadSeconds))
            .Message.ShouldMatchWildcard("*fixed-tick*between two ticks*");
    }

    // ---------------------------------------------------------------- seeking

    /// <summary>
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
    /// <c>SYS_ENRAGE</c> fires at 1 Hz from 70 s: across the 90 s cap that is twenty events, not a
    /// pathological log.
    /// </summary>
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
