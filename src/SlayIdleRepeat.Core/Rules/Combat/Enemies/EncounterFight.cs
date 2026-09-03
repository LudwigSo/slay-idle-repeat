using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// One authored chapter/tier and a per-slot power line, drawn into a real non-boss roster and
/// composed into the fight <see cref="CombatSimulator.SimulateEncounter"/> exposes.
/// </summary>
/// <remarks>
/// <para>
/// This is the first production caller of <c>EnemyCatalogue</c>, <c>EnemyDerivation</c>,
/// <c>ChapterEnemyPool</c> and <c>EliteModifierDraw</c> together: before it, nothing in
/// <c>Core</c> ever set <see cref="ActorPlan.IsElite"/> to a computed value, so
/// <c>TARGET_IS_ELITE</c>/<c>ATTACKER_IS_ELITE</c> could only ever read <c>false</c>.
/// </para>
/// <para>
/// The modifier-to-effect table stays absent, deliberately: two of the eight modifiers have no
/// honest translation today (<c>CURSED</c>'s curse catalogue does not exist, and <c>VOLATILE</c>
/// needs a targeting ruling that hasn't landed), and a six-of-eight table would let those two
/// silently do nothing. What this type does apply is the other half — the elite power multiplier
/// and <see cref="ActorPlan.IsElite"/> set from a real computation.
/// </para>
/// <para>
/// The pre-battle draws (archetype, Elite identity, Elite modifier) and the in-battle draws share
/// one <see cref="DeterministicRng"/> stream, and its position after the last draw is threaded
/// through as <see cref="BattlePlan.RngPosition"/> so the fight's own stream resumes rather than
/// re-consuming the same indices.
/// </para>
/// <para>
/// No run, so no cross-encounter Elite exclusion: every call draws through
/// <c>EliteModifierHistory.Restore(null)</c> — "no Elite fought yet" — which a caller wiring a real
/// run is expected to replace with its own persisted history.
/// </para>
/// <para>
/// ⚠️ <b>That local is why the no-repeat rule has never fired, and it is still open.</b> A fresh
/// history per battle means <c>PreviousEliteModifier</c> is permanently <c>null</c>, so the redraw
/// this type calls into can never exclude anything — the rule is present and inert, which is worse
/// than absent because every test of the draw passes. <b>M4-02</b> owns closing it, together with
/// the run's first persisted luck state: one history instance per run, carried on the <c>Run</c>
/// aggregate and handed to every Elite encounter in it. The luck milestone's first task deliberately
/// did not, because it wires no run and the fix is a field rather than a type.
/// </para>
/// </remarks>
internal static class EncounterFight
{
    /// <summary>Runs one non-boss encounter and returns the replay.</summary>
    /// <param name="battleSeed">The battle seed. The run seed never enters this layer.</param>
    /// <param name="hero">The hero's stat block, before content effects.</param>
    /// <param name="heroLevel">The hero's Legend Level.</param>
    /// <param name="chapter">The chapter, <see cref="EnemyLevelTable.FirstChapter"/>..<see cref="EnemyLevelTable.LastChapter"/>.</param>
    /// <param name="tierOrdinal">The tier's ordinal in <see cref="EnemyLevelTable.Ordinals"/>.</param>
    /// <param name="enemyPowers">
    /// One enemy power per enemy slot, 1-5 bodies. For the Elite slot (see
    /// <paramref name="eliteIndex"/>) this is the pre-multiplier power —
    /// <see cref="EnemyDerivation.ElitePower"/> is applied here, not by the caller.
    /// </param>
    /// <param name="eliteIndex">
    /// The index into <paramref name="enemyPowers"/> elevated to an Elite, or <c>-1</c> for none.
    /// </param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="heroEffects">
    /// Extra effects the hero holds for this fight — gear, affixes, talents already resolved by the
    /// caller, each carrying the holding it came from. <c>null</c>/empty for none. A holding with no
    /// instance id is minted a battle-local one, so an <c>ON_KILL</c> effect must arrive with the id
    /// the run layer holds for it (see <see cref="BattlePlan.Validated"/>).
    /// </param>
    /// <param name="eliteIdentities">
    /// The identities the Elite slot may be, or <c>null</c> for the chapter's own elite pool. A
    /// single-entry list is how a predetermined elite is composed: the draw is a uniform pick over
    /// one item, so it costs the same one draw index the chapter pool costs and the combat stream
    /// stays byte-identical in shape to an ordinary elite's. An empty list is refused by the draw
    /// itself, before it spends an index — a weighted table with no row is a content error.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="enemyPowers"/> is empty, <paramref name="eliteIdentities"/> is empty, or the roster breaks a rule.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="eliteIndex"/> is out of range for <paramref name="enemyPowers"/>.</exception>
    /// <exception cref="KeyNotFoundException"><paramref name="chapter"/> has no authored pool/level row.</exception>
    /// <exception cref="MissingContentException">A document or pointer the fight needs is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A constant the fight needs is <c>null</c> in the data.</exception>
    internal static SimulationResult Run(
        ulong battleSeed,
        ActorStats hero,
        int heroLevel,
        int chapter,
        int tierOrdinal,
        IReadOnlyList<double> enemyPowers,
        int eliteIndex,
        ContentSnapshot content,
        IReadOnlyList<HeldEffect>? heroEffects,
        double? heroStartingHp = null,
        IReadOnlyList<string>? eliteIdentities = null)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(enemyPowers);
        ArgumentNullException.ThrowIfNull(content);

        if (enemyPowers.Count == 0)
        {
            throw new ArgumentException(
                "A fight with no enemies is already won and has no ticks to run. `05` §3's roster is a " +
                "hero, 0-3 pets and 1-5 enemies.",
                nameof(enemyPowers));
        }

        if (eliteIndex != -1 && (eliteIndex < 0 || eliteIndex >= enemyPowers.Count))
        {
            throw new ArgumentOutOfRangeException(
                nameof(eliteIndex), eliteIndex,
                "-1 (no Elite) or a valid index into enemyPowers. `05` §6.2's Elite is one slot of the " +
                "encounter, not a slot of its own.");
        }

        var caps = CombatCaps.Read(content);
        var statuses = StatusCatalogue.Read(content);
        var enemies = EnemyCatalogue.Read(content);
        var pool = enemies.Pool(chapter);
        var level = enemies.Levels.Of(chapter, tierOrdinal);

        var rng = BattleRngScope.Open(battleSeed);

        var actors = new List<ActorPlan>(enemyPowers.Count + 1)
        {
            new()
            {
                Id = "HERO",
                Identity = "HERO",
                Index = 0,
                LogId = CombatActor.Hero,
                Side = BattleSide.HERO,
                Kind = EffectActorKind.HERO,
                BaseStats = hero,
                Level = heroLevel,
                Effects = heroEffects ?? Array.Empty<HeldEffect>(),
                StartingHp = heroStartingHp,
            },
        };

        var eliteHistory = EliteModifierHistory.Restore(null);

        for (var i = 0; i < enemyPowers.Count; i++)
        {
            var isElite = i == eliteIndex;
            var power = enemyPowers[i];

            EnemyArchetype archetype;
            string identity;

            if (isElite)
            {
                var elitePool = eliteIdentities ?? pool.ElitePool;
                var identities = new List<(string item, double weight)>(elitePool.Count);
                foreach (var id in elitePool)
                {
                    identities.Add((id, EliteModifierDraw.UniformWeight));
                }

                var eliteId = rng.WeightedPick(identities);

                archetype = enemies.EliteIdentities.TryGetValue(eliteId, out var baseArchetype)
                    ? baseArchetype
                    : throw new KeyNotFoundException(
                        $"chapter {chapter}'s elite identities name '{eliteId}', which has no row " +
                        "under content/enemies/enemies.json#/elites/identities. `05` §6.2's chapter " +
                        "pool and identity table have to name the same ids.");
                identity = eliteId;

                power = EnemyDerivation.ElitePower(power, enemies.ElitePowerMultiplier);

                // Drawn for real, on the production stream; not yet translated into an effect (type remarks).
                EliteModifierDraw.Draw(rng, enemies.EliteModifiers, eliteHistory, enemies.NoRepeatWithPreviousEliteInRun);
            }
            else
            {
                archetype = pool.Draw(rng);
                identity = archetype.ToString();
            }

            var row = enemies.Archetype(archetype);
            var stats = EnemyDerivation.Derive(power, row, enemies.Derivation);

            actors.Add(new ActorPlan
            {
                Id = $"ENEMY_{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                Identity = identity,
                Index = CombatActor.FirstEnemy + i,
                LogId = CombatActor.Enemy(i),
                Side = BattleSide.ENEMY,
                Kind = EffectActorKind.ENEMY,
                BaseStats = stats,
                Level = level,
                IsElite = isElite,
                TargetPriority = enemies.DefaultTargetPriority,
            });
        }

        return CombatSimulator.Simulate(new BattlePlan
        {
            BattleSeed = battleSeed,
            RngPosition = rng.Position,
            Actors = actors,
            Caps = caps.Caps,
            Mitigation = caps.Mitigation,
            WardCapPct = caps.WardCapPct,
            RunCounters = new RunTriggerCounters(),
            Seams = services =>
            {
                var attack = new AttackPipeline(services);
                var timeline = new StatusTimeline(services, attack, statuses);

                // Phases stays NoBossPhases and Summons stays NoSummons: a non-boss encounter has
                // neither of its own. SimulateBossFight is the entry point for a roster that does.
                return BattleSeams.Strict with
                {
                    Attack = attack,
                    Statuses = timeline,
                    Timeline = timeline,
                };
            },
        });
    }
}
