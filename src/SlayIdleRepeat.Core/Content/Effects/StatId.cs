namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The 26 stats a stat op may name: 14 combat stats and 12 non-combat stats.</summary>
/// <remarks>
/// <para>
/// The 14 combat stats are exactly the full combat stat block: every actor, hero and enemy alike,
/// carries a complete 14-stat block. This enum is isomorphic to that block, which is why
/// <c>ALL_COMBAT</c> is not a member — see <see cref="StatSelector"/>.
/// </para>
/// <para>
/// Wire values, as <see cref="EffectOp"/>: append, never renumber, never reuse; no <c>0</c> member.
/// The 14 combat stats occupy 1..14 and the 12 non-combat stats 15..26, so
/// <see cref="StatIds.IsCombat"/> is a fact about the enum rather than a second list to keep in
/// step — but it is still written as an exhaustive switch, because a renumbering would otherwise
/// silently reclassify a stat.
/// </para>
/// </remarks>
public enum StatId
{
    // ------------------------------------------------------- combat (14)

    /// <summary>Maximum hit points.</summary>
    MAX_HP = 1,

    /// <summary>Attack.</summary>
    ATK = 2,

    /// <summary>Defence.</summary>
    DEF = 3,

    /// <summary>Attack speed, in attacks per second.</summary>
    ASPD = 4,

    /// <summary>Critical-hit chance, 0..1.</summary>
    CRIT = 5,

    /// <summary>Critical damage bonus.</summary>
    CDMG = 6,

    /// <summary>Lifesteal fraction.</summary>
    LIFESTEAL = 7,

    /// <summary>Dodge chance, 0..1.</summary>
    DODGE = 8,

    /// <summary>Block chance, 0..1.</summary>
    BLOCK = 9,

    /// <summary>Armour penetration, 0..1.</summary>
    PEN = 10,

    /// <summary>Outgoing damage percent.</summary>
    DMG_PCT = 11,

    /// <summary>Damage reduction percent.</summary>
    DR_PCT = 12,

    /// <summary>Multiplier on all healing received; base 1.0.</summary>
    HEAL_PCT = 13,

    /// <summary>Thorns — damage returned to an attacker.</summary>
    THORNS = 14,

    // ------------------------------------------------------- non-combat (12)

    /// <summary>Gold gain percent.</summary>
    GOLD_PCT = 15,

    /// <summary>Crowns gain percent.</summary>
    CROWNS_PCT = 16,

    /// <summary>Drop chance.</summary>
    DROP_CHANCE = 17,

    /// <summary>Rarity shift on drops.</summary>
    RARITY_SHIFT = 18,

    /// <summary>Energy regeneration percent.</summary>
    ENERGY_REGEN_PCT = 19,

    /// <summary>Pet aura strength percent.</summary>
    PET_AURA_PCT = 20,

    // 21 was REROLL_CHARGES. Nothing can grant a reroll charge any more, so no effect, affix or
    // set bonus may name the stat. The number stays retired rather than reused: it is a wire value.

    // 22 was TILE_PREVIEW, the tile preview range. The board is completely visible at all times (`04` §4, `16` D42), so nothing can narrow a preview and nothing can widen one. The number stays retired
    // rather than reused: it is a wire value.

    /// <summary>Shop price percent.</summary>
    SHOP_PRICE_PCT = 23,

    /// <summary>Legend XP gain percent.</summary>
    XP_PCT = 24,

    /// <summary>Beast Feed gain percent.</summary>
    BEAST_FEED_PCT = 25,

    /// <summary>Enhance Stone gain percent.</summary>
    STONE_PCT = 26,
}
