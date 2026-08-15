using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// <c>tuning/par_power.json</c> — the twenty-four authored <c>(chapter, tier)</c> par cells and the
/// clear-rate band.
/// </summary>
/// <remarks>
/// The cells are read, never recomputed: the file's <c>defaultFill</c> block is explicitly documented
/// as the default fill, not a constraint, so evaluating it would grade the game against a table nobody
/// may have actually authored. This table's cell is <c>ChapterPowerTarget(c) × TierMult(t)</c>, which
/// is what makes <c>EnemyPower(i)</c> computable — see <c>Rules/NodePower.cs</c>.
/// </remarks>
public sealed class ParPowerTable
{
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

    /// <summary><c>clearRateAtPar.target</c>. Authored 0.70.</summary>
    public double ClearRateTarget { get; }

    /// <summary><c>clearRateAtPar.assertionMin</c>. Authored 0.62.</summary>
    public double ClearRateMin { get; }

    /// <summary><c>clearRateAtPar.assertionMax</c>. Authored 0.78.</summary>
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

    /// <summary><c>ParPower(c, t)</c>, straight off the authored cell.</summary>
    /// <exception cref="KeyNotFoundException">The cell is not in the table.</exception>
    public double Power(int chapter, Tier tier) =>
        _cells.TryGetValue((chapter, tier), out var power)
            ? power
            : throw new KeyNotFoundException(
                $"{Document}#/parPower has no chapter " +
                $"{chapter.ToString(CultureInfo.InvariantCulture)} {tier} cell. The authored chapters " +
                $"are {string.Join(", ", Chapters.Select(c => c.ToString(CultureInfo.InvariantCulture)))}.");
}
