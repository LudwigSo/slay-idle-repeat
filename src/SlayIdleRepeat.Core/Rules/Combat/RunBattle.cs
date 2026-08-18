using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Stats;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// The fight a run is standing in: the hero its loadout composes, the enemy its tile scales, and the
/// seed its combat stream committed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One composition behind two doors.</b> A screen holds persisted rows and the confirming
/// handler holds aggregates, and both have to reach the same fight or the server refuses honest
/// clients. So the row door rehydrates and delegates: there is exactly one place that decides which
/// enemy a tile is, one that decides the hero, and one that decides the seed.
/// </para>
/// <para>
/// 🔒 <b>The hero arrives as a base curve plus an effect list, never as a stat block.</b> The
/// simulator re-aggregates every pass, so handing it <c>HeroBuild.Stats</c> applies the whole
/// loadout twice. Nothing throws and the fight completes; the hero is simply, silently, far too
/// strong.
/// </para>
/// <para>
/// ⚠️ <b>The hero enters at full health.</b> A battle's roster carries no starting-HP slot, so a run's
/// current hit points do not reach the fight — a hero at 1 HP fights the same fight as one at full.
/// That is a property of the roster shape rather than of this composition, and it is reported rather
/// than papered over here.
/// </para>
/// <para>
/// ⚠️ <b>A <c>SWARM</c> draw still puts one body on the field, not three.</b> One power is supplied
/// per pool draw, which is what a normal enemy tile is; expanding a multi-unit archetype into its
/// bodies is the encounter composition's, and it does not do it yet.
/// </para>
/// </remarks>
public static class RunBattle
{
    /// <summary>The seed of the battle this run is standing in, from its persisted row.</summary>
    /// <param name="run">The run's row.</param>
    /// <returns>The battle seed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The run's combat stream names no battle.</exception>
    public static ulong SeedOf(RunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return SeedFrom(
            run.RunSeed,
            run.RngStreamPositions is not null &&
            run.RngStreamPositions.TryGetValue(RngStreams.Combat, out var started)
                ? started
                : 0UL);
    }

    /// <summary>The seed of the battle this run is standing in.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The battle seed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The run's combat stream names no battle.</exception>
    internal static ulong SeedOf(RunAggregate run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return SeedFrom(run.RunSeed, run.StreamPosition(RngStreams.Combat));
    }

    /// <summary>Composes and runs the fight this run is standing in, from the persisted rows.</summary>
    /// <param name="player">The player's row — where the loadout's items live.</param>
    /// <param name="run">The run's row.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <returns>The replay, with the <c>LogHash</c> the confirming command carries.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A row does not rehydrate.</exception>
    /// <exception cref="InvalidOperationException">The run is not standing in a battle it can fight.</exception>
    /// <exception cref="MissingContentException">A document or pointer the fight needs is absent.</exception>
    public static SimulationResult Simulate(
        PlayerSnapshot player, RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        return Simulate(
            RowDoor.Player(player, content, nameof(player)),
            RowDoor.Run(run, nameof(run)),
            content);
    }

    /// <summary>Composes and runs the fight this run is standing in.</summary>
    /// <param name="player">The player aggregate.</param>
    /// <param name="run">The run aggregate.</param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <returns>The replay, with the <c>LogHash</c> the confirming command carries.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The run is not standing in a battle it can fight.</exception>
    /// <exception cref="MissingContentException">A document or pointer the fight needs is absent.</exception>
    internal static SimulationResult Simulate(
        PlayerAggregate player, RunAggregate run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        var kind = OpenFightKind(run);
        var seed = SeedOf(run);
        var build = HeroBuild.Of(player, run, content);
        var tierOrdinal = TierOrdinal(run.Tier);

        var power = EnemyPowerFormula.Compute(
            ParPowerTuning.Read(content).ChapterPowerTarget(run.ChapterId),
            run.Tier,
            run.PendingTileLinearIndex,
            run.PendingTileStage);

        var holdings = Holdings(build);

        if (kind == TileKind.Boss)
        {
            return BossFight.Run(
                seed,
                build.BaseStats,
                player.LegendLevel,
                ChapterBoardTuning.Read(content, run.ChapterId).BossId,
                power,
                EnemyCatalogue.Read(content).Levels.Of(run.ChapterId, tierOrdinal),
                content,
                !player.HasClearedChapterTier(run.ChapterId, run.Tier),
                holdings);
        }

        // One power, because 05 §6.4's normal battle is one draw from the chapter's pool. The Elite
        // multiplier is the encounter's to apply, so what is handed over is the pre-multiplier figure.
        return EncounterFight.Run(
            seed,
            build.BaseStats,
            player.LegendLevel,
            run.ChapterId,
            tierOrdinal,
            new[] { power },
            kind == TileKind.Elite ? EliteSlot : NoElite,
            content,
            holdings);
    }

    /// <summary>
    /// The build's effects as the roster's holdings, each keeping the instance id it was collected
    /// under.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The id is not decoration.</b> A roster refuses an <c>ON_KILL</c> effect that arrives
    /// without one, because such a counter is run-scoped and a battle-local id resets it every fight.
    /// An authored set's four-piece bonus is exactly such an effect, so flattening the build down to
    /// bare definitions here — which is what going through the public simulator door would do — makes
    /// a hero in a full set unable to enter a fight at all.
    /// </remarks>
    private static IReadOnlyList<HeldEffect> Holdings(HeroBuild build)
    {
        var held = new HeldEffect[build.Collected.Count];

        for (var i = 0; i < held.Length; i++)
        {
            held[i] = new HeldEffect(build.Collected[i].Effect, build.Collected[i].Instance);
        }

        return held;
    }

    /// <summary>The index of the Elite in a one-body encounter.</summary>
    private const int EliteSlot = 0;

    /// <summary>The encounter entry point's "no Elite in this roster" sentinel.</summary>
    private const int NoElite = -1;

    /// <summary>
    /// The kind of fight this run has open, refusing anything that is not one — by phase, by tile,
    /// then by kind, each with its own sentence.
    /// </summary>
    private static TileKind OpenFightKind(RunAggregate run)
    {
        if (run.Phase != RunPhase.BattlePending)
        {
            throw new InvalidOperationException(
                "This run's phase is " + run.Phase + ", not " + RunPhase.BattlePending + ", so it is " +
                "standing in no battle. The combat counter of a run outside a battle names the LAST " +
                "fight, so composing one here would be a real fight for the wrong moment.");
        }

        if (!run.HasPendingTile)
        {
            throw new InvalidOperationException(
                "This run is in the battle phase and carries no pending tile, so nothing names the " +
                "enemy it is fighting. Opening a battle never clears the tile the fight is over.");
        }

        var kind = (TileKind)run.PendingTileKindValue;

        if (kind is not (TileKind.Enemy or TileKind.Elite or TileKind.Boss))
        {
            throw new InvalidOperationException(
                "This run's pending tile is " + kind + ", which is not a fight. Only Enemy, Elite and " +
                "Boss tiles open a battle, so a run standing in one over any other kind is a state no " +
                "command can produce.");
        }

        return kind;
    }

    /// <summary>
    /// The battle seed, from the run seed and the counter of battles started.
    /// </summary>
    /// <remarks>
    /// 🔒 The one derivation, shared by both doors. The counter counts battles STARTED, so the one
    /// now open is the index before it — an off-by-one here is not a crash but a real seed for the
    /// wrong fight, which the server would recompute differently and refuse.
    /// </remarks>
    private static ulong SeedFrom(ulong runSeed, ulong battlesStarted)
    {
        if (battlesStarted == 0UL)
        {
            throw new InvalidOperationException(
                "This run's '" + RngStreams.Combat + "' stream has never been drawn from, so no " +
                "battle of it has been opened and there is no battle index to derive a seed from. " +
                "Opening a battle is what advances that counter.");
        }

        return SeedDerivation.BattleSeed(runSeed, (int)(battlesStarted - 1UL));
    }

    /// <summary>The tier's ordinal in the enemy level table's authored order.</summary>
    /// <remarks>
    /// Matched by NAME against the authored order rather than computed off the enum's numbering: the
    /// two agree today and nothing holds them together, so an arithmetic conversion would silently
    /// start reading the wrong row the day either moves.
    /// </remarks>
    private static int TierOrdinal(DifficultyTier tier)
    {
        var name = tier.ToString();

        for (var ordinal = 0; ordinal < EnemyLevelTable.Ordinals.Count; ordinal++)
        {
            if (EnemyLevelTable.Ordinals[ordinal].Equals(name, StringComparison.Ordinal))
            {
                return ordinal;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(tier),
            tier,
            "The enemy level table authors a row per tier under the names " +
            string.Join(", ", EnemyLevelTable.Ordinals) + ", and '" + name + "' is not one of them. " +
            "A tier with no authored row has no enemy level, and inventing one here would invent the " +
            "difficulty of every fight in it. (" +
            ((int)tier).ToString(CultureInfo.InvariantCulture) + ")");
    }
}
