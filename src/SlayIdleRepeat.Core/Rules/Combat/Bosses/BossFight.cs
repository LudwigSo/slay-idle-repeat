using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 One authored boss script, three catalogues and a hero stat block, composed into the fight
/// `05` §9's balance harness measures.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>THIS IS THE FIRST PLACE THE WHOLE ENGINE IS ASSEMBLED</b> ═══
/// </para>
/// <para>
/// M2-08 through M2-15 each landed one seam of `05` §3.1 and tested it against fakes for the others,
/// and <see cref="BattleSeams.For"/> — what a <see cref="BattlePlan"/> gets by default — wires only
/// `05` §4's attack pipeline: its <c>Statuses</c> is <c>UnwiredStatusEngine</c>, which <b>throws</b>
/// the moment content applies one, and its <c>Phases</c> is <c>NoBossPhases</c>, which throws on a
/// roster carrying a boss. Every other composition in the repository is a test bench.
/// </para>
/// <para>
/// 🔒 <b>That consequence was reported by M2-16a and is now closed.</b> The entry it named — the
/// public
/// <see cref="CombatSimulator.Simulate(ulong, ActorStats, int, IReadOnlyList{ActorStats}, int, ContentSnapshot)"/>
/// overload — could not run a fight in which any `05` §5 status is applied, because a CASTER's biome
/// status (`05` §6.1a) reached <c>UnwiredStatusEngine.Apply</c> and threw. The fix is the one this
/// type already demonstrates and is <b>not</b> a widening of <see cref="BattleSeams.For"/>: that
/// factory takes a <see cref="BattleServices"/> and no content snapshot, so it cannot read `05` §5's
/// catalogue, and changing <see cref="BattlePlan"/>'s default would move every test bench in the
/// repository. The overload composes its own <see cref="StatusTimeline"/> from the snapshot it
/// already holds, exactly as this type does. <see cref="BattleSeams.For"/> keeps its meaning: the
/// default for a plan built without content.
/// </para>
/// <para>
/// 🔒 <b>Power arrives, and is never derived here.</b> `02` §4.3's <c>EnemyPower(i)</c> already
/// carries <c>StageMult.Boss = 2.20</c>; `05` §6.3 and `17` §1 both say in as many words <em>"do not
/// multiply by 2.20 again"</em>. Every boss type in this namespace takes power as a parameter for
/// exactly that reason, and this one keeps the convention.
/// </para>
/// </remarks>
internal static class BossFight
{
    /// <summary>
    /// The `05` §6.1 row a boss's coefficients are hung on: an identity row whose four power
    /// coefficients <see cref="BossEncounterBuilder.StatlineRow"/> replaces, carrying `17` §1.2's
    /// <em>secondary stats</em> — <em>"every boss uses the baseline"</em> — rather than any
    /// archetype's.
    /// </summary>
    /// <remarks>
    /// ⚠️ It is <b>not</b> <c>GRUNT</c>'s row: `17` §1.2 puts a boss's DODGE at <c>0</c> and `05`
    /// §6.1 puts a GRUNT's at <c>0.02</c>, so reusing the archetype would have given all nine bosses
    /// a 2% dodge nothing authorised. The four secondaries come from the boss document itself
    /// (<c>content/bosses/bosses.json#/secondaryStats</c>), which is where M2-13 transcribed them.
    /// <see cref="EnemyArchetype.GRUNT"/> appears only as the row's identity field, which the
    /// derivation never reads.
    /// </remarks>
    internal static ArchetypeRow Baseline(BossCatalogue bosses)
    {
        ArgumentNullException.ThrowIfNull(bosses);

        return new ArchetypeRow(
            EnemyArchetype.GRUNT,
            HpCoef: 1.0,
            AtkCoef: 1.0,
            DefCoef: 1.0,
            AspdCoef: 1.0,
            bosses.Crit,
            bosses.CritDamage,
            bosses.Dodge,
            bosses.Lifesteal,
            UnitsPerDraw: 1,
            ArchetypeOnHit.None);
    }

    /// <summary>Runs one boss fight to its `05` §3 bound and returns `05` §7's replay.</summary>
    /// <param name="battleSeed">`14` §8.1's battle seed. The <c>runSeed</c> never enters this layer.</param>
    /// <param name="hero">The hero's `05` §1 block, before `18` §8.</param>
    /// <param name="heroLevel">The hero's Legend Level — `05` §4's <c>20 × attacker.Level</c> term.</param>
    /// <param name="bossId">An id in <c>content/bosses/bosses.json</c>, e.g. <c>BOSS_THORNMAW</c>.</param>
    /// <param name="bossPower">`02` §4.3's <c>EnemyPower(i)</c>, <c>StageMult.Boss</c> already inside it.</param>
    /// <param name="enemyLevel">`05` §6.0's <c>EnemyLevel(c,t)</c> — shared by the boss and its adds.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="firstClear">`17` §1's first-clear flag: phase 1 lasts 20% longer.</param>
    internal static SimulationResult Run(
        ulong battleSeed,
        ActorStats hero,
        int heroLevel,
        string bossId,
        double bossPower,
        int enemyLevel,
        ContentSnapshot content,
        bool firstClear)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(bossId);
        ArgumentNullException.ThrowIfNull(content);

        var caps = CombatCaps.Read(content);
        var enemies = EnemyCatalogue.Read(content);
        var bosses = BossCatalogue.Read(content);
        var statuses = StatusCatalogue.Read(content);
        var entry = bosses.Of(bossId);

        var encounter = BossEncounterBuilder.Build(new BossEncounterRequest
        {
            Script = entry.Script,
            Effects = entry.Effects,
            Power = bossPower,
            Level = enemyLevel,
            Index = CombatActor.FirstEnemy,
            LogId = CombatActor.Enemy(0),
            Baseline = Baseline(bosses),
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
            BaseStats = hero,
            Level = heroLevel,
        };

        // 17 §1's adds band. Only a boss that authors a SUMMON carries the fraction, and a boss that
        // does not must keep NoSummons — which THROWS — so that a SUMMON reaching an unwired roster
        // half stays loud rather than spawning nothing.
        var addsPowerFraction = entry.Script.AddsPowerFraction;

        var plan = new BattlePlan
        {
            BattleSeed = battleSeed,
            Actors = new List<ActorPlan> { heroPlan, encounter.Plan },
            Caps = caps.Caps,
            Mitigation = caps.Mitigation,
            WardCapPct = caps.WardCapPct,
            RunCounters = new RunTriggerCounters(),
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
                        : new BossSummonSource(enemies, bossPower, addsPowerFraction.Value, enemyLevel),
                    Pets: NoPetAbilities.Instance,
                    Outcomes: new BossOutcomes(services));
            },
        };

        return CombatSimulator.Simulate(plan);
    }
}
