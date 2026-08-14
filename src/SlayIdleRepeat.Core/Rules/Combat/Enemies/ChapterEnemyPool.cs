using System.Globalization;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>One archetype's draw weight in a chapter's pool.</summary>
/// <param name="Archetype">The `05` §6.1 shape.</param>
/// <param name="Weight">
/// Its weight in this chapter. 🔒 A zero is <b>authored intent</b>, not an absence: Chapter 1 has no
/// <c>REAVER</c> — <em>"no 30%-crit spikes in the tutorial chapter"</em> — and Chapter 6 no
/// <c>LEECH</c> — <em>"machines do not drink"</em>.
/// </param>
internal readonly record struct ArchetypeWeight(EnemyArchetype Archetype, double Weight);

/// <summary>
/// 🔒 One chapter's enemy pool — the weight table a <c>TILE_ENEMY</c> battle draws from, and the two
/// elites the chapter's <c>elitePool</c> holds.
/// </summary>
/// <remarks>
/// <para>
/// A <c>TILE_ENEMY</c> battle draws <b>one</b> entry by weight, and a <c>SWARM</c> draw spawns its
/// three units (<see cref="ArchetypeRow.UnitsPerDraw"/>). Elites come only from
/// <see cref="ElitePool"/> — never from the weight table — and the tiers reuse the same pool,
/// because tier difficulty comes from the Power multiplier and the enemy level, not from
/// composition.
/// </para>
/// <para>
/// 🔒 <b>All eight shapes are present in every row, including the zeros.</b> The weight table is
/// stated over the closed archetype set rather than over "whatever the data holds", so an archetype
/// that disappears from a chapter is a load failure. A rule that only checked the row's total would
/// let Chapter 1's <c>REAVER</c> zero be edited into a five and Chapter 6's <c>LEECH</c> zero into a
/// five, with the sum still 100 and two authored design statements silently gone.
/// </para>
/// <para>
/// ⚠️ <b>The draw is one <c>WeightedPick</c> and the seed is handed in.</b> `14` §8.0/§8.1: every
/// call consumes exactly one draw index, and the caller opens
/// <c>new DeterministicRng(battleSeed, RngStreams.Combat)</c>. Nothing here derives or holds a
/// <c>runSeed</c>.
/// </para>
/// </remarks>
internal sealed class ChapterEnemyPool
{
    private readonly IReadOnlyList<(EnemyArchetype item, double weight)> _table;

    private ChapterEnemyPool(
        int chapter, IReadOnlyList<ArchetypeWeight> weights, IReadOnlyList<string> elitePool)
    {
        Chapter = chapter;
        Weights = weights;
        ElitePool = elitePool;

        var table = new List<(EnemyArchetype item, double weight)>(weights.Count);
        var total = 0.0;

        foreach (var weight in weights)
        {
            table.Add((weight.Archetype, weight.Weight));
            total += weight.Weight;
        }

        _table = table;
        TotalWeight = total;
    }

    /// <summary>The chapter this pool belongs to.</summary>
    internal int Chapter { get; }

    /// <summary>The eight weights, in `05` §6.1's archetype order.</summary>
    internal IReadOnlyList<ArchetypeWeight> Weights { get; }

    /// <summary>The chapter's two elite identities. Elites are drawn from here and nowhere else.</summary>
    internal IReadOnlyList<string> ElitePool { get; }

    /// <summary>The sum of the row's weights. `05` §6.4 states every row totals 100.</summary>
    internal double TotalWeight { get; }

    /// <summary>Builds a pool, over the whole archetype set.</summary>
    /// <param name="chapter">The chapter, <c>1..8</c>.</param>
    /// <param name="weights">One weight per archetype — all eight, no more, no fewer.</param>
    /// <param name="elitePool">The chapter's elite identity ids.</param>
    /// <exception cref="ArgumentException">
    /// An archetype is missing or repeated, a weight is negative or not finite, or every weight is
    /// zero.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The chapter is outside `05` §6's range.</exception>
    internal static ChapterEnemyPool From(
        int chapter, IReadOnlyList<ArchetypeWeight> weights, IReadOnlyList<string> elitePool)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(elitePool);

        if (chapter < EnemyLevelTable.FirstChapter || chapter > EnemyLevelTable.LastChapter)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapter), chapter,
                $"05 §6 covers chapters {EnemyLevelTable.FirstChapter.ToString(CultureInfo.InvariantCulture)}.." +
                $"{EnemyLevelTable.LastChapter.ToString(CultureInfo.InvariantCulture)}.");
        }

        var all = Enum.GetValues<EnemyArchetype>();
        var seen = weights.Select(w => w.Archetype).ToArray();

        var missing = all.Where(a => !seen.Contains(a)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"a chapter's enemy pool states a weight for every one of 05 §6.1's " +
                $"{all.Length.ToString(CultureInfo.InvariantCulture)} archetypes, including the zeros, " +
                $"and chapter {chapter.ToString(CultureInfo.InvariantCulture)} has none for " +
                $"{string.Join(", ", missing)}. An omitted archetype and an authored zero are " +
                "indistinguishable once the row is read, and two of those zeros — Chapter 1's REAVER " +
                "and Chapter 6's LEECH — are design statements, not incidental.",
                nameof(weights));
        }

        var duplicated = seen.GroupBy(a => a).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicated.Length > 0)
        {
            throw new ArgumentException(
                $"chapter {chapter.ToString(CultureInfo.InvariantCulture)} states a weight twice for " +
                $"{string.Join(", ", duplicated)}. Two weights for one shape is two different pools.",
                nameof(weights));
        }

        var malformed = weights.Where(w => !double.IsFinite(w.Weight) || w.Weight < 0.0).ToArray();
        if (malformed.Length > 0)
        {
            throw new ArgumentException(
                $"chapter {chapter.ToString(CultureInfo.InvariantCulture)} states a weight that is " +
                $"negative or not finite: {string.Join(", ", malformed)}. WeightedPick skips a " +
                "non-positive row, so a negative weight is not 'never drawn' — it is an unbalanced " +
                "total that shifts every other row's share.",
                nameof(weights));
        }

        if (weights.All(w => w.Weight == 0.0))
        {
            throw new ArgumentException(
                $"every weight in chapter {chapter.ToString(CultureInfo.InvariantCulture)}'s pool is " +
                "zero, so a TILE_ENEMY battle there can draw nothing at all.",
                nameof(weights));
        }

        // 🔒 The elite pool gets the same treatment as the weight table, and it has to: 05 §6.2 says
        // elites come ONLY from here, so an empty pool is a chapter that can present no elite and a
        // repeated id is a chapter with one elite wearing two hats. The schema stops both for the
        // shipped file; a Core caller building a pool from anything else has no schema at all.
        if (elitePool.Count == 0)
        {
            throw new ArgumentException(
                $"chapter {chapter.ToString(CultureInfo.InvariantCulture)}'s elitePool is empty, and " +
                "05 §6.2 has elites come only from an elitePool — so the chapter could present none.",
                nameof(elitePool));
        }

        var repeated = elitePool.GroupBy(e => e, StringComparer.Ordinal)
                                .Where(g => g.Count() > 1)
                                .Select(g => g.Key)
                                .ToArray();
        if (repeated.Length > 0)
        {
            throw new ArgumentException(
                $"chapter {chapter.ToString(CultureInfo.InvariantCulture)}'s elitePool repeats " +
                $"{string.Join(", ", repeated)}. 05 §6.2 gives each chapter exactly its two biome " +
                "elites; a repeat is one elite drawn twice as often as the other.",
                nameof(elitePool));
        }

        return new ChapterEnemyPool(chapter, weights.ToArray(), elitePool.ToArray());
    }

    /// <summary>
    /// Draws one archetype by weight — `14` §8.0: exactly one draw index, whatever the table holds.
    /// </summary>
    /// <param name="rng">
    /// The encounter's combat stream, <c>new DeterministicRng(battleSeed, RngStreams.Combat)</c>.
    /// </param>
    internal EnemyArchetype Draw(DeterministicRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        return rng.WeightedPick(_table);
    }

    /// <summary>One archetype's weight in this chapter.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The archetype is not one of `05` §6.1's eight.</exception>
    internal double WeightOf(EnemyArchetype archetype)
    {
        foreach (var weight in Weights)
        {
            if (weight.Archetype == archetype)
            {
                return weight.Weight;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(archetype), archetype,
            $"chapter {Chapter.ToString(CultureInfo.InvariantCulture)}'s pool states no weight for it. " +
            "05 §6.1's eight shapes are the closed set every pool is stated over.");
    }
}
