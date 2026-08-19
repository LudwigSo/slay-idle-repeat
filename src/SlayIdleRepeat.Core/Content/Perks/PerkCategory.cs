namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>
/// The nine drafted perk categories: five elements and four generic ones. Cursed Perks are the
/// non-drafted category and carry no row here.
/// </summary>
/// <remarks>
/// <para>
/// The names mirror the schema's enum tokens exactly rather than shorter paraphrased spellings.
/// </para>
/// <para>
/// 🔒 <b>The five elements are five stacking rules, not five flavours.</b>
/// <see cref="Lightning"/> carries no ailment at all and is hit count; <see cref="Cold"/> degrades
/// what an enemy deals and can stop it acting; <see cref="Fire"/> burns on a capped stack that
/// expires; <see cref="Poison"/> stacks without a cap and never expires; <see cref="Bleed"/> stacks
/// to a cap and is the one ailment a perk can spend for an instant payout. A category whose
/// mechanics could be swapped with its neighbour's would not need to be its own member.
/// </para>
/// <para>
/// Single-valued, and a hybrid perk is filed under one of the categories it draws on: the draft's
/// diversity rule counts categories per draft, so a perk counted under two would satisfy a
/// diversity narrowing by itself. What a hybrid demands of several elements lives in its
/// <see cref="PerkCatalogueEntry.Requires"/> instead.
/// </para>
/// </remarks>
public enum PerkCategory
{
    /// <summary>Chains and repeat strikes. The one element with no ailment of its own.</summary>
    Lightning,

    /// <summary>Chill and freeze — degrading what an enemy deals, then stopping it acting.</summary>
    Cold,

    /// <summary>Burn: damage over time on a capped stack that expires.</summary>
    Fire,

    /// <summary>Poison: damage over time that stacks without a cap and never expires.</summary>
    Poison,

    /// <summary>Bleed: a capped, expiring stack that a perk can spend for an instant payout.</summary>
    Bleed,

    /// <summary>Damage reduction, dodge, health and shields.</summary>
    Defense,

    /// <summary>Raw damage, attack speed and extra swings.</summary>
    Offense,

    /// <summary>Crit chance, crit damage, and what a crit sets off.</summary>
    Crit,

    /// <summary>Healing, lifesteal and staying up.</summary>
    Sustain,
}
