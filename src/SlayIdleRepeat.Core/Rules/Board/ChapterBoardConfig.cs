using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// The slice of a chapter's content (`game-data/schema/chapter.schema.json`) that
/// <see cref="BoardGenerator.GenerateBoard"/> needs — deliberately not the whole chapter
/// document: <c>Core</c> has zero dependencies, so nothing here does JSON parsing. A caller (the
/// Application-layer content loader) reads the chapter file and builds one of these.
/// </summary>
internal sealed class ChapterBoardConfig
{
    private ChapterBoardConfig(
        int chapterId,
        IReadOnlyList<int> stageLengths,
        IReadOnlyList<int> eliteCount,
        IReadOnlyList<IReadOnlyDictionary<TileKind, double>> tileWeights,
        IReadOnlyList<ForkCountRange> forksPerStage,
        string bossId,
        double forkBiasPlusMultiplier,
        double forkBiasMinusMultiplier)
    {
        ChapterId = chapterId;
        StageLengths = stageLengths;
        EliteCount = eliteCount;
        TileWeights = tileWeights;
        ForksPerStage = forksPerStage;
        BossId = bossId;
        ForkBiasPlusMultiplier = forkBiasPlusMultiplier;
        ForkBiasMinusMultiplier = forkBiasMinusMultiplier;
    }

    /// <summary>The chapter number.</summary>
    public int ChapterId { get; }

    /// <summary>The three stage lengths, e.g. <c>[12, 14, 16]</c>.</summary>
    public IReadOnlyList<int> StageLengths { get; }

    /// <summary>One elite count per stage, matching <see cref="StageLengths"/>.</summary>
    public IReadOnlyList<int> EliteCount { get; }

    /// <summary>
    /// One tile-kind weight table per stage, matching <see cref="StageLengths"/>. Consumed
    /// verbatim by the weighted draw, with no kind excluded, so a content author who wants a
    /// mandatory-placement kind (e.g. <see cref="TileKind.Shop"/>) to also be reachable by the
    /// weighted draw states it here and gets exactly that.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<TileKind, double>> TileWeights { get; }

    /// <summary>
    /// How many forks each stage may carry, matching <see cref="StageLengths"/>. Authored rather
    /// than a generator constant because a fork count that does not scale with stage length turns a
    /// long stage into a straight line, and `03` §3.1 makes the fork the board's one real navigation
    /// decision. The shipped chapters author `03` §1's 1-2, so today's boards are unchanged in
    /// shape by the move — only in where the number lives.
    /// </summary>
    public IReadOnlyList<ForkCountRange> ForksPerStage { get; }

    /// <summary>The chapter's boss identity — carried through onto the boss <see cref="BoardNode"/> but not otherwise interpreted here.</summary>
    public string BossId { get; }

    /// <summary>
    /// The multiplier <see cref="BoardGenerator"/> applies to a fork branch's boosted tile kinds.
    /// Read from content (<c>ChapterBoardTuning</c>) rather than a Core constant, since the
    /// magnitude is authored, not derived.
    /// </summary>
    public double ForkBiasPlusMultiplier { get; }

    /// <summary>The same, for the one suppressed tile kind a label may name.</summary>
    public double ForkBiasMinusMultiplier { get; }

    /// <summary>Builds a config, validating the shape the generator needs.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="stageLengths"/> or <paramref name="eliteCount"/> or <paramref name="tileWeights"/>
    /// or a supplied <paramref name="forksPerStage"/> does not have exactly 3 entries; a stage length is not positive; an elite count is negative
    /// or exceeds its stage length; a stage's weight table is empty, states
    /// <see cref="TileKind.Boss"/> (never drawable — placed once, structurally), or has no positive
    /// weight; <paramref name="bossId"/> is empty; <paramref name="forkBiasPlusMultiplier"/> is not
    /// greater than 1; or <paramref name="forkBiasMinusMultiplier"/> is not in (0, 1).
    /// </exception>
    /// <param name="forksPerStage">
    /// One inclusive fork-count range per stage. Defaults to `03` §1's authored 1-2 per stage
    /// (<see cref="ForkCountRange.Authored"/>) so callers that do not care about fork density — most
    /// fixture and negative-path tests — need not restate it; the one production caller,
    /// <see cref="ChapterBoardTuning.Read"/>, always passes an explicit, content-read value. This is
    /// the same arrangement, for the same reason, as the two multipliers below.
    /// </param>
    /// <param name="forkBiasPlusMultiplier">
    /// Defaults to the shipped 2.5x so callers that do not care about fork bias (most
    /// fixture/negative-path tests) need not restate it; the one production caller,
    /// <see cref="ChapterBoardTuning.Read"/>, always passes an explicit, content-read value.
    /// </param>
    /// <param name="forkBiasMinusMultiplier">The same, for the shipped 0.2x suppression.</param>
    public static ChapterBoardConfig From(
        int chapterId,
        IReadOnlyList<int> stageLengths,
        IReadOnlyList<int> eliteCount,
        IReadOnlyList<IReadOnlyDictionary<TileKind, double>> tileWeights,
        string bossId,
        IReadOnlyList<ForkCountRange>? forksPerStage = null,
        double forkBiasPlusMultiplier = 2.5,
        double forkBiasMinusMultiplier = 0.2)
    {
        ArgumentNullException.ThrowIfNull(stageLengths);
        ArgumentNullException.ThrowIfNull(eliteCount);
        ArgumentNullException.ThrowIfNull(tileWeights);
        ArgumentNullException.ThrowIfNull(bossId);

        if (!double.IsFinite(forkBiasPlusMultiplier) || forkBiasPlusMultiplier <= 1.0)
        {
            throw new ArgumentException(
                "A boost multiplier must exceed 1 — 03 §3.1's bias is a boost, not a flat pass-through.",
                nameof(forkBiasPlusMultiplier));
        }

        if (!double.IsFinite(forkBiasMinusMultiplier) || forkBiasMinusMultiplier is <= 0.0 or >= 1.0)
        {
            throw new ArgumentException(
                "A suppression multiplier must lie strictly between 0 and 1.",
                nameof(forkBiasMinusMultiplier));
        }

        if (stageLengths.Count != 3)
        {
            throw new ArgumentException("03 §1 fixes exactly 3 stages; stageLengths must have 3 entries.", nameof(stageLengths));
        }

        if (eliteCount.Count != 3)
        {
            throw new ArgumentException("03 §1 fixes exactly 3 stages; eliteCount must have 3 entries.", nameof(eliteCount));
        }

        if (tileWeights.Count != 3)
        {
            throw new ArgumentException("03 §1 fixes exactly 3 stages; tileWeights must have 3 entries.", nameof(tileWeights));
        }

        if (forksPerStage is not null && forksPerStage.Count != 3)
        {
            throw new ArgumentException("03 §1 fixes exactly 3 stages; forksPerStage must have 3 entries.", nameof(forksPerStage));
        }

        if (string.IsNullOrWhiteSpace(bossId))
        {
            throw new ArgumentException("bossId must not be empty.", nameof(bossId));
        }

        for (var i = 0; i < 3; i++)
        {
            var stageNumber = (i + 1).ToString(CultureInfo.InvariantCulture);

            if (stageLengths[i] <= 0)
            {
                throw new ArgumentException($"stage {stageNumber}'s length must be positive.", nameof(stageLengths));
            }

            if (eliteCount[i] < 0)
            {
                throw new ArgumentException($"stage {stageNumber}'s elite count must not be negative.", nameof(eliteCount));
            }

            // Re-checked here rather than trusted from ForkCountRange.Of: the type is a record
            // struct, so `default` and its primary constructor both reach this factory without ever
            // passing through Of. This is the gate; Of is the convenience.
            if (forksPerStage is not null && forksPerStage[i].Minimum < 0)
            {
                throw new ArgumentException($"stage {stageNumber}'s fork count minimum must not be negative.", nameof(forksPerStage));
            }

            if (forksPerStage is not null && forksPerStage[i].Maximum < forksPerStage[i].Minimum)
            {
                throw new ArgumentException($"stage {stageNumber}'s fork count maximum must not be below its minimum.", nameof(forksPerStage));
            }

            if (eliteCount[i] > stageLengths[i])
            {
                throw new ArgumentException($"stage {stageNumber}'s elite count ({eliteCount[i].ToString(CultureInfo.InvariantCulture)}) exceeds its length ({stageLengths[i].ToString(CultureInfo.InvariantCulture)}).", nameof(eliteCount));
            }

            var weights = tileWeights[i] ?? throw new ArgumentException($"stage {stageNumber}'s weight table is null.", nameof(tileWeights));

            if (weights.Count == 0)
            {
                throw new ArgumentException($"stage {stageNumber}'s weight table is empty — the weighted draw of 03 §3 would have nothing to draw.", nameof(tileWeights));
            }

            if (weights.ContainsKey(TileKind.Boss))
            {
                throw new ArgumentException($"stage {stageNumber}'s weight table states a weight for TILE_BOSS, which is never drawn — the boss is placed once, structurally.", nameof(tileWeights));
            }

            // The same rule as TILE_BOSS above, for the same reason and it is the reason the
            // mini-boss works at all: it is placed on a stage's LAST node so the stage-end clamp
            // makes it unmissable. A drawable weight would scatter more of them mid-stage, where
            // nothing stops a roll carrying past one, and a run would meet a gate it can skip.
            if (weights.ContainsKey(TileKind.MiniBoss))
            {
                throw new ArgumentException($"stage {stageNumber}'s weight table states a weight for TILE_MINIBOSS, which is never drawn — a stage's mini-boss is placed once, structurally, on its last node.", nameof(tileWeights));
            }

            if (weights.Values.All(w => w <= 0.0))
            {
                throw new ArgumentException($"stage {stageNumber}'s weight table has no positive weight — the weighted draw of 03 §3 could never pick anything.", nameof(tileWeights));
            }

            if (weights.Values.Any(w => !double.IsFinite(w) || w < 0.0))
            {
                throw new ArgumentException($"stage {stageNumber}'s weight table has a negative or non-finite weight.", nameof(tileWeights));
            }
        }

        var frozenWeights = tileWeights
            .Select(w => (IReadOnlyDictionary<TileKind, double>)new Dictionary<TileKind, double>(w))
            .ToArray();

        var forkCounts = forksPerStage?.ToArray() ??
            Enumerable.Repeat(ForkCountRange.Authored, 3).ToArray();

        return new ChapterBoardConfig(
            chapterId, stageLengths.ToArray(), eliteCount.ToArray(), frozenWeights, forkCounts, bossId,
            forkBiasPlusMultiplier, forkBiasMinusMultiplier);
    }
}
