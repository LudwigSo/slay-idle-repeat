using System.Globalization;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// <c>content/bosses/bosses.json</c>'s script list, reduced to what the sweep needs: which script a
/// chapter's boss node runs, its coefficients, and whether it summons.
/// </summary>
/// <remarks>
/// <c>BOSS_FTUE</c> is excluded from the sweep because it is the only script with no <c>chapter</c> —
/// it carries <c>fixedPower</c>/<c>fixedLevel</c> instead, so there is no <c>ParPower(c, t)</c> cell to
/// start from. <c>effects</c> and <c>phases</c> are not modelled here; the harness never interprets a
/// boss script itself (<c>CombatSimulator.SimulateBossFight</c> does). This reader is just the index.
/// </remarks>
public sealed class BossRoster
{
    public const string Document = "content/bosses/bosses.json";

    /// <summary>The FTUE mini-boss, excluded from the sweep. See the type remarks.</summary>
    public const string FtueScriptId = "BOSS_FTUE";

    private BossRoster(IReadOnlyList<BossEntry> all)
    {
        All = all;
        Campaign = all.Where(b => b.Chapter is not null)
                      .OrderBy(b => b.Chapter!.Value)
                      .ToArray();
        Excluded = all.Where(b => b.Chapter is null).ToArray();
    }

    /// <summary>Every authored script, in authored order.</summary>
    public IReadOnlyList<BossEntry> All { get; }

    /// <summary>The scripts that state a chapter, ascending by chapter — the ones the sweep fights.</summary>
    public IReadOnlyList<BossEntry> Campaign { get; }

    /// <summary>The scripts with no chapter, which the sweep skips. <c>BOSS_FTUE</c> today.</summary>
    public IReadOnlyList<BossEntry> Excluded { get; }

    /// <summary>The scripts carrying <c>addsPowerFraction</c> — the summoning bosses.</summary>
    public IReadOnlyList<BossEntry> Summoners =>
        All.Where(b => b.AddsPowerFraction is not null).ToArray();

    /// <summary>Reads the document.</summary>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    public static BossRoster Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var scripts = content.Read($"{Document}#/scripts").Items;
        var entries = new List<BossEntry>(scripts.Count);

        for (var i = 0; i < scripts.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            var script = $"{Document}#/scripts/{index}";

            entries.Add(new BossEntry(
                Id: content.ReadText($"{script}/id"),
                Chapter: content.IsAuthorised($"{script}/chapter")
                    ? content.ReadInt32($"{script}/chapter")
                    : null,
                HpCoef: content.ReadDouble($"{script}/coefficients/hp"),
                AtkCoef: content.ReadDouble($"{script}/coefficients/atk"),
                DefCoef: content.ReadDouble($"{script}/coefficients/def"),
                AspdCoef: content.ReadDouble($"{script}/coefficients/aspd"),
                AddsPowerFraction: content.IsAuthorised($"{script}/addsPowerFraction")
                    ? content.ReadDouble($"{script}/addsPowerFraction")
                    : null));
        }

        return new BossRoster(entries);
    }

    /// <summary>The script a chapter's boss node runs.</summary>
    /// <exception cref="KeyNotFoundException">No script states that chapter.</exception>
    public BossEntry ForChapter(int chapter) =>
        Campaign.FirstOrDefault(b => b.Chapter == chapter)
        ?? throw new KeyNotFoundException(
            $"No script in {Document} states chapter " +
            $"{chapter.ToString(CultureInfo.InvariantCulture)}. The authored chapters are " +
            $"{string.Join(", ", Campaign.Select(b => b.Chapter!.Value.ToString(CultureInfo.InvariantCulture)))}.");

    /// <summary>The script with the given id.</summary>
    /// <exception cref="KeyNotFoundException">No script carries that id.</exception>
    public BossEntry Get(string id) =>
        All.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"'{id}' is not a script id in {Document}.");
}

/// <summary>One boss script's index entry.</summary>
/// <param name="Id">The script id, e.g. <c>BOSS_SPOREQUEEN_VELL</c>.</param>
/// <param name="Chapter">The chapter whose boss node runs it, or <c>null</c> for <c>BOSS_FTUE</c>.</param>
/// <param name="HpCoef">The <c>MaxHP</c> coefficient.</param>
/// <param name="AtkCoef">The <c>ATK</c> coefficient.</param>
/// <param name="DefCoef">The <c>DEF</c> coefficient. Guardrail 5 reads it.</param>
/// <param name="AspdCoef">The <c>ASPD</c> coefficient.</param>
/// <param name="AddsPowerFraction">
/// The share of the boss's power each add carries, or <c>null</c> for a script that summons nothing.
/// </param>
public sealed record BossEntry(
    string Id,
    int? Chapter,
    double HpCoef,
    double AtkCoef,
    double DefCoef,
    double AspdCoef,
    double? AddsPowerFraction);
