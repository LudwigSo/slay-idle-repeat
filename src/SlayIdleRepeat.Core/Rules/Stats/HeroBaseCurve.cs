using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// The hero's <c>Base(stat)</c> — the block stat aggregation starts adding to.
/// </summary>
/// <remarks>
/// None of the actual numbers live in this file — the fourteen rows are read from
/// <c>content/combat_caps.json#/heroBaseStats</c>; what lives here is the shape,
/// <c>base + perLevel × L</c>, and the rule that all fourteen must be present. A constant stat is a
/// row with <c>perLevel</c> 0, not a filled hole. <c>HEAL_PCT</c>'s base is 1.0, not 0: it's a
/// multiplier on all healing received, so a 0 there would silently disable every heal and lifesteal
/// tick in the game. The level range is enforced, not clamped — <see cref="At"/> throws outside the
/// authored range rather than answering a question the design hasn't specified.
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

    /// <summary>The lowest Legend Level the curve is authored for.</summary>
    internal int MinimumLevel { get; }

    /// <summary>The highest Legend Level the curve is authored for.</summary>
    internal int MaximumLevel { get; }

    /// <summary>Builds a curve from a complete set of the fourteen rows.</summary>
    /// <param name="rows">One <c>(base, perLevel)</c> row per combat stat. Not more, not fewer.</param>
    /// <param name="minimumLevel">The lower Legend Level bound.</param>
    /// <param name="maximumLevel">The upper Legend Level bound.</param>
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
    /// <c>Base(stat)</c> at a Legend Level, rounded at the accumulation point.
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
