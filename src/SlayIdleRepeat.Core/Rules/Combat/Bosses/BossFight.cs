using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// One authored boss script, three catalogues and a hero stat block, composed into a runnable fight.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BattleSeams.For"/> — what a <see cref="BattlePlan"/> gets by default — wires only the
/// attack pipeline: its <c>Statuses</c> is <c>UnwiredStatusEngine</c>, which throws the moment
/// content applies one, and its <c>Phases</c> is <c>NoBossPhases</c>, which throws on a roster
/// carrying a boss. Every other composition in the repository is a test bench; this composes a real
/// <see cref="StatusTimeline"/> and <see cref="BossPhaseController"/> from the content snapshot it
/// holds, without widening <see cref="BattleSeams.For"/>'s default (which would move every test bench).
/// </para>
/// <para>
/// Power arrives, and is never derived here: it already carries the boss stage multiplier and must
/// not be multiplied by it again.
/// </para>
/// </remarks>
internal static class BossFight
{
    /// <summary>
    /// The row a boss's coefficients are hung on: an identity row whose four power coefficients
    /// <see cref="BossEncounterBuilder.StatlineRow"/> replaces, carrying the boss document's own
    /// secondary stats rather than any archetype's.
    /// </summary>
    /// <remarks>
    /// Not <c>GRUNT</c>'s row: a boss's DODGE is 0 while GRUNT's is 0.02, so reusing the archetype
    /// would give every boss a 2% dodge nothing authorised. <see cref="EnemyArchetype.GRUNT"/>
    /// appears only as the row's identity field, which the derivation never reads.
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

    /// <summary>Runs one boss fight to its tick bound and returns the replay.</summary>
    /// <param name="battleSeed">The battle seed. The run seed never enters this layer.</param>
    /// <param name="hero">The hero's stat block, before content effects.</param>
    /// <param name="heroLevel">The hero's Legend Level.</param>
    /// <param name="bossId">An id in <c>content/bosses/bosses.json</c>, e.g. <c>BOSS_THORNMAW</c>.</param>
    /// <param name="bossPower">The enemy power, with <c>StageMult.Boss</c> already inside it.</param>
    /// <param name="enemyLevel">The enemy level — shared by the boss and its adds.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="firstClear">The first-clear flag: phase 1 lasts 20% longer.</param>
    /// <param name="heroEffects">
    /// Extra effects the hero holds for this fight — gear, affixes, talents already resolved by the
    /// caller, each carrying the holding it came from. <c>null</c>/empty for none. A holding with no
    /// instance id is minted a battle-local one, so an <c>ON_KILL</c> effect must arrive with the id
    /// the run layer holds for it (see <see cref="BattlePlan.Validated"/>).
    /// <para>
    /// 🔴 This parameter did not exist, and its absence was not visible from here: the hero's plan
    /// simply carried no effects, so the same player fought every ordinary enemy with their loadout
    /// and every boss without it. Nothing threw; the boss was a different fight from the one the
    /// player's build described.
    /// </para>
    /// </param>
    internal static SimulationResult Run(
        ulong battleSeed,
        ActorStats hero,
        int heroLevel,
        string bossId,
        double bossPower,
        int enemyLevel,
        ContentSnapshot content,
        bool firstClear,
        IReadOnlyList<HeldEffect>? heroEffects,
        double? heroStartingHp = null)
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
            Effects = heroEffects ?? Array.Empty<HeldEffect>(),
            StartingHp = heroStartingHp,
        };

        // Only a boss that authors a SUMMON carries the fraction; a boss that does not keeps
        // NoSummons, which throws, so a SUMMON reaching an unwired roster half stays loud.
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
