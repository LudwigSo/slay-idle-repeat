namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The persisted shape of one rolled gear item — flat, serialisable fields.</summary>
/// <param name="InstanceId">The item's identity.</param>
/// <param name="DefId">Which of the twenty-four base items it is.</param>
/// <param name="Slot">The slot it is worn in.</param>
/// <param name="Family">Its family — the stat bias, and at <see cref="Primitives.Rarity.SS"/> the set.</param>
/// <param name="Rarity">Its rarity band.</param>
/// <param name="ChapterOrigin">The chapter its power is scaled against. Never the chapter it is read in.</param>
/// <param name="Quality">The one quality scalar, already rounded.</param>
/// <param name="EnhanceLevel">How far it has been enhanced.</param>
/// <param name="EnhanceFailures">Consecutive failed enhancement attempts on this item.</param>
/// <param name="Affixes">The affixes it rolled, in roll order. Never null; may be empty.</param>
/// <param name="Locked">Whether the player has locked it against destructive operations.</param>
/// <remarks>
/// <para>
/// A separate type from <c>Model.Gear.GearInstance</c> rather than the item itself, and that is
/// forced rather than chosen: the canonical state writer requires exactly one <em>public</em>
/// constructor, and a type under <c>Model/</c> may not have one. The item validates in its
/// constructor; this carries the fields across the persistence seam and validates nothing —
/// <c>Inventory.Rehydrate</c> is where a corrupt row becomes a loud failure.
/// </para>
/// <para>
/// It lives in <c>Primitives/</c> beside <see cref="GearAffixRoll"/>, <see cref="EnergyBanks"/> and
/// <see cref="PlayerId"/>, for the reason all three do: it is a <b>component</b> of a persisted
/// aggregate rather than a persisted aggregate, so it carries no <c>SchemaVersion</c> of its own —
/// the root snapshot carries the one version the whole tree is read at, and a second version on a
/// component would be a second answer to which layout a row was written in.
/// </para>
/// <para>
/// No derived stat is stored, and that is what the item's own remarks cost and buy: everything a
/// screen shows is recomputed from these fields, so a balance patch re-tunes items a player already
/// owns.
/// </para>
/// </remarks>
public sealed record GearInstanceSnapshot(
    GearInstanceId InstanceId,
    string DefId,
    GearSlot Slot,
    GearFamily Family,
    Rarity Rarity,
    int ChapterOrigin,
    double Quality,
    int EnhanceLevel,
    int EnhanceFailures,
    IReadOnlyList<GearAffixRoll> Affixes,
    bool Locked);
