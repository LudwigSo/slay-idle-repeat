using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Gear;

/// <summary>
/// Which stats are measured in whole units and which are shares — the one fact a screen needs in
/// order to write a gear affix's roll as a number or as a percentage.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>An exhaustive switch over <see cref="StatId"/>, so a stat added to the vocabulary has to be
/// classified here before it compiles.</b> The classification is the enum's own documentation read
/// back: hit points, attack, defence, thorns and a rarity shift are amounts; everything else is a
/// chance, a fraction or a multiplier consumed bare. A default arm that guessed "share" for a new stat
/// would draw a future flat stat as a percentage of nothing.
/// </para>
/// <para>
/// ⚠️ <b>Attack speed is a share here, and that is the drop table's ruling, not this file's.</b>
/// <c>tuning/drops.json#/percentStatsByRarity</c> lists ASPD beside CRIT, DODGE, PEN and LIFESTEAL as
/// the stats that <em>"scale with rarity only … because they feed capped percentages"</em>, and
/// <c>GearStatDerivation</c> already writes a boots' primary as a percentage on that authority. Two
/// unit rules for one stat on one sheet was the alternative.
/// </para>
/// <para>
/// ⚠️ This is about how an affix's ROLL is written, not about which bucket it lands in. An affix
/// that adds <c>STAT_ADD_PCT</c> onto attack speed is a share too, whatever its stat is measured in
/// — <see cref="IsShare(StatId, EffectOp)"/> answers that combined question, which is the one the
/// inventory projection asks.
/// </para>
/// </remarks>
internal static class GearStatKinds
{
    /// <summary>Whether a stat is a share (a chance, a fraction, a bare multiplier) rather than an amount.</summary>
    /// <param name="stat">The stat.</param>
    /// <exception cref="ArgumentOutOfRangeException">A stat the vocabulary does not declare.</exception>
    internal static bool IsShare(StatId stat) => stat switch
    {
        StatId.MAX_HP => false,
        StatId.ATK => false,
        StatId.DEF => false,
        StatId.ASPD => true,
        StatId.THORNS => false,
        StatId.RARITY_SHIFT => false,
        StatId.CRIT => true,
        StatId.CDMG => true,
        StatId.LIFESTEAL => true,
        StatId.DODGE => true,
        StatId.BLOCK => true,
        StatId.PEN => true,
        StatId.DMG_PCT => true,
        StatId.DR_PCT => true,
        StatId.HEAL_PCT => true,
        StatId.GOLD_PCT => true,
        StatId.CROWNS_PCT => true,
        StatId.DROP_CHANCE => true,
        StatId.ENERGY_REGEN_PCT => true,
        StatId.PET_AURA_PCT => true,
        StatId.SHOP_PRICE_PCT => true,
        StatId.XP_PCT => true,
        StatId.BEAST_FEED_PCT => true,
        StatId.STONE_PCT => true,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stat), stat,
            "This stat is not classified as an amount or a share. Classify it here before a gear affix " +
            "can write onto it, or a screen draws its roll in the wrong unit."),
    };

    /// <summary>
    /// Whether an affix's rolled value is a share: it is when the bucket is the percent one, and it is
    /// when the stat it adds onto is itself a share.
    /// </summary>
    /// <param name="stat">The stat the affix writes.</param>
    /// <param name="op">The bucket it writes into.</param>
    internal static bool IsShare(StatId stat, EffectOp op) => op == EffectOp.STAT_ADD_PCT || IsShare(stat);

    /// <summary>
    /// Whether a smaller figure is the better one — the two multipliers a player wants LOW.
    /// </summary>
    /// <remarks>
    /// 🔒 Exhaustive for the same reason <see cref="IsShare(StatId)"/> is. The enum's own words are the
    /// authority: damage taken is <em>"base 1.0, so lower is better"</em>, and a shop price is a cost.
    /// Every other stat is a gain to raise. The damage-reduction affix is authored NEGATIVE on this
    /// account, and a screen that signed its roll by raw value would write a benefit as a loss.
    /// </remarks>
    /// <param name="stat">The stat.</param>
    /// <exception cref="ArgumentOutOfRangeException">A stat the vocabulary does not declare.</exception>
    internal static bool LowerIsBetter(StatId stat) => stat switch
    {
        StatId.DR_PCT => true,
        StatId.SHOP_PRICE_PCT => true,
        StatId.MAX_HP or StatId.ATK or StatId.DEF or StatId.ASPD or StatId.CRIT or StatId.CDMG or
        StatId.LIFESTEAL or StatId.DODGE or StatId.BLOCK or StatId.PEN or StatId.DMG_PCT or
        StatId.HEAL_PCT or StatId.THORNS or StatId.GOLD_PCT or StatId.CROWNS_PCT or
        StatId.DROP_CHANCE or StatId.RARITY_SHIFT or StatId.ENERGY_REGEN_PCT or StatId.PET_AURA_PCT or
        StatId.XP_PCT or StatId.BEAST_FEED_PCT or StatId.STONE_PCT => false,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stat), stat,
            "This stat has no polarity classified. Say whether a player wants it high or low before a " +
            "gear affix can write onto it, or a screen signs its roll the wrong way round."),
    };

    /// <summary>Whether a rolled value moves its stat the way a player wants it to move.</summary>
    /// <param name="stat">The stat the roll writes.</param>
    /// <param name="value">The roll.</param>
    internal static bool IsFavourable(StatId stat, double value) => LowerIsBetter(stat) ? value < 0.0 : value > 0.0;
}
