using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>What one chest pick resolved to.</summary>
/// <param name="Tier">The outcome tier index drawn, zero-based.</param>
/// <param name="FromPity">
/// Whether the guarantee forced it. Reported rather than inferred from the tier: a natural top-tier
/// pick and a forced one are the same outcome and different events.
/// </param>
/// <param name="Counter">The one counter this pick moved — advanced on a miss, reset on the top tier.</param>
internal readonly record struct ChestPickResolution(
    int Tier, bool FromPity, PityCounterChange Counter);

/// <summary>
/// The three-chest pick's gold-tier guarantee: a pure 1-in-3 draw with the top tier forced every
/// N-th consecutive miss.
/// </summary>
/// <remarks>
/// <para>
/// The other three minigames are skill-scaled and carry no counter at all, which is why this is a
/// chest-pick resolver rather than a minigame one. Nothing here is keyed on which tile the pick
/// happened at: the counter is player-scoped and lifetime, and the run's per-tile resolution map is a
/// legality gate over a different question.
/// </para>
/// <para>
/// One draw index per resolution, forced or not — the forced path skips the draw's <em>result</em>,
/// never the draw, so a resumed stream lands in the same place either way.
/// </para>
/// </remarks>
internal static class ChestPickGuarantee
{
    /// <summary>Resolves one chest pick and answers the counter it moved.</summary>
    /// <param name="rule">The authored chest-pick block.</param>
    /// <param name="counterKey">The counter id, as the tuning reader forms it. Never hand-composed.</param>
    /// <param name="counters">The player's counters as they stand before this pick.</param>
    /// <param name="draws">The already-opened draw stream, continued. Exactly one index is consumed.</param>
    /// <param name="tierCount">How many outcome tiers this minigame authors.</param>
    /// <returns>The tier, whether pity forced it, and the counter change to apply.</returns>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tierCount"/> is below 1.</exception>
    internal static ChestPickResolution Resolve(
        ChestPickRule rule,
        string counterKey,
        PityCounters counters,
        DeterministicRng draws,
        int tierCount)
    {
        ArgumentNullException.ThrowIfNull(counterKey);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(draws);

        var topTier = TopTier(tierCount);
        var missesBeforePick = counters.Get(counterKey);
        var forced = GuaranteeFires(rule, missesBeforePick);

        // Drawn on every branch, forced or not: the guarantee overrides the draw's RESULT, never the
        // draw, so a resumed stream lands in the same place whichever way the pick went.
        var drawn = draws.Range(0, tierCount);
        var tier = forced ? topTier : drawn;

        return new ChestPickResolution(
            tier,
            forced,
            new PityCounterChange(
                counterKey,
                tier == topTier ? HardPity.Reset() : HardPity.Advance(missesBeforePick)));
    }

    /// <summary>Whether the pick about to be made is the forced one.</summary>
    /// <remarks>
    /// Split out so a client can show "one more chest" without a second statement of the rule, on the
    /// same argument the ladder path's own read-only question is split out.
    /// </remarks>
    /// <param name="rule">The authored chest-pick block.</param>
    /// <param name="missesBeforeDraw">The counter's value before this pick. Never negative.</param>
    /// <returns><see langword="true"/> when the next pick is forced onto the top tier.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="missesBeforeDraw"/> is negative.</exception>
    internal static bool GuaranteeFires(ChestPickRule rule, int missesBeforeDraw) =>
        HardPity.Fires(missesBeforeDraw, rule.GuaranteeAfterConsecutiveMisses);

    /// <summary>The top outcome tier of a table of <paramref name="tierCount"/> rows.</summary>
    /// <remarks>
    /// The tiers ascend in the document, so the last row is the gold-tier chest. Derived rather than
    /// authored a second time: <c>goldTierChests</c> says how many of the three chests are gold, not
    /// which index the reward table puts it at.
    /// </remarks>
    /// <param name="tierCount">How many outcome tiers the minigame authors.</param>
    /// <returns>The top tier's index.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tierCount"/> is below 1.</exception>
    internal static int TopTier(int tierCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tierCount, 1);

        return tierCount - 1;
    }
}
