using System.Globalization;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>One archetype's draw weight in a chapter's pool.</summary>
/// <param name="Archetype">The shape.</param>
/// <param name="Weight">
/// Its weight in this chapter. A zero is authored intent, not an absence — e.g. no REAVER in the
/// tutorial chapter.
/// </param>
internal readonly record struct ArchetypeWeight(EnemyArchetype Archetype, double Weight);

/// <summary>
/// One chapter's enemy pool — the weight table a <c>TILE_ENEMY</c> battle draws from, and the two
/// elites the chapter's <c>elitePool</c> holds.
/// </summary>
/// <remarks>
/// <para>
/// A <c>TILE_ENEMY</c> battle draws one entry by weight, and a <c>SWARM</c> draw spawns its three
/// units. Elites come only from <see cref="ElitePool"/>, never from the weight table, and every tier
/// reuses the same pool — difficulty comes from the Power multiplier and enemy level, not composition.
/// </para>
/// <para>
/// All eight shapes are present in every row, including the zeros: the weight table is stated over
/// the closed archetype set rather than over whatever the data holds, so an archetype missing from a
/// chapter is a load failure rather than a silently edited-out design statement.
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

    /// <summary>The eight weights, in archetype order.</summary>
    internal IReadOnlyList<ArchetypeWeight> Weights { get; }

    /// <summary>The chapter's two elite identities. Elites are drawn from here and nowhere else.</summary>
    internal IReadOnlyList<string> ElitePool { get; }

    /// <summary>The sum of the row's weights. Every row totals 100.</summary>
    internal double TotalWeight { get; }

    /// <summary>Builds a pool, over the whole archetype set.</summary>
    /// <param name="chapter">The chapter, <c>1..8</c>.</param>
    /// <param name="weights">One weight per archetype — all eight, no more, no fewer.</param>
    /// <param name="elitePool">The chapter's elite identity ids.</param>
    /// <exception cref="ArgumentException">
    /// An archetype is missing or repeated, a weight is negative or not finite, or every weight is
    /// zero.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The chapter is out of range.</exception>
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

        // Elites come only from here, so an empty pool is a chapter that can present no elite.
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
    /// Draws one archetype by weight — exactly one draw index, whatever the table holds.
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
    /// <exception cref="ArgumentOutOfRangeException">The archetype is not one of the eight.</exception>
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
