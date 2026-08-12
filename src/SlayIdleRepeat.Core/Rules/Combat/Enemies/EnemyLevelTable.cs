using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// 🔒 `05` §6.0 — <c>EnemyLevel(c, t) = BaseEnemyLevel(c) + TierLevelBonus(t)</c>.
/// </summary>
/// <remarks>
/// <para>
/// `05` §6.0: <em>"The damage formula (§4) needs an attacker/defender <b>Level</b> for enemies,
/// which the Power derivation above does not produce. Enemy level is a <b>chapter-based table</b>,
/// set to roughly the Legend Level a player typically has at that chapter."</em> All enemies,
/// Elites, Guardians (`25` §3) and bosses in a <c>(chapter, tier)</c> share this level — it is the
/// term that keeps `05` §4's mitigation denominator growing, so defence has to keep growing to stay
/// relevant across chapters.
/// </para>
/// <para>
/// ⚠️ <b>ERRATUM — where this table lives.</b> `05` §6.0 says <em>"lives in
/// <c>data/tuning/par_power.json</c> beside the par table"</em>. It does not, and M2-11 could not
/// put it there: doc 21's catalogue fixes <c>game-data/tuning/</c> at sixteen files,
/// <c>par_power.schema.json</c> is <c>additionalProperties: false</c> with a closed
/// <c>required</c> list, and this milestone's standing rules forbid editing <c>tuning/</c> at all.
/// It is authored in <c>content/enemies/enemies.json</c> with the rest of `05` §6 and the conflict
/// is recorded here, in the schema and in the data file rather than resolved silently.
/// </para>
/// <para>
/// 🔒 <b>The tier arrives as an ORDINAL, and there is no tier enum.</b> `18` §4's <c>TIER</c>
/// condition is typed <em>"enum"</em> and no such enum exists anywhere in the repository;
/// <c>SubjectSetFloorTests.Pending</c> parks that hole with no assigned milestone, and steering S6
/// forbids inventing the members. <see cref="Ordinals"/> is therefore the authored order `05` §6.0
/// itself lists — Normal, Heroic, Mythic — and <c>IRunStateView.Tier</c> is the same
/// ordinal. When the milestone that owns difficulty tiers declares the enum, this becomes its
/// ordinal and nothing here changes.
/// </para>
/// </remarks>
internal sealed class EnemyLevelTable
{
    /// <summary>
    /// 🔒 The tier names in `05` §6.0's own order, which is also the order
    /// <c>tuning/par_power.json</c> writes them in — the only other file that names the tiers.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <see cref="List{T}"/> initialiser, not <c>[ … ]</c> and not <c>new[] { … }</c>. A
    /// collection expression targeting <see cref="IReadOnlyList{T}"/> synthesises a type in the
    /// <b>global</b> namespace, which
    /// <c>AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace</c> reports
    /// as an undocumented Core namespace. The same trap <c>CombatCaps.CappedStats</c> records.
    /// </remarks>
    internal static IReadOnlyList<string> Ordinals { get; } = new List<string> { "NORMAL", "HEROIC", "MYTHIC" };

    /// <summary>The lowest chapter `05` §6.0 authors a base level for.</summary>
    internal const int FirstChapter = 1;

    /// <summary>The highest chapter `05` §6.0 authors a base level for.</summary>
    internal const int LastChapter = 8;

    private readonly IReadOnlyDictionary<int, int> _baseByChapter;
    private readonly IReadOnlyList<int> _tierBonus;

    private EnemyLevelTable(IReadOnlyDictionary<int, int> baseByChapter, IReadOnlyList<int> tierBonus)
    {
        _baseByChapter = baseByChapter;
        _tierBonus = tierBonus;
    }

    /// <summary>Builds the table from `05` §6.0's two rows.</summary>
    /// <param name="baseByChapter">
    /// <c>BaseEnemyLevel(c)</c> — one entry per chapter <see cref="FirstChapter"/>..<see cref="LastChapter"/>,
    /// no more and no fewer.
    /// </param>
    /// <param name="tierBonus">
    /// <c>TierLevelBonus(t)</c>, indexed by the ordinal of <see cref="Ordinals"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A chapter is missing or unknown, or the tier bonuses are not exactly
    /// <see cref="Ordinals"/> long.
    /// </exception>
    internal static EnemyLevelTable From(
        IReadOnlyDictionary<int, int> baseByChapter, IReadOnlyList<int> tierBonus)
    {
        ArgumentNullException.ThrowIfNull(baseByChapter);
        ArgumentNullException.ThrowIfNull(tierBonus);

        var expected = Enumerable.Range(FirstChapter, LastChapter - FirstChapter + 1).ToArray();

        var missing = expected.Where(c => !baseByChapter.ContainsKey(c)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"05 §6.0 authors a BaseEnemyLevel for every chapter {Range()}, and these have none: " +
                $"{string.Join(", ", missing.Select(c => c.ToString(CultureInfo.InvariantCulture)))}. " +
                "A chapter with no base level would produce a level-0 enemy, which 05 §4's mitigation " +
                "denominator reads as an attacker that never grows.",
                nameof(baseByChapter));
        }

        var unknown = baseByChapter.Keys.Except(expected).OrderBy(c => c).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException(
                $"05 §6.0's table covers chapters {Range()} and this one also states " +
                $"{string.Join(", ", unknown.Select(c => c.ToString(CultureInfo.InvariantCulture)))}. " +
                "A ninth chapter is a design decision, not a data edit.",
                nameof(baseByChapter));
        }

        if (tierBonus.Count != Ordinals.Count)
        {
            throw new ArgumentException(
                $"05 §6.0 states {Ordinals.Count.ToString(CultureInfo.InvariantCulture)} tier bonuses " +
                $"({string.Join(", ", Ordinals)}) and this table has " +
                $"{tierBonus.Count.ToString(CultureInfo.InvariantCulture)}.",
                nameof(tierBonus));
        }

        return new EnemyLevelTable(
            expected.ToDictionary(c => c, c => baseByChapter[c]), tierBonus.ToArray());
    }

    /// <summary>`05` §6.0 — <c>BaseEnemyLevel(c) + TierLevelBonus(t)</c>.</summary>
    /// <param name="chapter">The chapter, <see cref="FirstChapter"/>..<see cref="LastChapter"/>.</param>
    /// <param name="tierOrdinal">The tier's ordinal in <see cref="Ordinals"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is outside its authored range.</exception>
    internal int Of(int chapter, int tierOrdinal)
    {
        if (!_baseByChapter.TryGetValue(chapter, out var baseLevel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapter), chapter,
                $"05 §6.0 authors BaseEnemyLevel for chapters {Range()} only.");
        }

        if (tierOrdinal < 0 || tierOrdinal >= _tierBonus.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tierOrdinal), tierOrdinal,
                $"05 §6.0 states {_tierBonus.Count.ToString(CultureInfo.InvariantCulture)} tiers " +
                $"({string.Join(", ", Ordinals)}), so the ordinal is " +
                $"0..{(_tierBonus.Count - 1).ToString(CultureInfo.InvariantCulture)}. " +
                "18 §4's TIER condition is typed 'enum' and no tier enum exists yet — see the type " +
                "remarks and SubjectSetFloorTests' Tier entry.");
        }

        return baseLevel + _tierBonus[tierOrdinal];
    }

    /// <summary>`05` §6.0 — <c>BaseEnemyLevel(c)</c> on its own.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The chapter is outside the authored range.</exception>
    internal int BaseLevel(int chapter) => Of(chapter, 0) - _tierBonus[0];

    /// <summary>`05` §6.0 — <c>TierLevelBonus(t)</c> on its own.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The ordinal is outside the authored range.</exception>
    internal int TierBonus(int tierOrdinal) => Of(FirstChapter, tierOrdinal) - _baseByChapter[FirstChapter];

    private static string Range() =>
        $"{FirstChapter.ToString(CultureInfo.InvariantCulture)}..{LastChapter.ToString(CultureInfo.InvariantCulture)}";
}
