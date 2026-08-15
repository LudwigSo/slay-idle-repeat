using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// The shipped <c>game-data/content/bosses/bosses.json</c>, read once through the production reader.
/// </summary>
/// <remarks>
/// A cache, not a reader: one <see cref="Lazy{T}"/> around <c>BossCatalogue.Read</c>, mapping and
/// parsing nothing. Cached because the assertions run into the hundreds; the catalogue is immutable,
/// so sharing cannot let one case see another's edit.
/// </remarks>
internal static class ShippedBosses
{
    private static readonly Lazy<SlayIdleRepeat.Core.Content.ContentSnapshot> LazyContent =
        new(GameDataLoader.Load);

    private static readonly Lazy<BossCatalogue> LazyCatalogue =
        new(() => BossCatalogue.Read(LazyContent.Value));

    /// <summary>
    /// The raw snapshot the catalogue above was read from, cached alongside it for the real-engine
    /// boss-fight bench (<see cref="RealBossFight"/>), which needs more of the snapshot than just
    /// the boss content.
    /// </summary>
    internal static SlayIdleRepeat.Core.Content.ContentSnapshot Content => LazyContent.Value;

    internal static BossCatalogue Catalogue => LazyCatalogue.Value;
}
