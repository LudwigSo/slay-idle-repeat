using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// Everything drawing and pricing a shop offer needs to know about the run standing at it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A reading of the run rather than the run, so the SCREEN and the PURCHASE can share one
/// derivation.</b> The handler holds a live <c>Run</c>; the shop screen holds a <c>RunSnapshot</c>
/// and cannot construct one. Given a parameter typed as the aggregate, the screen would have had to
/// re-derive the offer its own way — and a shop that shows one thing and charges for another is the
/// one failure a second copy of derived data reliably produces.
/// </para>
/// <para>
/// It carries the perk tiers and both modifier lists rather than resolving them itself, because
/// what a run owns is the run's business and what a slot costs is this layer's.
/// </para>
/// </remarks>
/// <param name="RunSeed">The committed run seed the shop stream is rooted in.</param>
/// <param name="ChapterId">The chapter, for the price scalar and the run-buff magnitudes.</param>
/// <param name="StageIndex">`03` §7's 0/1/2, already clamped into the pricing formula's range.</param>
/// <param name="OwnedPerkTiers">
/// The perk tiers the run owns, so slot 1 never offers a perk already at its top tier.
/// </param>
/// <param name="ShrineBuffs">The shrine buffs taken, read for their shop-price contribution (none today).</param>
/// <param name="Curses">The curses carried, read for <c>CUR_MISERLY</c>'s shop-price move.</param>
internal sealed record RunShopContext(
    ulong RunSeed,
    int ChapterId,
    int StageIndex,
    DraftedPerks OwnedPerkTiers,
    IReadOnlyList<string> ShrineBuffs,
    IReadOnlyList<string> Curses)
{
    /// <summary>The reading of a live run — the form a handler builds.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    internal static RunShopContext Of(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new RunShopContext(
            run.RunSeed,
            run.ChapterId,
            RunShopOffer.StageIndexOf(run.HasPendingTile ? run.PendingTileStage : 1),
            run.DraftedPerks,
            run.ShrineBuffs,
            run.Curses);
    }

    /// <summary>The reading of a persisted row — the form a projection builds.</summary>
    /// <remarks>
    /// The absent-collection defaults are the snapshot's own: every one of these members is
    /// nullable on <see cref="RunSnapshot"/> because it defaults on a row written before the field
    /// existed, and a projection that threw on one would refuse to draw a screen for a run the game
    /// can still play.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    internal static RunShopContext Of(RunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new RunShopContext(
            run.RunSeed,
            run.ChapterId,
            RunShopOffer.StageIndexOf(run.PendingTileStage),
            new DraftedPerks(run.OwnedPerkTiers ?? EmptyTiers),
            run.ShrineBuffs ?? Array.Empty<string>(),
            run.Curses ?? Array.Empty<string>());
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyTiers =
        new Dictionary<string, int>(0, StringComparer.Ordinal);
}
