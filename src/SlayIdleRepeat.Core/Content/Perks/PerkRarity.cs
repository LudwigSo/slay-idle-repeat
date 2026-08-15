namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>
/// 🔒 `06` §4's <c>RarityWeights</c> bands — the four rarities every draft-pool weight table is
/// keyed on. Declared in ascending order deliberately: <c>Rules.Perks.DraftRarityWeights</c>'
/// elite-shift arithmetic reads "Common halved, Legendary doubled" off the two ends of this set.
/// </summary>
public enum PerkRarity
{
    Common,
    Rare,
    Epic,
    Legendary,
}
