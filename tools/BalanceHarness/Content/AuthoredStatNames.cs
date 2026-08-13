using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.BalanceHarness.Model;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// 🔒 The one mapping from <c>tuning/calibration_builds.json</c>'s camelCase stat keys to `18` §2.1's
/// <see cref="StatId"/> vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// The two vocabularies genuinely differ on five names and the differences are not guessable:
/// <c>lifesteal</c> is `05` §1's <c>LS</c> and `18` §2.1's <see cref="StatId.LIFESTEAL"/>,
/// <c>thorns</c> is <c>THORN</c> / <see cref="StatId.THORNS"/>, <c>critDamage</c> is
/// <see cref="StatId.CDMG"/>, and <c>dmgPct</c> / <c>drPct</c> / <c>healPct</c> are `05` §1's
/// <c>DMG%</c> / <c>DR%</c> / <c>HEAL%</c>. Every other key is the obvious lowercase of the enum.
/// </para>
/// <para>
/// 🔒 <b><c>healPct</c>'s base is 1.0, not 0.</b> It multiplies all healing received, so a block that
/// silently defaulted it would disable every heal in the game and make the lifesteal archetype's
/// guardrail-1 number a fiction. <see cref="Read"/> therefore requires all fourteen keys and refuses a
/// missing one rather than filling it.
/// </para>
/// </remarks>
public static class AuthoredStatNames
{
    /// <summary>The authored key for each of `05` §1's fourteen combat stats.</summary>
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
