using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// The shipped <c>game-data/content/bosses/bosses.json</c>, read once through the production
/// reader.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is a CACHE, not a reader.</b> It maps nothing, parses nothing and knows no key of the
/// document — it is one <see cref="Lazy{T}"/> around
/// <c>BossCatalogue.Read(GameDataLoader.Load())</c>. Its predecessor, M2-13's
/// <c>AuthoredBossScripts</c>, WAS a reader — a second <c>System.Text.Json</c> mapping of one file
/// into one set of types — and it was deleted in the same commit that landed
/// <see cref="BossCatalogue"/>, which is what
/// <c>SubjectSetFloorTests</c>' entry for that type existed to force.
/// </para>
/// <para>
/// ⚠️ Cached because the assertions over it run into the hundreds and each one would otherwise
/// re-read the whole <c>game-data</c> tree off disk. The catalogue is an immutable record over an
/// immutable <c>ContentSnapshot</c>, so sharing one instance across cases cannot let one case see
/// another's edit; every negative case builds its own snapshot through
/// <see cref="GameDataLoader.LoadWith"/> instead of touching this one.
/// </para>
/// </remarks>
internal static class ShippedBosses
{
    private static readonly Lazy<BossCatalogue> LazyCatalogue =
        new(() => BossCatalogue.Read(GameDataLoader.Load()));

    /// <summary>`17` §1.2's nine rows and `17` §2-9's mechanics, as shipped.</summary>
    internal static BossCatalogue Catalogue => LazyCatalogue.Value;
}
