using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// 🔒 <c>tuning/par_power.json</c> — `29` §4's twenty-four authored <c>(chapter, tier)</c> par cells
/// and `05` §9's clear-rate band.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The twenty-four cells are read, never recomputed.</b> The file carries a
/// <c>defaultFill</c> block (<c>1000 × 2^(c-1)</c> and tier multipliers 1/4/16) and says of it in so
/// many words that <em>"every cell is independently editable and the formula below is the default
/// fill, not a constraint"</em>. A harness that evaluated the formula would grade the game against a
/// table nobody may have authored, and would stop noticing the day design edits one cell.
/// </para>
/// <para>
/// 🔒 <c>ChapterPowerTarget(c) × TierMult(t)</c> of `02` §4.3 <b>is</b> this table's cell — that is
/// what makes <c>EnemyPower(i)</c> computable from authored data with only `02` §4.3's node terms
/// added. See <c>Rules/NodePower.cs</c>.
/// </para>
/// </remarks>
public sealed class ParPowerTable
{
    /// <summary>`29` §4-5 — the document.</summary>
    public const string Document = "tuning/par_power.json";

    private readonly IReadOnlyDictionary<(int Chapter, Tier Tier), double> _cells;

    private ParPowerTable(
        IReadOnlyDictionary<(int Chapter, Tier Tier), double> cells,
        IReadOnlyList<int> chapters,
        double clearRateTarget,
        double clearRateMin,
        double clearRateMax)
    {
        _cells = cells;
        Chapters = chapters;
        ClearRateTarget = clearRateTarget;
        ClearRateMin = clearRateMin;
        ClearRateMax = clearRateMax;
    }

    /// <summary>The authored chapters, ascending. Eight of them.</summary>
    public IReadOnlyList<int> Chapters { get; }

    /// <summary>`05` §9 — <c>clearRateAtPar.target</c>. Authored 0.70.</summary>
    public double ClearRateTarget { get; }

    /// <summary>🔒 `05` §9 — <c>clearRateAtPar.assertionMin</c>. Authored 0.62.</summary>
    public double ClearRateMin { get; }

    /// <summary>🔒 `05` §9 — <c>clearRateAtPar.assertionMax</c>. Authored 0.78.</summary>
    public double ClearRateMax { get; }

    /// <summary>Reads the document.</summary>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    public static ParPowerTable Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rows = content.Read($"{Document}#/parPower").Items;
        var cells = new Dictionary<(int, Tier), double>(rows.Count * Tiers.All.Count);
        var chapters = new List<int>(rows.Count);

        for (var i = 0; i < rows.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            var chapter = content.ReadInt32($"{Document}#/parPower/{index}/chapter");
            chapters.Add(chapter);

            foreach (var tier in Tiers.All)
            {
                cells[(chapter, tier)] = content.ReadDouble($"{Document}#/parPower/{index}/{tier}");
            }
        }

        chapters.Sort();

        return new ParPowerTable(
            cells,
            chapters,
            content.ReadDouble($"{Document}#/clearRateAtPar/target"),
            content.ReadDouble($"{Document}#/clearRateAtPar/assertionMin"),
            content.ReadDouble($"{Document}#/clearRateAtPar/assertionMax"));
    }

    /// <summary>🔒 `29` §4 — <c>ParPower(c, t)</c>, straight off the authored cell.</summary>
    /// <exception cref="KeyNotFoundException">The cell is not in the table.</exception>
    public double Power(int chapter, Tier tier) =>
        _cells.TryGetValue((chapter, tier), out var power)
            ? power
            : throw new KeyNotFoundException(
                $"{Document}#/parPower has no chapter " +
                $"{chapter.ToString(CultureInfo.InvariantCulture)} {tier} cell. The authored chapters " +
                $"are {string.Join(", ", Chapters.Select(c => c.ToString(CultureInfo.InvariantCulture)))}.");
}
