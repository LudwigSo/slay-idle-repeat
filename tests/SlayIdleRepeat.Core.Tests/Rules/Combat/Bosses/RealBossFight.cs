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
/// One authored boss fight through the real engine — the real <see cref="AttackPipeline"/> and
/// <see cref="StatusTimeline"/>, the composition production and the balance harness use, not
/// <see cref="BossTestBench"/>'s fakes.
/// </summary>
/// <remarks>
/// Neither existing bench serves: <see cref="Bosses.BossFight.Run"/> takes only an
/// <see cref="ActorStats"/> hero and has no way to give it a held effect, while
/// <see cref="BossTestBench"/> composes a pipeline that deals no damage and statuses that apply
/// nothing — right for the phase-machinery suite, but unable to prove a real op resolves against
/// the real engine.
/// <para>
/// The one hero-held effect this bench seeds: <c>BOSS_COGITATOR_PRIME_P2_RECALIBRATE</c> is a
/// <c>STAT_COPY HIGHEST_PCT_BONUS</c> whose source is the hero, and
/// <c>HighestPercentBonusStat</c> throws by design when the source carries no percent bucket —
/// which only <c>STAT_COPY</c> writes. A hero built through today's production entry points never
/// carries one, so Recalibrate would still fault for a reason this bench must not paper over by
/// weakening that refusal. Instead the hero gets a single harmless <c>STAT_COPY</c> of
/// <c>LIFESTEAL</c> (base <c>0.0</c>, so a real write of a real zero with no effect on balance) —
/// the percent bucket a real build will one day populate.
/// </para>
/// </remarks>
internal static class RealBossFight
{
    /// <summary>An arbitrary but fixed battle seed, used across this bench's fights.</summary>
    internal const ulong BattleSeed = 0xB055_C0DE_0000_0003UL;

    /// <summary>
    /// A hero stat block strong enough to survive a full fight against any of the nine authored
    /// bosses (at <see cref="BossPower"/>) and to bring one down through phase 3 well inside the
    /// 90 s bound — calibrated empirically against the shipped <c>content/bosses/bosses.json</c>.
    /// Not a balanced build: DEF and Max HP are deliberately extreme so the fight's outcome is
    /// never in doubt and every tick is about the boss's own mechanics, not a coin-flip loss.
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

    /// <summary>The boss-node power this bench fights at — moderate, not tuned to any par cell.</summary>
    internal const double BossPower = 10_000.0;

    /// <summary>The shared enemy/hero level this bench fights at.</summary>
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
    /// Runs one authored boss to the 1800-tick bound (or fewer, via <paramref name="maxTicks"/>)
    /// through the real attack pipeline and the real status engine — the same composition
    /// <see cref="Bosses.BossFight.Run"/> uses, plus the hero's one seeded held effect.
    /// </summary>
    /// <param name="bossId">A script id in <c>content/bosses/bosses.json</c>.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="battleSeed">The battle seed.</param>
    /// <param name="maxTicks">The tick bound, or fewer.</param>
    /// <param name="firstClear">The first-clear flag.</param>
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

    /// <summary>Every authored script id, in <c>content/bosses/bosses.json</c> order.</summary>
    internal static IEnumerable<object[]> AllBossIds(ContentSnapshot content) =>
        BossCatalogue.Read(content).Scripts.Select(s => new object[] { s.Script.Id });
}
