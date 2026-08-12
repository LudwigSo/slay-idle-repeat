using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// 🔒 `05` §2 — the hero's <c>Base(stat)</c>, the block `18` §8 step 4 starts adding to.
/// </summary>
/// <remarks>
/// <para>
/// `05` §2 writes three stats as curves in the Legend Level and eleven as constants:
/// </para>
/// <code>
/// MaxHP  = 250 + 45 * L        ATK    = 30  + 6  * L        DEF    = 15  + 3  * L
/// ASPD   = 1.00                CRIT   = 0.05               CDMG   = 0.50
/// LS     = 0.00                DODGE  = 0.02               BLOCK  = 0.00
/// PEN    = 0.00                DMG%   = 0.00               DR%    = 0.00
/// HEAL%  = 1.00                THORN  = 0.00
/// where L = Legend Level (1..200)
/// </code>
/// <para>
/// 🔒 <b>None of those numbers is in this file.</b> `05` §2 carries a 📐 TUNABLE marker and `14` §6
/// is unambiguous — <em>"every number marked 📐 TUNABLE lives in <c>game-data/*.json</c>, never in
/// code"</em> — so the fourteen rows are read from <c>content/combat_caps.json#/heroBaseStats</c>
/// and what lives here is the <em>shape</em>: <c>base + perLevel × L</c>, and the ruling that all
/// fourteen must be present. A constant row is a row with <c>perLevel</c> 0, which is a
/// transcription of <c>ASPD = 1.00</c> rather than a filled hole.
/// </para>
/// <para>
/// 🔒 <b><c>HEAL_PCT</c>'s base is 1.0, not 0.</b> `05` §2's own comment: <em>"a multiplier on ALL
/// healing received; base 1.0, so lifesteal and heals work with no modifiers. '+35% Healing
/// Received' ⇒ ×1.35."</em> A 0 there is not a small error — it multiplies `05` §4.3's
/// <c>Heal()</c> by zero and silently disables every heal, every lifesteal tick and every REGEN in
/// the game, while every test that does not heal stays green. The schema requires the row and
/// <see cref="Read"/> refuses a curve that omits it.
/// </para>
/// <para>
/// ⚠️ <b>The level range is enforced, not clamped.</b> `05` §2 authors the curve for
/// <c>L = 1..200</c> and says nothing about either side of it. Clamping would answer a question the
/// document has not been asked; <see cref="At"/> throws instead.
/// </para>
/// </remarks>
internal sealed class HeroBaseCurve
{
    private readonly IReadOnlyDictionary<StatId, Row> _rows;

    private HeroBaseCurve(IReadOnlyDictionary<StatId, Row> rows, int minimumLevel, int maximumLevel)
    {
        _rows = rows;
        MinimumLevel = minimumLevel;
        MaximumLevel = maximumLevel;
    }

    /// <summary>`05` §2 — the lowest Legend Level the curve is authored for.</summary>
    internal int MinimumLevel { get; }

    /// <summary>`05` §2 — the highest Legend Level the curve is authored for.</summary>
    internal int MaximumLevel { get; }

    /// <summary>Builds a curve from a complete set of `05` §2's fourteen rows.</summary>
    /// <param name="rows">One <c>(base, perLevel)</c> row per combat stat. Not more, not fewer.</param>
    /// <param name="minimumLevel">`05` §2's lower Legend Level bound.</param>
    /// <param name="maximumLevel">`05` §2's upper Legend Level bound.</param>
    /// <exception cref="ArgumentException">
    /// A combat stat has no row, a row names something that is not a combat stat, a coefficient is
    /// not finite, or the level bounds are not a non-empty ascending range.
    /// </exception>
    internal static HeroBaseCurve From(
        IReadOnlyDictionary<StatId, (double Base, double PerLevel)> rows, int minimumLevel, int maximumLevel)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var missing = StatIds.Combat.Where(stat => !rows.ContainsKey(stat)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"05 §2: the hero base curve states all {StatIds.Combat.Count} combat stats — 'every " +
                "actor carries a complete 14-stat block with these defaults. An unstated stat is a bug, " +
                $"not a zero.' Missing: {string.Join(", ", missing)}.",
                nameof(rows));
        }

        var extra = rows.Keys.Where(stat => !StatIds.Combat.Contains(stat)).ToArray();
        if (extra.Length > 0)
        {
            throw new ArgumentException(
                $"05 §2's hero base curve covers the {StatIds.Combat.Count} combat stats only; " +
                $"{string.Join(", ", extra)} {(extra.Length == 1 ? "is" : "are")} not among them.",
                nameof(rows));
        }

        if (minimumLevel < 1 || maximumLevel < minimumLevel)
        {
            throw new ArgumentException(
                $"05 §2 authors the curve over 'L = Legend Level (1..200)'. " +
                $"[{minimumLevel.ToString(CultureInfo.InvariantCulture)}, " +
                $"{maximumLevel.ToString(CultureInfo.InvariantCulture)}] is not a Legend Level range.",
                nameof(minimumLevel));
        }

        var table = new Dictionary<StatId, Row>(StatIds.Combat.Count);
        foreach (var stat in StatIds.Combat)
        {
            var (baseValue, perLevel) = rows[stat];

            if (!double.IsFinite(baseValue) || !double.IsFinite(perLevel))
            {
                throw new ArgumentException(
                    $"05 §2: {stat}'s row is base " +
                    $"{baseValue.ToString("R", CultureInfo.InvariantCulture)}, perLevel " +
                    $"{perLevel.ToString("R", CultureInfo.InvariantCulture)}. Both must be finite.",
                    nameof(rows));
            }

            table[stat] = new Row(baseValue, perLevel);
        }

        return new HeroBaseCurve(table, minimumLevel, maximumLevel);
    }

    /// <summary>
    /// `05` §2's <c>Base(stat)</c> at a Legend Level, rounded at the accumulation point
    /// (`05` §1.1).
    /// </summary>
    /// <param name="legendLevel">L, within the authored range.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="legendLevel"/> is outside <see cref="MinimumLevel"/>..<see cref="MaximumLevel"/>.
    /// </exception>
    internal ActorStats At(int legendLevel)
    {
        if (legendLevel < MinimumLevel || legendLevel > MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(legendLevel), legendLevel,
                $"05 §2 authors the hero base curve for Legend Level " +
                $"{MinimumLevel.ToString(CultureInfo.InvariantCulture)}.." +
                $"{MaximumLevel.ToString(CultureInfo.InvariantCulture)} and says nothing about either " +
                "side of it. Clamping would answer a question 05 has not been asked.");
        }

        var values = new Dictionary<StatId, double>(StatIds.Combat.Count);
        foreach (var stat in StatIds.Combat)
        {
            var row = _rows[stat];
            values[stat] = StatRounding.Round(row.Base + (row.PerLevel * legendLevel), stat, "the 05 §2 base curve");
        }

        return ActorStats.From(values);
    }

    private readonly record struct Row(double Base, double PerLevel);
}
