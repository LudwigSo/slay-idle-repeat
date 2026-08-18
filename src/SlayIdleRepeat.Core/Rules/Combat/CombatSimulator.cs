using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>The fixed-tick combat engine: <c>Simulate(seed, heroSnapshot, enemySnapshot)</c> returns an identical result every time, on any device, on the server.</summary>
/// <remarks>
/// <para>
/// This and <c>PowerCalculator</c> are the only two <c>Rules</c> types that are public, each with a
/// named external consumer: the client's local battle simulation and the Hero screen's power readout.
/// <see cref="SimulationResult"/>, <see cref="CombatEvent"/> and <see cref="CombatEventType"/> are
/// public too, since the client replays the log and the backend recomputes <c>LogHash</c> over it.
/// </para>
/// <para>
/// A public entry point's parameter and return types are public by consequence — a public
/// <c>Simulate</c> returning an internal <c>SimulationResult</c> does not compile. The public surface
/// is enumerated explicitly (<c>Domain.PublicRuleTypes</c>) rather than left to "anything reachable
/// from a public type", so widening it takes a diff rather than happening implicitly.
/// </para>
/// <para>
/// The public overload below is the plain three-argument signature and nothing wider. Everything a
/// fight can carry beyond a stat block and a level — authored effects, boss phases, target priority,
/// duel bounds, the balance harness's seams — goes through the internal
/// <see cref="Simulate(BattlePlan)"/>.
/// </para>
/// </remarks>
public static class CombatSimulator
{
    /// <summary>Simulates one fight and returns its replay.</summary>
    /// <param name="battleSeed">The battle seed. The run seed it is derived from never enters this layer.</param>
    /// <param name="hero">The hero's stat block, before effect aggregation.</param>
    /// <param name="heroLevel">The hero's Legend Level — the attacker-level term of the mitigation curve.</param>
    /// <param name="enemies">One stat block per enemy, in roster index order. One to five.</param>
    /// <param name="enemyLevel">
    /// All enemies, Elites, Guardians and bosses in a (chapter, tier) share this level, which is why
    /// one value covers the whole side.
    /// </param>
    /// <param name="content">The loaded, schema-validated content snapshot. The simulator's tunable constants live in content and reach it through this. See the remarks.</param>
    /// <exception cref="ArgumentException">The roster is empty or breaks a <c>BattlePlan</c> rule.</exception>
    /// <exception cref="MissingContentException"><c>combat_caps.json</c> is absent or missing a pointer.</exception>
    /// <exception cref="UnauthorisedTunableException">A constant is <c>null</c> in the data.</exception>
    /// <remarks>
    /// <para>
    /// A fight with no authored effects and no boss: every actor swings its basic attack on the fixed
    /// schedule until one side is cleared or the timeout decides it on remaining HP fraction. This is
    /// the balance harness's standard dummy.
    /// </para>
    /// <para>
    /// <b>Why the snapshot, rather than the constants themselves.</b> This method is <c>static</c>, so
    /// it cannot hold the document, but it can be handed one. Writing the numbers here would be a third
    /// copy of tunables the content build already mirrors; defaulting them would silently mitigate 100%
    /// of every hit or delete every shield in the game; and taking them as bare doubles puts two
    /// same-typed parameters in a public signature where transposing them compiles and yields a
    /// plausible but wrong game.
    /// </para>
    /// <para>
    /// <b>Why this composes its own seams instead of taking <c>BattleSeams.For</c>.</b> That default
    /// wires only the attack pipeline, leaving the status engine unwired — which would make this public
    /// entry point unusable against ordinary content, since biome-driven enemies carry statuses. The
    /// wiring is done here rather than widened into <c>BattleSeams.For</c>, because this method already
    /// holds the content snapshot the status catalogue needs and that seam factory does not.
    /// </para>
    /// <para>Consequence for the caller: the snapshot must carry <c>content/statuses.json</c> as well as <c>content/combat_caps.json</c>, and a missing document fails loudly rather than mid-fight.</para>
    /// </remarks>
    public static SimulationResult Simulate(
        ulong battleSeed,
        ActorStats hero,
        int heroLevel,
        IReadOnlyList<ActorStats> enemies,
        int enemyLevel,
        ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(enemies);
        ArgumentNullException.ThrowIfNull(content);

        if (enemies.Count == 0)
        {
            throw new ArgumentException(
                "A fight with no enemies is already won and has no ticks to run. `05` §3's roster is a " +
                "hero, 0-3 pets and 1-5 enemies.",
                nameof(enemies));
        }

        var actors = new List<ActorPlan>(enemies.Count + 1)
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
            },
        };

        for (var i = 0; i < enemies.Count; i++)
        {
            actors.Add(new ActorPlan
            {
                Id = $"ENEMY_{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                Index = CombatActor.FirstEnemy + i,
                LogId = CombatActor.Enemy(i),
                Side = BattleSide.ENEMY,
                Kind = EffectActorKind.ENEMY,
                BaseStats = enemies[i] ??
                    throw new ArgumentException(
                        $"Enemy {i.ToString(System.Globalization.CultureInfo.InvariantCulture)} has no " +
                        "stat block. `05` §2: 'an unstated stat is a bug, not a zero.'",
                        nameof(enemies)),
                Level = enemyLevel,
            });
        }

        // One read of content/combat_caps.json, through the one type that knows its pointers.
        var caps = CombatCaps.Read(content);

        var statuses = StatusCatalogue.Read(content);

        return Simulate(new BattlePlan
        {
            BattleSeed = battleSeed,
            Actors = actors,
            Caps = caps.Caps,
            Mitigation = caps.Mitigation,
            WardCapPct = caps.WardCapPct,
            RunCounters = new RunTriggerCounters(),
            Seams = services =>
            {
                var attack = new AttackPipeline(services);
                var timeline = new StatusTimeline(services, attack, statuses);

                // Phases stays NoBossPhases and Summons stays NoSummons deliberately: this overload's
                // roster is stat blocks, so it carries no boss and authors no SUMMON, and both
                // defaults are refusals at the point content asks. SimulateBossFight is the entry
                // point for a roster that has either.
                return BattleSeams.Strict with
                {
                    Attack = attack,
                    Statuses = timeline,
                    Timeline = timeline,
                };
            },
        });
    }

    /// <summary>One authored boss fight, which is what the balance harness's guardrails are measured over.</summary>
    /// <param name="battleSeed">The battle seed. One fight per seed.</param>
    /// <param name="hero">The hero's stat block, before effect aggregation.</param>
    /// <param name="heroLevel">The hero's Legend Level.</param>
    /// <param name="bossId">A script id in <c>content/bosses/bosses.json</c>. An unknown id throws.</param>
    /// <param name="bossPower">
    /// The boss node's enemy power, with the boss stage multiplier already inside it — do not multiply
    /// by it again. Taken as a parameter so it cannot be applied twice.
    /// </param>
    /// <param name="enemyLevel">The shared enemy level for the (chapter, tier), adds included.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="firstClear">The first time a player fights a boss, phase 1 lasts 20% longer.</param>
    /// <param name="heroEffects">
    /// Extra effects the hero holds for this fight — e.g. an already-resolved gear affix.
    /// <c>null</c>/empty for none. A boss fought without the player's loadout is a silently different
    /// fight from the one their build describes, which is what this parameter's absence used to make
    /// unavoidable.
    /// </param>
    /// <exception cref="KeyNotFoundException"><paramref name="bossId"/> is not in the document.</exception>
    /// <exception cref="MissingContentException">A document or pointer the fight needs is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A constant the fight needs is <c>null</c> in the data.</exception>
    /// <remarks>
    /// <para>
    /// A second public method rather than a second public type: the balance harness is pinned to
    /// <c>SlayIdleRepeat.Core</c> with no package references, so it cannot reach the internal boss
    /// catalogue, encounter builder or plan types directly — an entry point of this shape is what makes
    /// running a boss fight from the harness possible at all, while keeping the DSL itself internal.
    /// </para>
    /// <para>The composition itself lives in <see cref="BossFight"/>.</para>
    /// </remarks>
    public static SimulationResult SimulateBossFight(
        ulong battleSeed,
        ActorStats hero,
        int heroLevel,
        string bossId,
        double bossPower,
        int enemyLevel,
        ContentSnapshot content,
        bool firstClear = false,
        IReadOnlyList<EffectDefinition>? heroEffects = null) =>
        BossFight.Run(
            battleSeed, hero, heroLevel, bossId, bossPower, enemyLevel, content, firstClear,
            BattleLocalHoldings(heroEffects));

    /// <summary>A real, content-driven non-boss encounter, with an optional Elite.</summary>
    /// <param name="battleSeed">The battle seed. The run seed never enters this layer.</param>
    /// <param name="hero">The hero's stat block, before effect aggregation.</param>
    /// <param name="heroLevel">The hero's Legend Level.</param>
    /// <param name="chapter">The chapter.</param>
    /// <param name="tierOrdinal">The tier's ordinal — Normal/Heroic/Mythic order, 0-based.</param>
    /// <param name="enemyPowers">
    /// One enemy power value per enemy slot. For <paramref name="eliteIndex"/>'s slot this is the
    /// pre-multiplier power; the Elite multiplier is applied internally.
    /// </param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="eliteIndex">The index into <paramref name="enemyPowers"/> to elevate to an Elite, or the default <c>-1</c> for an encounter with none.</param>
    /// <param name="heroEffects">Extra effects the hero holds for this fight — e.g. an already-resolved gear affix. <c>null</c>/empty for none.</param>
    /// <exception cref="ArgumentException"><paramref name="enemyPowers"/> is empty, or the roster breaks a roster rule.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="eliteIndex"/> is out of range for <paramref name="enemyPowers"/>.</exception>
    /// <exception cref="KeyNotFoundException"><paramref name="chapter"/> has no authored pool/level row.</exception>
    /// <exception cref="MissingContentException">A document or pointer the fight needs is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A constant the fight needs is <c>null</c> in the data.</exception>
    /// <remarks>
    /// <para>
    /// Before this method, the enemy derivation and Elite treatment were reachable only from inside
    /// <c>Core</c>. A caller cannot name an archetype, author a modifier, or reach a seam through this
    /// signature — it names a chapter, a tier and a power line the content already carries.
    /// </para>
    /// <para>
    /// <see cref="EffectDefinition"/> in a public signature is what makes an attached effect reachable
    /// at all — a caller can hand this method an <c>APPLY_STATUS</c> effect and have it actually
    /// resolve mid-fight, without reaching any other internal effects type.
    /// </para>
    /// <para>
    /// The Elite-modifier → effect table is deliberately still absent — see <see cref="EncounterFight"/>'s
    /// remarks. What this method does make real is the Elite's derived stats and
    /// <see cref="ActorPlan.IsElite"/>.
    /// </para>
    /// </remarks>
    public static SimulationResult SimulateEncounter(
        ulong battleSeed,
        ActorStats hero,
        int heroLevel,
        int chapter,
        int tierOrdinal,
        IReadOnlyList<double> enemyPowers,
        ContentSnapshot content,
        int eliteIndex = -1,
        IReadOnlyList<EffectDefinition>? heroEffects = null) =>
        EncounterFight.Run(
            battleSeed, hero, heroLevel, chapter, tierOrdinal, enemyPowers, eliteIndex, content,
            BattleLocalHoldings(heroEffects));

    /// <summary>A Ghost Duel: two hero builds, one fight.</summary>
    /// <param name="battleSeed">The battle seed. The run seed never enters this layer.</param>
    /// <param name="attacker">The attacking player's stat block, before effect aggregation.</param>
    /// <param name="attackerLevel">The attacker's Legend Level.</param>
    /// <param name="defender">The Ghost's stat block — the defending player's recorded build.</param>
    /// <param name="defenderLevel">The Ghost's Legend Level.</param>
    /// <param name="durationSeconds">The duel cap in seconds, as the caller read it from content.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="attackerIsUnderdog">True names the attacker the lower-rated player, who wins an exact tie at the timeout; false names the defender.</param>
    /// <param name="attackerEffects">Extra effects the attacker holds for this duel. <c>null</c>/empty for none.</param>
    /// <param name="defenderEffects">Extra effects the Ghost holds for this duel. <c>null</c>/empty for none.</param>
    /// <exception cref="MissingContentException">A document or pointer the fight needs is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A constant the fight needs is <c>null</c> in the data.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="durationSeconds"/> is outside the addressable tick range.</exception>
    /// <remarks>
    /// <para><c>BattleSide</c> stays internal: <paramref name="attackerIsUnderdog"/> is the primitive that replaces it in this signature, translated to the lower-rated side by <see cref="DuelFight"/>.</para>
    /// <para>No pets — see <see cref="DuelFight"/>'s remarks.</para>
    /// </remarks>
    public static SimulationResult SimulateDuel(
        ulong battleSeed,
        ActorStats attacker,
        int attackerLevel,
        ActorStats defender,
        int defenderLevel,
        double durationSeconds,
        ContentSnapshot content,
        bool attackerIsUnderdog = false,
        IReadOnlyList<EffectDefinition>? attackerEffects = null,
        IReadOnlyList<EffectDefinition>? defenderEffects = null) =>
        DuelFight.Run(
            battleSeed, attacker, attackerLevel, defender, defenderLevel, durationSeconds,
            attackerIsUnderdog, content, attackerEffects, defenderEffects);

    /// <summary>
    /// Wraps caller-supplied effect definitions as battle-local holdings — no instance id, minted by
    /// the simulator.
    /// </summary>
    /// <remarks>
    /// 🔒 Battle-local is the only honest answer at this door. A caller reaching a fight through the
    /// public surface has no run, so it holds no run-stable id for an effect; the roster's refusal of
    /// an <c>ON_KILL</c> effect with no id is therefore correct here rather than an inconvenience.
    /// A caller that <em>does</em> have a run — <see cref="RunBattle"/> — composes its own holdings
    /// from the build and never comes through here.
    /// </remarks>
    private static IReadOnlyList<HeldEffect>? BattleLocalHoldings(
        IReadOnlyList<EffectDefinition>? effects)
    {
        if (effects is null || effects.Count == 0)
        {
            return null;
        }

        var held = new HeldEffect[effects.Count];

        for (var i = 0; i < held.Length; i++)
        {
            held[i] = new HeldEffect(effects[i]);
        }

        return held;
    }

    /// <summary>The full entry point — the one a boss fight, a Ghost Duel or the balance harness uses.</summary>
    /// <param name="plan">The roster, the seed, the fight's bounds and the engine seams.</param>
    /// <remarks>
    /// Internal, since <see cref="BattlePlan"/> reaches effect definitions, stat caps and the seam
    /// interfaces, and making those public would turn the enumerated public surface into most of <c>Core</c>.
    /// </remarks>
    internal static SimulationResult Simulate(BattlePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new BattleSimulation(plan).Run();
    }
}
