using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The key each difficulty tier's authored name lives under. Chapter Select owns the strings; every
/// screen that spells a tier — the picker's buttons, Home's progress tile — reads them through here,
/// so two screens cannot spell the same tier off two tables.
/// </summary>
public static class TierNames
{
    private const string NormalKey = "loc.chapter_select.tier_normal.name";
    private const string HeroicKey = "loc.chapter_select.tier_heroic.name";
    private const string MythicKey = "loc.chapter_select.tier_mythic.name";

    /// <summary>The key <paramref name="tier"/>'s name is authored under, or <c>null</c> for a tier no key names.</summary>
    /// <param name="tier">The tier to name.</param>
    public static string? KeyOf(DifficultyTier tier) => tier switch
    {
        DifficultyTier.NORMAL => NormalKey,
        DifficultyTier.HEROIC => HeroicKey,
        DifficultyTier.MYTHIC => MythicKey,
        _ => null,
    };
}
