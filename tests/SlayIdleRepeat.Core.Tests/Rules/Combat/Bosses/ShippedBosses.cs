using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// The shipped <c>game-data/content/bosses/bosses.json</c>, read once through the production reader.
/// </summary>
/// <remarks>
/// 🔒 A cache, not a reader: one <see cref="Lazy{T}"/> around <c>BossCatalogue.Read</c>, mapping and
/// parsing nothing. Its predecessor <em>was</em> a second <c>System.Text.Json</c> mapping of the same
/// file, and was deleted when <see cref="BossCatalogue"/> landed. ⚠️ Cached because the assertions run
/// into the hundreds; the catalogue is immutable, so sharing cannot let one case see another's edit.
/// </remarks>
internal static class ShippedBosses
{
    private static readonly Lazy<SlayIdleRepeat.Core.Content.ContentSnapshot> LazyContent =
        new(GameDataLoader.Load);

    private static readonly Lazy<BossCatalogue> LazyCatalogue =
        new(() => BossCatalogue.Read(LazyContent.Value));

    /// <summary>
    /// 🔒 M2-R3 — the raw <c>game-data</c> snapshot the catalogue above was read from, cached
    /// alongside it for the real-engine boss-fight bench (<see cref="RealBossFight"/>), which needs
    /// more of the snapshot than just <c>content/bosses/bosses.json</c> (also
    /// <c>content/combat_caps.json</c>, <c>content/statuses.json</c> and the enemy catalogue).
    /// </summary>
    internal static SlayIdleRepeat.Core.Content.ContentSnapshot Content => LazyContent.Value;

    /// <summary>`17` §1.2's nine rows and `17` §2-9's mechanics, as shipped.</summary>
    internal static BossCatalogue Catalogue => LazyCatalogue.Value;
}
