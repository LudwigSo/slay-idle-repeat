using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.BalanceHarness.Model;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>Maps <c>tuning/calibration_builds.json</c>'s camelCase stat keys to <see cref="StatId"/>.</summary>
/// <remarks>
/// Five names genuinely differ and aren't guessable: <c>lifesteal</c>/<see cref="StatId.LIFESTEAL"/>,
/// <c>thorns</c>/<see cref="StatId.THORNS"/>, <c>critDamage</c>/<see cref="StatId.CDMG"/>, and
/// <c>dmgPct</c>/<c>drPct</c>/<c>healPct</c>. Every other key is the obvious lowercase of the enum.
/// <c>healPct</c>'s base is 1.0, not 0 — it multiplies all healing received, so a silent default would
/// disable every heal in the game. <see cref="Read"/> therefore requires all fourteen keys.
/// </remarks>
public static class AuthoredStatNames
{
    /// <summary>The authored key for each combat stat.</summary>
    public static IReadOnlyDictionary<StatId, string> ByStat { get; } = new Dictionary<StatId, string>
    {
        [StatId.MAX_HP] = "maxHp",
        [StatId.ATK] = "atk",
        [StatId.DEF] = "def",
        [StatId.ASPD] = "aspd",
        [StatId.CRIT] = "crit",
        [StatId.CDMG] = "critDamage",
        [StatId.LIFESTEAL] = "lifesteal",
        [StatId.DODGE] = "dodge",
        [StatId.BLOCK] = "block",
        [StatId.PEN] = "pen",
        [StatId.DMG_PCT] = "dmgPct",
        [StatId.DR_PCT] = "drPct",
        [StatId.HEAL_PCT] = "healPct",
        [StatId.THORNS] = "thorns",
    };

    /// <summary>
    /// Reads a complete fourteen-stat block out of an authored <c>stats</c> object.
    /// </summary>
    /// <param name="content">The loaded snapshot.</param>
    /// <param name="statsPointer">
    /// The reference of the <c>stats</c> object itself, e.g.
    /// <c>tuning/calibration_builds.json#/referenceParBuild/stats</c>.
    /// </param>
    /// <exception cref="MissingContentException">A stat key is absent — never defaulted.</exception>
    /// <exception cref="UnauthorisedTunableException">A stat is authored <c>null</c>.</exception>
    public static StatLine Read(ContentSnapshot content, string statsPointer)
    {
        ArgumentNullException.ThrowIfNull(content);

        var values = new Dictionary<StatId, double>(ByStat.Count);
        foreach (var (stat, key) in ByStat)
        {
            values[stat] = content.ReadDouble($"{statsPointer}/{key}");
        }

        return StatLine.From(values);
    }
}
