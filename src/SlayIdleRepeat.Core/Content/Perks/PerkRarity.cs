namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>
/// The four rarities every draft-pool weight table is keyed on. Declared in ascending order
/// deliberately: the elite-shift arithmetic reads "Common halved, Legendary doubled" off the two
/// ends of this set.
/// </summary>
public enum PerkRarity
{
    Common,
    Rare,
    Epic,
    Legendary,
}
