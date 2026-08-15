namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>
/// 🔒 `06` §2 — the six standard drafted perk categories. Cursed Perks (`06` §3.7) are a seventh,
/// non-drafted category and carry no row here — <c>game-data/schema/perk.schema.json</c>'s own
/// <c>category</c> enum excludes them for the same reason.
/// </summary>
/// <remarks>
/// The names mirror <c>perk.schema.json</c>'s enum tokens exactly (<c>DICE_AND_BOARD</c>,
/// <c>TRIGGER_SYNERGY</c>) rather than the shorter spellings a milestone handover paraphrased them
/// as — steering S9/S21: the authored content is what exists, not the paraphrase.
/// </remarks>
public enum PerkCategory
{
    Offense,
    Defense,
    Sustain,
    DiceAndBoard,
    Economy,
    TriggerSynergy,
}
