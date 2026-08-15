namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>
/// The six standard drafted perk categories. Cursed Perks are a seventh, non-drafted category and
/// carry no row here.
/// </summary>
/// <remarks>
/// The names mirror the schema's enum tokens exactly (<c>DICE_AND_BOARD</c>,
/// <c>TRIGGER_SYNERGY</c>) rather than shorter paraphrased spellings.
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
