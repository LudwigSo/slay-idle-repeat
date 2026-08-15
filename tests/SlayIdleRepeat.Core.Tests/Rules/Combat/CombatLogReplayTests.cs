using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// The visual battle is a replay of a pre-computed log, not a live simulation — made checkable
/// rather than aspirational.
/// </summary>
/// <remarks>
/// <see cref="Replayer"/> is a deliberately blunt consumer: it reconstructs tick by tick everything
/// the battle screen draws. Besides the log it holds only the two sides' starting HP and the roster —
/// no stat block, effect definition, status table, RNG or simulator type, and it recomputes nothing.
/// </remarks>
public sealed class CombatLogReplayTests
{
    private const byte Enemy0 = CombatActor.FirstEnemy;
    private const byte Enemy1 = CombatActor.FirstEnemy + 1;

    /// <summary>
    /// A whole small fight: opening ward, trades, a status, a death, a boss phase, a telegraph, a
    /// queued run effect, and the end.
    /// </summary>
    private static SimulationResult Fight()
    {
        var log = new CombatLog();

        // Pre-tick, then BattleStart.
        log.Append(0, CombatEventType.Shield, CombatActor.Hero, CombatActor.Hero, 100.0);
        log.Append(0, CombatEventType.PhaseChange, Enemy0, Enemy0, 1.0);
        log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);

        log.Append(0, CombatEventType.Attack, CombatActor.Hero, Enemy0);
        log.Append(0, CombatEventType.Hit, CombatActor.Hero, Enemy0, 40.0);

        log.Append(10, CombatEventType.Attack, Enemy0, CombatActor.Hero);
        log.Append(10, CombatEventType.Crit, Enemy0, CombatActor.Hero);
        log.Append(10, CombatEventType.Hit, Enemy0, CombatActor.Hero, 30.0);
        log.Append(10, CombatEventType.StatusApplied, Enemy0, CombatActor.Hero, 5.0, StatusBurn);

        log.Append(20, CombatEventType.Attack, CombatActor.Hero, Enemy1);
        log.Append(20, CombatEventType.Miss, CombatActor.Hero, Enemy1);

        // A DoT tick and a HoT tick, distinguished by the sign of Value alone.
        log.Append(30, CombatEventType.StatusTick, Enemy0, CombatActor.Hero, -5.0, StatusBurn);
        log.Append(30, CombatEventType.StatusTick, CombatActor.Hero, CombatActor.Hero, 2.5, StatusRegen);
        log.Append(30, CombatEventType.Heal, CombatActor.Hero, CombatActor.Hero, 12.5);

        log.AppendTelegraph(40, Enemy0, CombatActor.Hero, EffectAllIn, 1.5);

        log.Append(60, CombatEventType.Hit, CombatActor.Hero, Enemy0, 60.0);
        log.Append(60, CombatEventType.PhaseChange, Enemy0, Enemy0, 2.0);

        log.Append(70, CombatEventType.StatusExpired, Enemy0, CombatActor.Hero, 0.0, StatusBurn);
        log.AppendRunEffectQueued(80, Enemy0, EffectScramble, 3.0);

        log.Append(90, CombatEventType.Hit, CombatActor.Hero, Enemy1, 25.0);
        log.Append(90, CombatEventType.ActorDeath, CombatActor.Hero, Enemy1);

        return log.Complete(heroWon: true, 100, 82.5);
    }

    private const ushort StatusBurn = 1;
    private const ushort StatusRegen = 2;
    private const ushort EffectScramble = 41;
    private const ushort EffectAllIn = 42;

    /// <summary>The replayer reconstructs the whole fight from the log alone — no simulator, no stats, no RNG.</summary>
    [Fact]
    public void The_log_alone_reconstructs_the_battle_for_display()
    {
        var result = Fight();

        var replay = Replayer.Play(result.Log, heroStartingHp: 200.0, enemyStartingHp: 100.0);

        // HP bars: hero took 30 and a -5 burn tick, gained a +2.5 regen tick and healed 12.5.
        replay.Hp[CombatActor.Hero].ShouldBe(200.0 - 30.0 - 5.0 + 2.5 + 12.5);

        // The two status ticks are told apart by sign alone, with no content table consulted.
        replay.StatusTicks.ShouldBe([(StatusBurn, -5.0), (StatusRegen, 2.5)]);

        // Enemy 0 took 40 then 60; enemy 1 took 25 and died.
        replay.Hp[Enemy0].ShouldBe(100.0 - 40.0 - 60.0);

        // ActorDeath names the dying actor in TargetId (SourceId is the killer): a replayer that
        // read the wrong slot would delete the wrong sprite.
        replay.Dead.ShouldBe([Enemy1]);
        result.Log.Single(e => e.Type == CombatEventType.ActorDeath)
            .ShouldBe(new CombatEvent(90, CombatEventType.ActorDeath, CombatActor.Hero, Enemy1, 0.0, 0));

        // The opening ward, the status that came and went, the boss band, the telegraph.
        replay.WardGranted[CombatActor.Hero].ShouldBe(100.0);
        replay.ActiveStatuses[CombatActor.Hero].ShouldBeEmpty("BURN was applied at tick 10 and expired at 70");
        replay.Phase[Enemy0].ShouldBe(2);
        replay.Telegraphs.ShouldBe([(40, EffectAllIn, 1.5)]);

        replay.QueuedRunEffects.ShouldBe([(EffectScramble, 3.0)]);

        replay.Finished.ShouldBeTrue();
        replay.LastTick.ShouldBe(result.DurationTicks - 1);
    }

    /// <summary>
    /// A status that is still up at the end stays up in the replay, so the test above's empty
    /// status set is a real expiry rather than the replayer never having seen an application.
    /// </summary>
    [Fact]
    public void A_status_still_active_at_the_end_is_still_active_in_the_replay()
    {
        var log = new CombatLog();
        log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);
        log.Append(5, CombatEventType.StatusApplied, Enemy0, CombatActor.Hero, 5.0, StatusBurn);
        var result = log.Complete(heroWon: true, 20, 100.0);

        Replayer.Play(result.Log, 200.0, 100.0)
            .ActiveStatuses[CombatActor.Hero].ShouldBe([StatusBurn]);
    }

    /// <summary>
    /// Skip is always available: replaying only the last event yields the same finished state as
    /// replaying every one, so a skip needs no simulation.
    /// </summary>
    [Fact]
    public void Skipping_to_the_end_reaches_the_outcome_that_watching_would()
    {
        var result = Fight();

        var partway = Replayer.Play(result.Log, 200.0, 100.0, upToTick: 50);
        var skipped = Replayer.Play(result.Log, 200.0, 100.0);

        // The premise: tick 50 is genuinely mid-fight, so "skip reaches the end" is a claim that
        // can fail. Without this, a replayer that ignored every event would satisfy the rest.
        partway.Finished.ShouldBeFalse();
        partway.Dead.ShouldBeEmpty();
        partway.QueuedRunEffects.ShouldBeEmpty();
        partway.Phase[Enemy0].ShouldBe(1);

        // And the outcome is reachable by reading the rest of the log — no simulation, no RNG.
        skipped.Finished.ShouldBeTrue();
        skipped.Dead.ShouldBe([Enemy1]);
        skipped.QueuedRunEffects.ShouldBe([(EffectScramble, 3.0)]);
        skipped.Phase[Enemy0].ShouldBe(2);
        skipped.LastTick.ShouldBe(result.DurationTicks - 1);
    }

    /// <summary>
    /// The ×1/×2/×3 speed toggle simply consumes the log faster: the log carries an integer tick and
    /// no wall clock, so speed is a rendering-side division.
    /// </summary>
    [Theory]
    [InlineData(1, 20, 170.0)]
    [InlineData(2, 40, 180.0)]
    [InlineData(3, 60, 180.0)]
    public void The_speed_toggle_only_changes_how_much_log_a_second_consumes(
        int speed, int expectedTick, double expectedHeroHp)
    {
        const double oneSecond = 1.0;
        var result = Fight();

        // 20 ticks/second, consumed `speed` times as fast. The window is one second because a half
        // second put ×1 and ×2 in the same event-free stretch of the fight.
        var tick = (int)(oneSecond * CombatLog.TicksPerSecond * speed);
        tick.ShouldBe(expectedTick);

        Replayer.Play(result.Log, 200.0, 100.0, tick).Hp[CombatActor.Hero].ShouldBe(expectedHeroHp);
    }

    /// <summary>
    /// A status's stack count is reconstructible from the log alone: <c>StatusApplied</c> carries the
    /// resulting stack count rather than potency, since counting events cannot substitute —
    /// reapplication may add a stack or merely refresh, and the two are indistinguishable by counting.
    /// </summary>
    [Fact]
    public void The_log_alone_reconstructs_a_statuss_stack_count()
    {
        var events = ReferenceLogs.Instance("status-stack-then-expire");

        StacksAfter(events, upToTick: 20).ShouldBe(1);
        StacksAfter(events, upToTick: 40).ShouldBe(2);
        StacksAfter(events, upToTick: 60).ShouldBe(3, "the fourth application is a refresh at max stacks");
        StacksAfter(events, upToTick: 100).ShouldBe(2, "one stack decayed");
        StacksAfter(events, upToTick: 120).ShouldBe(0, "the status is gone");
    }

    /// <summary>The stack count a replayer would draw at a given tick, read out of the log.</summary>
    private static int StacksAfter(IReadOnlyList<CombatEvent> events, int upToTick) =>
        events
            .Where(e => e.Tick <= upToTick)
            .Where(e => e.Type is CombatEventType.StatusApplied or CombatEventType.StatusExpired)
            .Select(e => (int)e.Value)
            .LastOrDefault();

    /// <summary>
    /// Nothing in the log refers to simulator state — every field is a primitive or an enum — so a
    /// replayer can hold the log and nothing else. A log carrying a reference to the effect instance
    /// that fired would make the replayer depend on the simulator's object graph.
    /// </summary>
    [Fact]
    public void No_field_of_the_log_refers_to_simulator_state()
    {
        var fieldTypes = typeof(CombatEvent)
            .GetProperties()
            .Select(p => p.PropertyType)
            .ToArray();

        fieldTypes.ShouldNotBeEmpty();
        fieldTypes.ShouldAllBe(t => t.IsPrimitive || t.IsEnum);
    }

    /// <summary>
    /// A replayer: everything the battle screen draws, rebuilt from the log and nothing else.
    /// Deliberately dumb — it knows the starting HP of each side and then only reads events.
    /// </summary>
    private sealed class Replayer
    {
        public Dictionary<byte, double> Hp { get; } = [];

        public Dictionary<byte, double> WardGranted { get; } = [];

        public Dictionary<byte, int> Phase { get; } = [];

        public Dictionary<byte, List<ushort>> ActiveStatuses { get; } = [];

        public List<byte> Dead { get; } = [];

        public List<(int Tick, ushort EffectIndex, double LeadSeconds)> Telegraphs { get; } = [];

        public List<(ushort EffectIndex, double Argument)> QueuedRunEffects { get; } = [];

        public List<(ushort StatusId, double Delta)> StatusTicks { get; } = [];

        public bool Finished { get; private set; }

        public int LastTick { get; private set; } = -1;

        public static Replayer Play(
            IReadOnlyList<CombatEvent> log,
            double heroStartingHp,
            double enemyStartingHp,
            int upToTick = int.MaxValue)
        {
            var replay = new Replayer();

            replay.Hp[CombatActor.Hero] = heroStartingHp;
            replay.Hp[Enemy0] = enemyStartingHp;
            replay.Hp[Enemy1] = enemyStartingHp;
            foreach (var actor in replay.Hp.Keys)
            {
                replay.ActiveStatuses[actor] = [];
            }

            foreach (var entry in log)
            {
                if (entry.Tick > upToTick)
                {
                    break;
                }

                replay.LastTick = entry.Tick;
                replay.Apply(entry);
            }

            return replay;
        }

        private void Apply(CombatEvent entry)
        {
            switch (entry.Type)
            {
                case CombatEventType.Hit:
                    Hp[entry.TargetId] -= entry.Value;
                    break;

                case CombatEventType.StatusTick:
                    // Signed: negative is a DoT, positive a HoT. No status content table needed.
                    Hp[entry.TargetId] += entry.Value;
                    StatusTicks.Add((entry.DataId, entry.Value));
                    break;

                case CombatEventType.Heal:
                    Hp[entry.TargetId] += entry.Value;
                    break;

                case CombatEventType.Shield:
                    WardGranted[entry.TargetId] = WardGranted.GetValueOrDefault(entry.TargetId) + entry.Value;
                    break;

                case CombatEventType.StatusApplied:
                    if (!ActiveStatuses[entry.TargetId].Contains(entry.DataId))
                    {
                        ActiveStatuses[entry.TargetId].Add(entry.DataId);
                    }

                    break;

                case CombatEventType.StatusExpired:
                    ActiveStatuses[entry.TargetId].Remove(entry.DataId);
                    break;

                case CombatEventType.ActorDeath:
                    Dead.Add(entry.TargetId);
                    break;

                case CombatEventType.PhaseChange:
                    Phase[entry.TargetId] = (int)entry.Value;
                    break;

                case CombatEventType.Telegraph:
                    Telegraphs.Add((entry.Tick, entry.DataId, entry.Value));
                    break;

                case CombatEventType.RunEffectQueued:
                    QueuedRunEffects.Add((entry.DataId, entry.Value));
                    break;

                case CombatEventType.BattleEnd:
                    Finished = true;
                    break;

                default:
                    // Attack, Crit, Miss, Block, WardBroken, PetAbility, BattleStart — presentation
                    // cues with no state of their own. A renderer animates them; a state
                    // reconstruction does not need to.
                    break;
            }
        }
    }
}
