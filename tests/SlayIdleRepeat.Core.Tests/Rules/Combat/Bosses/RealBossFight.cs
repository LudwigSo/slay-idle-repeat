using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 M2-R3 — one authored boss fight through the REAL engine: <see cref="AttackPipeline"/> and
/// <see cref="StatusTimeline"/>, exactly the composition <see cref="Bosses.BossFight.Run"/> uses for
/// production and the balance harness — <b>not</b> <see cref="BossTestBench"/>'s fakes.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Why this exists beside <see cref="BossFight"/> and <see cref="BossTestBench"/> rather than
/// reusing either.</b> <see cref="Bosses.BossFight.Run"/> (what
/// <c>CombatSimulator.SimulateBossFight</c> calls) takes only an <see cref="ActorStats"/> hero — it
/// has no way to give the hero a held effect, because M2 wires no hero perk/gear/talent path into a
/// boss fight at all (that integration is M3+'s). <see cref="BossTestBench"/> is the opposite
/// problem: its <c>Run</c> composes <c>RecordingAttackPipeline</c> (deals no damage) and
/// <c>RecordingStatuses</c> (applies nothing), which is exactly right for the phase-machinery suite
/// it serves but cannot prove a real <c>DAMAGE</c>/<c>EXTEND_STATUS</c>/<c>STAT_COPY</c>/
/// <c>APPLY_STATUS</c> resolves against the real engine — which is the whole point of the M2-R3
/// regression: nobody could add a test that runs the real engine to completion before this fix,
/// because it faulted.
/// </para>
/// <para>
/// 🔒 <b>The one hero-held effect this bench seeds, and why.</b>
/// <c>BOSS_COGITATOR_PRIME_P2_RECALIBRATE</c> is a <c>STAT_COPY HIGHEST_PCT_BONUS</c> whose source
/// is — after the M2-R3 <c>CURRENT_TARGET</c> fix — the hero. <c>HighestPercentBonusStat</c> throws
/// (by design — steering S6, see <c>CombatFlowState.HighestPercentBonusStat</c>'s own remarks) when
/// the source carries no percent bucket at all, and a percent bucket is written <b>only</b> by
/// <c>STAT_COPY</c> itself (`18` §2.4). A hero built through today's production entry points
/// (<c>BossFight.Run</c>, the balance harness's <c>ParHero</c>) never carries one — no hero
/// perk/gear/talent integration exists yet to write it — so Recalibrate would still fault against a
/// bare hero even after the CURRENT_TARGET fix, for a reason this task was not asked to close and
/// must not paper over by weakening <c>HighestPercentBonusStat</c>'s S6 refusal. What this bench
/// grants the hero instead is a single, harmless <c>STAT_COPY</c> of its own — <c>LIFESTEAL</c>
/// (base <c>0.0</c>, so the copy is a real write of a real <c>0.0</c>, not a fabricated bonus and
/// with no effect on the fight's balance) — so the percent bucket a real gear/talent build will one
/// day populate is standing in, honestly, for a mechanism M2 has not built yet. See the M2-R3
/// completion report for this recorded as a finding, not a fix.
/// </para>
/// </remarks>
internal static class RealBossFight
{
    /// <summary>`14` §8.1 — an arbitrary but fixed battle seed, used across this bench's fights.</summary>
    internal const ulong BattleSeed = 0xB055_C0DE_0000_0003UL;

    /// <summary>
    /// A hero stat block strong enough to survive a full `05` §3 fight against any of the nine
    /// authored bosses (at <see cref="BossPower"/>) and to bring one down through phase 3 well inside
    /// the 90 s bound — calibrated empirically against the shipped
    /// <c>content/bosses/bosses.json</c> (see the M2-R3 completion report for the numbers each boss
    /// produced). Not a balanced build: DEF and Max HP are deliberately extreme so the fight's outcome
    /// is never in doubt and every tick is about the boss's own mechanics, not a coin-flip loss.
    /// </summary>
    internal static ActorStats Hero() => ActorStats.From(new Dictionary<StatId, double>
    {
        [StatId.MAX_HP] = 1_000_000_000.0,
        [StatId.ATK] = 420.0,
        [StatId.ASPD] = 1.0,
        [StatId.DEF] = 1_000_000.0,
        [StatId.CRIT] = 0.05,
        [StatId.CDMG] = 0.5,
        [StatId.LIFESTEAL] = 0.0,
        [StatId.DODGE] = 0.02,
        [StatId.BLOCK] = 0.0,
        [StatId.PEN] = 0.0,
        [StatId.DMG_PCT] = 0.0,
        [StatId.DR_PCT] = 0.6,
        [StatId.HEAL_PCT] = 1.0,
        [StatId.THORNS] = 0.0,
    });

    /// <summary>`02` §4.3's boss-node power this bench fights at — moderate, not tuned to any par cell.</summary>
    internal const double BossPower = 10_000.0;

    /// <summary>`05` §6.0's shared enemy/hero level this bench fights at.</summary>
    internal const int Level = 50;

    /// <summary>
    /// The one held effect <see cref="Hero"/> needs — see the type remarks for why. A
    /// <c>STAT_COPY</c> of the hero's own (zero) <c>LIFESTEAL</c>, onto itself, every tick: a
    /// same-value no-op for combat purposes, present only so
    /// <c>CombatFlowState.PercentBuckets</c> is non-empty from the first tick.
    /// </summary>
    private static HeldEffect PercentBucketSeed() => new(new EffectDefinition
    {
        Id = "TEST_HERO_PERCENT_BUCKET_SEED",
        Op = EffectOp.STAT_COPY,
        Stat = StatSelector.Of(StatId.LIFESTEAL),
        Value = 0.0001,
        Target = EffectTarget.SELF,
        Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 0.05 },
        Duration = new EffectDuration { Scope = DurationScope.BATTLE },
    });

    /// <summary>
    /// Runs one authored boss to `05` §3's 1800-tick bound (or fewer, via <paramref name="maxTicks"/>)
    /// through the real `05` §4 attack pipeline and the real `05` §5 status engine — the same
    /// composition <see cref="Bosses.BossFight.Run"/> uses, plus the hero's one seeded held effect.
    /// </summary>
    /// <param name="bossId">A script id in <c>content/bosses/bosses.json</c>.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="battleSeed">`14` §8.1's battle seed.</param>
    /// <param name="maxTicks">`05` §3's bound, or fewer.</param>
    /// <param name="firstClear">`17` §1's first-clear flag.</param>
    internal static SimulationResult Run(
        string bossId,
        ContentSnapshot content,
        ulong battleSeed = BattleSeed,
        int maxTicks = CombatLog.MaxTicks,
        bool firstClear = false)
    {
        var caps = CombatCaps.Read(content);
        var enemies = EnemyCatalogue.Read(content);
        var bosses = BossCatalogue.Read(content);
        var statuses = StatusCatalogue.Read(content);
        var entry = bosses.Of(bossId);

        var encounter = BossEncounterBuilder.Build(new BossEncounterRequest
        {
            Script = entry.Script,
            Effects = entry.Effects,
            Power = BossPower,
            Level = Level,
            Index = CombatActor.FirstEnemy,
            LogId = CombatActor.Enemy(0),
            Baseline = BossFight.Baseline(bosses),
            Derivation = enemies.Derivation,
            FirstClear = firstClear,
        });

        var heroPlan = new ActorPlan
        {
            Id = "HERO",
            Index = 0,
            LogId = CombatActor.Hero,
            Side = BattleSide.HERO,
            Kind = EffectActorKind.HERO,
            BaseStats = Hero(),
            Level = Level,
            Effects = new[] { PercentBucketSeed() },
        };

        var addsPowerFraction = entry.Script.AddsPowerFraction;

        var plan = new BattlePlan
        {
            BattleSeed = battleSeed,
            Actors = new List<ActorPlan> { heroPlan, encounter.Plan },
            Caps = caps.Caps,
            Mitigation = caps.Mitigation,
            WardCapPct = caps.WardCapPct,
            RunCounters = new RunTriggerCounters(),
            Rules = CombatRules.PvE with { MaxTicks = maxTicks },
            Seams = services =>
            {
                var attack = new AttackPipeline(services);
                var timeline = new StatusTimeline(services, attack, statuses);

                return new BattleSeams(
                    Attack: attack,
                    Statuses: timeline,
                    Timeline: timeline,
                    Phases: new BossPhaseController(services, encounter),
                    Summons: addsPowerFraction is null
                        ? NoSummons.Instance
                        : new BossSummonSource(enemies, BossPower, addsPowerFraction.Value, Level),
                    Pets: NoPetAbilities.Instance,
                    Outcomes: new BossOutcomes(services));
            },
        };

        return CombatSimulator.Simulate(plan);
    }

    /// <summary>Every script id `17` §1.2 authors, in <c>content/bosses/bosses.json</c> order.</summary>
    internal static IEnumerable<object[]> AllBossIds(ContentSnapshot content) =>
        BossCatalogue.Read(content).Scripts.Select(s => new object[] { s.Script.Id });
}
