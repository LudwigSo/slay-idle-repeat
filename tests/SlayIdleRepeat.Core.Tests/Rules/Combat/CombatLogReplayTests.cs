using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05`'s headnote — <em>"the visual battle is a <b>replay of a pre-computed log</b>, not a live
/// simulation. Compute first, then animate."</em> — made checkable rather than aspirational.
/// </summary>
/// <remarks>
/// <para>
/// <b>How sufficiency is verified here.</b> <see cref="Replayer"/> below is a deliberately blunt
/// consumer: it holds nothing but the roster sizes and the log, has no access to stat blocks,
/// effect definitions, the RNG or any simulator type, and never recomputes a number. It
/// reconstructs, tick by tick, everything `05` §8 says the battle screen draws — each actor's HP
/// bar, who is alive, which statuses are up, the boss's phase band, and the queued run effects. If
/// it can do that, the log is sufficient for display; if it needed one value the log does not
/// carry, this file would not compile.
/// </para>
/// <para>
/// That is the real content of the claim. M7-06 builds the actual renderer, and there is no Godot
/// client until M7 — so what M2-15 owes is a log a renderer <i>can</i> be built against, and the
/// way to owe that honestly is to build the smallest possible one now.
/// </para>
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

        // Pre-tick (`05` §3.1 step 0b), then BattleStart at step 0d.
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

        log.Append(30, CombatEventType.StatusTick, Enemy0, CombatActor.Hero, 5.0, StatusBurn);
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
    private const ushort EffectScramble = 41;
    private const ushort EffectAllIn = 42;

    /// <summary>
    /// 🔒 The replayer reconstructs the whole fight from the log alone — no simulator, no stats, no
    /// RNG, no recomputation.
    /// </summary>
    [Fact]
    public void The_log_alone_reconstructs_the_battle_for_display()
    {
        var result = Fight();

        var replay = Replayer.Play(result.Log, heroStartingHp: 200.0, enemyStartingHp: 100.0);

        // HP bars: hero took 30 + a 5 burn tick, healed 12.5.
        replay.Hp[CombatActor.Hero].ShouldBe(200.0 - 30.0 - 5.0 + 12.5);

        // Enemy 0 took 40 then 60; enemy 1 took 25 and died.
        replay.Hp[Enemy0].ShouldBe(100.0 - 40.0 - 60.0);
        replay.Dead.ShouldBe([Enemy1]);

        // The opening ward, the status that came and went, the boss band, the telegraph.
        replay.WardGranted[CombatActor.Hero].ShouldBe(100.0);
        replay.ActiveStatuses[CombatActor.Hero].ShouldBeEmpty("BURN was applied at tick 10 and expired at 70");
        replay.Phase[Enemy0].ShouldBe(2);
        replay.Telegraphs.ShouldBe([(40, EffectAllIn, 1.5)]);

        // And the queued run effect, which nothing in M2 consumes (`18` §2.5).
        replay.QueuedRunEffects.ShouldBe([(EffectScramble, 3.0)]);

        replay.Finished.ShouldBeTrue();
        replay.LastTick.ShouldBe(result.DurationTicks - 1);
    }

    /// <summary>
    /// 🔒 A status that is still up at the end stays up in the replay — the replayer tracks state,
    /// so the test above's empty status set is a real expiry rather than the replayer never having
    /// seen an application.
    /// </summary>
    /// <remarks>
    /// Without this, <c>ActiveStatuses.ShouldBeEmpty()</c> would pass just as happily against a
    /// replayer that ignored <c>StatusApplied</c> altogether.
    /// </remarks>
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
    /// 🔒 `05` §8 — <b>skip is always available</b>, <em>"the outcome is already determined, so this
    /// is safe and must be offered"</em>. Replaying only the last event yields the same finished
    /// state as replaying every one, so a skip needs no simulation.
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
    /// 🔒 `05` §8 — the ×1/×2/×3 toggle <em>"simply consumes the log faster"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim, stated so it can fail: after the same half second of wall clock, a ×1 replay has
    /// consumed 10 ticks, ×2 has consumed 20 and ×3 has consumed 30 — and each shows the state the
    /// log says holds at that tick, against a trajectory written out here rather than recomputed
    /// from the replayer.
    /// </para>
    /// <para>
    /// What makes that possible is that the log carries an integer <b>tick</b> and no wall clock at
    /// all, so speed is a rendering-side division and nothing in the log participates in it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1, 10, 170.0)]
    [InlineData(2, 20, 170.0)]
    [InlineData(3, 30, 177.5)]
    public void The_speed_toggle_only_changes_how_much_log_a_second_consumes(
        int speed, int expectedTick, double expectedHeroHp)
    {
        const double halfASecond = 0.5;
        var result = Fight();

        // 20 ticks/second (`05` §3), consumed `speed` times as fast.
        var tick = (int)(halfASecond * 20 * speed);
        tick.ShouldBe(expectedTick);

        Replayer.Play(result.Log, 200.0, 100.0, tick).Hp[CombatActor.Hero].ShouldBe(expectedHeroHp);
    }

    /// <summary>
    /// 🔒 Nothing in the log refers to simulator state. Every field is an integer tick, an
    /// enum, a byte actor id, a <see cref="double"/> and a <see cref="ushort"/> content id — so a
    /// replayer can hold the log and nothing else.
    /// </summary>
    /// <remarks>
    /// This is what "compute-then-animate" means structurally. A log carrying, say, a reference to
    /// the effect instance that fired would make the replayer depend on the simulator's object
    /// graph, and `05`'s headnote would quietly become false.
    /// </remarks>
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
    /// </summary>
    /// <remarks>
    /// Deliberately dumb. It knows the starting HP of each side — which the pre-battle banner
    /// already shows the player (`05` §8) — and then only reads events. It never computes damage,
    /// never consults a stat block, never touches the RNG, and names no simulator type.
    /// </remarks>
    private sealed class Replayer
    {
        public Dictionary<byte, double> Hp { get; } = [];

        public Dictionary<byte, double> WardGranted { get; } = [];

        public Dictionary<byte, int> Phase { get; } = [];

        public Dictionary<byte, List<ushort>> ActiveStatuses { get; } = [];

        public List<byte> Dead { get; } = [];

        public List<(int Tick, ushort EffectIndex, double LeadSeconds)> Telegraphs { get; } = [];

        public List<(ushort EffectIndex, double Argument)> QueuedRunEffects { get; } = [];

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
                case CombatEventType.StatusTick:
                    Hp[entry.TargetId] -= entry.Value;
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
