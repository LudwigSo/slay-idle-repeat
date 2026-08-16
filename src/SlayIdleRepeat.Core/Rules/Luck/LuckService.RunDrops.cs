using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>
/// The façade's in-run-drop half: the one grant class whose protection is a dry-streak breaker rather
/// than a rarity ladder.
/// </summary>
/// <remarks>
/// <para>
/// <b>A partial file rather than a second type, deliberately.</b> The routing rule asks whether a
/// producer names <c>LuckService</c>, so a drop path that reached a differently-named resolver would
/// be a second façade with the same authority and none of the rule's protection. Splitting the file
/// rather than the type keeps one façade while leaving the two lanes that write into this namespace a
/// file each.
/// </para>
/// <para>
/// <b>Why <c>Resolve</c> cannot serve this class, and does not pretend to.</b> That path is stated
/// over the hard-rungs-plus-soft-curve shape five classes author; this class authors three unrelated
/// rules — two dry-streak breakers over different kill kinds and a per-day session floor that no draw
/// produces — so asking the registry for a ladder it does not have is refused by name rather than
/// answered with a synthesised one. The guarantee decision still never leaves this namespace: the
/// breakers below call the same hard-pity predicate every other guarantee in the game fires through.
/// </para>
/// </remarks>
internal static partial class LuckService
{
    /// <summary>
    /// Resolves the rarity of one in-run gear drop: applies the trigger's dry-streak breaker, draws
    /// once against the chapter's authored shares, and answers the counter the draw moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly one draw, forced or not — the breaker is expressed as a floor on the same table, so a
    /// resumed stream lands in the same place either way. A refusal happens before the draw, so it
    /// consumes nothing.
    /// </para>
    /// <para>
    /// ⚠️ <b>The miss threshold and the forced band are read separately.</b> A drop counts as a miss
    /// when it lands strictly below the breaker's <c>belowRarity</c>, while the forced draw is floored
    /// at its <c>forceRarityAtLeast</c>; the shipped data makes those the same band, and treating them
    /// as one would silently freeze that coincidence into the engine.
    /// </para>
    /// </remarks>
    /// <param name="tuning">The pity registry — the one place a counter id is formed.</param>
    /// <param name="dropRun">The in-run drop protections.</param>
    /// <param name="drops">The gear tables, for the chapter's authored drop shares.</param>
    /// <param name="chapter">The chapter the drop is scaled against, from 1.</param>
    /// <param name="trigger">What produced the drop, and therefore which breaker protects it.</param>
    /// <param name="counters">The player's counters as they stand before this drop.</param>
    /// <param name="draws">The already-opened draw stream, continued. Exactly one index is consumed.</param>
    /// <returns>The rarity, whether a breaker forced it, and the counter changes to apply.</returns>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="trigger"/> is not declared, or the chapter is below 1.</exception>
    /// <exception cref="InvalidOperationException">The chapter's table carries no weight once floored.</exception>
    internal static LuckResolution ResolveRunDrop(
        LuckTuning tuning,
        DropRunTuning dropRun,
        DropsTuning drops,
        int chapter,
        RunDropTrigger trigger,
        PityCounters counters,
        DeterministicRng draws)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(dropRun);
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentOutOfRangeException.ThrowIfLessThan(chapter, 1);

        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(
                nameof(trigger), trigger, "That is not a source of an in-run gear drop.");
        }

        var breaker = BreakerFor(dropRun, trigger);
        var table = ChapterTable(drops, chapter);

        string? key = null;
        var forced = false;

        if (breaker is { } streak)
        {
            key = tuning.CounterKey(SourceClass.DROP_RUN, streak.ForceRarityAtLeast);
            forced = HardPity.Fires(counters.Get(key), streak.ForceOnNthKill);

            if (forced)
            {
                table = table.FloorAt(streak.ForceRarityAtLeast);
            }
        }

        if (!table.HasPositiveWeight)
        {
            throw new InvalidOperationException(
                $"The chapter {chapter} in-run drop table carries no weight once its floor is applied, " +
                "so there is nothing to draw. Refused before the draw is taken, so the stream is left " +
                "where it stood.");
        }

        var outcome = draws.WeightedPick(Walk(table));

        return new LuckResolution(outcome, forced, Moved(counters, breaker, key, outcome));
    }

    /// <summary>
    /// How many floor items a completed run owes the player: the session floor, applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A count rather than a draw. The floor is not protection on a roll — it is the run-end payout
    /// noticing that the run produced nothing worth keeping and adding one item at the authored band,
    /// so there is no stream to consume and no counter to ramp.
    /// </para>
    /// <para>
    /// It answers zero for a run that does not qualify, for a run that already produced an item at or
    /// above the floor's band, and for a player who has already taken the day's allowance — three
    /// different reasons for the same answer, all of them the caller's to explain to the player.
    /// </para>
    /// </remarks>
    /// <param name="dropRun">The in-run drop protections.</param>
    /// <param name="qualified">
    /// Whether the run ended the way the floor requires. The caller decides what that means; this
    /// reads whether the requirement applies at all.
    /// </param>
    /// <param name="itemsAtOrAboveFloor">
    /// How many items at or above the floor's band the run already produced. Never negative.
    /// </param>
    /// <param name="grantsAlreadyToday">
    /// How many floor grants the player has already taken in this game day. Never negative.
    /// </param>
    /// <returns>How many items the floor grants now. Zero or more, never above the authored count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dropRun"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
    internal static int SessionFloorGrant(
        DropRunTuning dropRun, bool qualified, int itemsAtOrAboveFloor, int grantsAlreadyToday)
    {
        ArgumentNullException.ThrowIfNull(dropRun);
        ArgumentOutOfRangeException.ThrowIfNegative(itemsAtOrAboveFloor);
        ArgumentOutOfRangeException.ThrowIfNegative(grantsAlreadyToday);

        var floor = dropRun.SessionFloor;

        if (floor.RequiresVictoryOrStage3Death && !qualified)
        {
            return 0;
        }

        if (itemsAtOrAboveFloor > 0 || grantsAlreadyToday >= floor.MaxPerDay)
        {
            return 0;
        }

        return floor.GrantCount;
    }

    /// <summary>The dry-streak breaker a trigger is protected by, or none for an ordinary kill.</summary>
    private static DryStreakBreaker? BreakerFor(DropRunTuning dropRun, RunDropTrigger trigger) =>
        trigger switch
        {
            RunDropTrigger.ELITE => dropRun.EliteMercy,
            RunDropTrigger.BOSS => dropRun.BossMercy,
            _ => null,
        };

    /// <summary>The chapter's authored drop shares, as a weighted table.</summary>
    private static RarityTable ChapterTable(DropsTuning drops, int chapter)
    {
        var shares = drops.SharesFor(chapter);
        var rows = new RarityWeight[shares.Count];

        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = new RarityWeight(shares[i].Rarity, shares[i].Share);
        }

        return RarityTable.Of(rows);
    }

    /// <summary>
    /// The counter the drop moved: reset where the drop was not a miss, advanced where it was.
    /// </summary>
    /// <remarks>
    /// An ordinary kill moves nothing, and that is the rule rather than an omission — the breakers
    /// count consecutive <em>Elite</em> and <em>boss</em> kills, so a stream of ordinary drops between
    /// two Elite kills leaves the elite streak exactly where it was.
    /// </remarks>
    private static IReadOnlyList<PityCounterChange> Moved(
        PityCounters counters, DryStreakBreaker? breaker, string? key, Rarity outcome)
    {
        if (breaker is not { } streak || key is null)
        {
            return Array.Empty<PityCounterChange>();
        }

        var missed = outcome < streak.BelowRarity;

        return new[]
        {
            new PityCounterChange(
                key, missed ? HardPity.Advance(counters.Get(key)) : HardPity.Reset()),
        };
    }
}
