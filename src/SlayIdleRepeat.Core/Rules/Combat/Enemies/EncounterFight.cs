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
/// 🔒 `05` §6-§6.2 — one authored chapter/tier and a per-slot power line, drawn into a real
/// non-boss roster and composed into the fight <see cref="CombatSimulator.SimulateEncounter"/>
/// exposes.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHY THIS EXISTS — M2-R4</b> ═══
/// </para>
/// <para>
/// Before this type, <c>EnemyCatalogue</c>, <c>EnemyDerivation</c>, <c>ChapterEnemyPool</c> and
/// <c>EliteModifierDraw</c> were each authored, tested and reachable only from a bench: nothing in
/// <c>Core</c> ever called <c>EliteModifierDraw.Draw</c> outside a test, and no production roster
/// ever set <see cref="ActorPlan.IsElite"/> to a computed value, so <c>18</c> §4's
/// <c>TARGET_IS_ELITE</c>/<c>ATTACKER_IS_ELITE</c> could only ever read <c>false</c>. This is the
/// first production caller — <c>05</c> §6.2's "the first consumer owes exactly one table" note
/// (M2-11) is why this type draws the modifier rather than inventing whether to.
/// </para>
/// <para>
/// 🔒 <b>The `05` §6.2 modifier → `18` §1 effect table stays absent, deliberately (S6).</b> Two of
/// the eight modifiers have no honest translation today — <c>CURSED</c> names a curse id that is
/// <c>null</c> because <c>content/curses/</c> is empty, and <c>VOLATILE</c> needs a hero-naming
/// target token in a no-attack-context trigger that no ruling has settled. A six-of-eight table
/// would retire the deferral this type's own draw re-opens for real, while two modifiers silently
/// did nothing — worse than the modifier staying undocumented as applied. What this type DOES
/// apply is `05` §6.2's other half: <c>Elite = base archetype × 2.2 power</c>
/// (<see cref="EnemyDerivation.ElitePower"/>) and <see cref="ActorPlan.IsElite"/> set from a real
/// computation, which is what makes an Elite fight measurably different from an ordinary one and
/// what makes <c>TARGET_IS_ELITE</c> reachable at all.
/// </para>
/// <para>
/// 🔒 <b>The pre-battle draws and the in-battle draws share one stream, at one position.</b> Which
/// archetype each slot is, which of the chapter's two Elite identities appears, and which of the
/// eight modifiers it draws are all `05` §6/§6.2 facts that have to be decided <em>before</em> a
/// <see cref="BattlePlan"/> can be built — there is no roster to hand the simulator otherwise. They
/// are drawn on one <see cref="DeterministicRng"/> opened over <c>(battleSeed, RngStreams.Combat)</c>,
/// and its position after the last draw is threaded through as <see cref="BattlePlan.RngPosition"/>
/// so the fight's own stream resumes rather than re-consuming the same indices. See that member's
/// remarks for why the collision is real without it.
/// </para>
/// <para>
/// 🔒 <b>No run, so no cross-encounter Elite exclusion — an explicit, greppable narrowing.</b>
/// `05` §6.2's no-repeat rule needs the run's memory of the previous Elite
/// (<c>IEliteModifierHistory</c>), and a single public encounter call carries no run. Every call
/// therefore draws through <c>EliteModifierHistory.Restore(null)</c> — "no Elite fought yet" — which
/// is honest for a standalone encounter and is exactly what a caller wiring a real run (M3) is
/// expected to replace with its own persisted history. Recorded here rather than silently
/// approximated (S6).
/// </para>
/// </remarks>
internal static class EncounterFight
{
    /// <summary>Runs one non-boss encounter and returns `05` §7's replay.</summary>
    /// <param name="battleSeed">`14` §8.1's battle seed. The <c>runSeed</c> never enters this layer.</param>
    /// <param name="hero">The hero's `05` §1 block, before `18` §8.</param>
    /// <param name="heroLevel">The hero's Legend Level.</param>
    /// <param name="chapter">The chapter, `05` §6.0's <see cref="EnemyLevelTable.FirstChapter"/>..<see cref="EnemyLevelTable.LastChapter"/>.</param>
    /// <param name="tierOrdinal">The tier's ordinal in <see cref="EnemyLevelTable.Ordinals"/>.</param>
    /// <param name="enemyPowers">
    /// One `02` §4.3 <c>EnemyPower(i)</c> per enemy slot, `05` §3's 1-5 bodies. For the Elite slot
    /// (see <paramref name="eliteIndex"/>) this is the <em>pre-multiplier</em> power —
    /// <see cref="EnemyDerivation.ElitePower"/> is applied here, not by the caller.
    /// </param>
    /// <param name="eliteIndex">
    /// The index into <paramref name="enemyPowers"/> that `05` §6.2 elevates to an Elite, or
    /// <c>-1</c> for an encounter with none.
    /// </param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="heroEffects">
    /// Extra `18` §1 effects the hero holds for this fight — gear, affixes, talents already
    /// resolved by the caller. <c>null</c>/empty for none. Each is minted a battle-local instance id
    /// (<see cref="HeldEffect"/>), so an <c>ON_KILL</c> effect must not be passed here (see
    /// <see cref="BattlePlan.Validated"/>).
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="enemyPowers"/> is empty, or the roster breaks a `05` §3 rule.</exception>
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
        IReadOnlyList<EffectDefinition>? heroEffects)
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

        // 🔒 One stream, opened here — see the type remarks for why its END position has to travel
        // with the plan rather than the battle re-opening its own at 0.
        var rng = BattleRngScope.Open(battleSeed);

        var actors = new List<ActorPlan>(enemyPowers.Count + 1)
        {
            new()
            {
                Id = "HERO",
                Index = 0,
                LogId = CombatActor.Hero,
                Side = BattleSide.HERO,
                Kind = EffectActorKind.HERO,
                BaseStats = hero,
                Level = heroLevel,
                Effects = ToHeld(heroEffects),
            },
        };

        // 🔒 No run, so no cross-encounter exclusion — see the type remarks.
        var eliteHistory = EliteModifierHistory.Restore(null);

        for (var i = 0; i < enemyPowers.Count; i++)
        {
            var isElite = i == eliteIndex;
            var power = enemyPowers[i];

            EnemyArchetype archetype;

            if (isElite)
            {
                var identities = new List<(string item, double weight)>(pool.ElitePool.Count);
                foreach (var id in pool.ElitePool)
                {
                    identities.Add((id, EliteModifierDraw.UniformWeight));
                }

                var eliteId = rng.WeightedPick(identities);

                archetype = enemies.EliteIdentities.TryGetValue(eliteId, out var identity)
                    ? identity
                    : throw new KeyNotFoundException(
                        $"content/enemies/enemies.json's chapter {chapter} elitePool names " +
                        $"'{eliteId}', which has no row under elites/identities. `05` §6.2's chapter " +
                        "pool and identity table have to name the same ids.");

                power = EnemyDerivation.ElitePower(power, enemies.ElitePowerMultiplier);

                // 🔒 Drawn for real, on the production stream, against the real content pool and the
                // real (empty-for-this-call) history — `05` §6.2's redraw rule included. The drawn
                // value is not yet translated into an `18` §1 effect: see the type remarks.
                EliteModifierDraw.Draw(rng, enemies.EliteModifiers, eliteHistory, enemies.NoRepeatWithPreviousEliteInRun);
            }
            else
            {
                archetype = pool.Draw(rng);
            }

            var row = enemies.Archetype(archetype);
            var stats = EnemyDerivation.Derive(power, row, enemies.Derivation);

            actors.Add(new ActorPlan
            {
                Id = $"ENEMY_{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
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

                // Phases stays NoBossPhases and Summons stays NoSummons: `05` §6 gives a non-boss
                // encounter no phases and no SUMMON of its own. SimulateBossFight is the entry point
                // for a roster that carries either.
                return BattleSeams.Strict with
                {
                    Attack = attack,
                    Statuses = timeline,
                    Timeline = timeline,
                };
            },
        });
    }

    /// <summary>Wraps caller-supplied effects as battle-local holdings — <c>null</c> id, minted by the simulator.</summary>
    private static IReadOnlyList<HeldEffect> ToHeld(IReadOnlyList<EffectDefinition>? effects)
    {
        if (effects is null || effects.Count == 0)
        {
            return Array.Empty<HeldEffect>();
        }

        var held = new List<HeldEffect>(effects.Count);
        foreach (var effect in effects)
        {
            held.Add(new HeldEffect(effect));
        }

        return held;
    }
}
