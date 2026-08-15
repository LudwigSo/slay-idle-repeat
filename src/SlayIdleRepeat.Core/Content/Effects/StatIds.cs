namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The <see cref="StatId"/> vocabulary read as a set: all 26, and the 14 combat stats.</summary>
public static class StatIds
{
    /// <summary>Every stat this DSL declares, in declaration order.</summary>
    public static IReadOnlyList<StatId> All { get; } = Enum.GetValues<StatId>();

    /// <summary>The 14 combat stats — and therefore exactly what <c>ALL_COMBAT</c> selects.</summary>
    public static IReadOnlyList<StatId> Combat { get; } =
        Enum.GetValues<StatId>().Where(IsCombat).ToArray();

    /// <summary>The 12 non-combat stats.</summary>
    public static IReadOnlyList<StatId> NonCombat { get; } =
        Enum.GetValues<StatId>().Where(s => !IsCombat(s)).ToArray();

    /// <summary>True for one of the 14 combat stats.</summary>
    /// <remarks>
    /// Every stat named explicitly rather than <c>stat &lt;= StatId.THORNS</c>: the wire values are
    /// append-only and a future non-combat stat could not be given a number below THORNS, but a
    /// comparison would still be a rule that depends on numbering rather than on membership, and it
    /// would go quietly wrong.
    /// <c>StatSelectorTests.Every_stat_is_classified_combat_or_non_combat</c> enumerates the enum,
    /// so a 27th stat with no arm here is a red test rather than a throw in a battle.
    /// </remarks>
    public static bool IsCombat(StatId stat) => stat switch
    {
        StatId.MAX_HP or
        StatId.ATK or
        StatId.DEF or
        StatId.ASPD or
        StatId.CRIT or
        StatId.CDMG or
        StatId.LIFESTEAL or
        StatId.DODGE or
        StatId.BLOCK or
        StatId.PEN or
        StatId.DMG_PCT or
        StatId.DR_PCT or
        StatId.HEAL_PCT or
        StatId.THORNS => true,

        StatId.GOLD_PCT or
        StatId.CROWNS_PCT or
        StatId.DROP_CHANCE or
        StatId.RARITY_SHIFT or
        StatId.ENERGY_REGEN_PCT or
        StatId.PET_AURA_PCT or
        StatId.REROLL_CHARGES or
        StatId.TILE_PREVIEW or
        StatId.SHOP_PRICE_PCT or
        StatId.XP_PCT or
        StatId.BEAST_FEED_PCT or
        StatId.STONE_PCT => false,

        _ => throw new ArgumentOutOfRangeException(
            nameof(stat), stat,
            "18 §2.1 declares 26 stats and this value is none of them."),
    };
}
