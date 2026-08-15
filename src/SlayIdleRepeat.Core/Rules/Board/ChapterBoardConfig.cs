using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// The slice of a chapter's content (`game-data/schema/chapter.schema.json`) that
/// <see cref="BoardGenerator.GenerateBoard"/> needs — deliberately not the whole chapter
/// document: <c>Core</c> has zero dependencies, so nothing here does JSON parsing. A caller (the
/// Application-layer content loader) reads the chapter file and builds one of these.
/// </summary>
public sealed class ChapterBoardConfig
{
    private ChapterBoardConfig(
        int chapterId,
        IReadOnlyList<int> stageLengths,
        IReadOnlyList<int> eliteCount,
        IReadOnlyList<IReadOnlyDictionary<TileKind, double>> tileWeights,
        string bossId)
    {
        ChapterId = chapterId;
        StageLengths = stageLengths;
        EliteCount = eliteCount;
        TileWeights = tileWeights;
        BossId = bossId;
    }

    /// <summary>The chapter number, `14` §6.</summary>
    public int ChapterId { get; }

    /// <summary>`03` §1 — the three stage lengths, e.g. <c>[12, 14, 16]</c>.</summary>
    public IReadOnlyList<int> StageLengths { get; }

    /// <summary>One elite count per stage, matching <see cref="StageLengths"/>.</summary>
    public IReadOnlyList<int> EliteCount { get; }

    /// <summary>
    /// One tile-kind weight table per stage, matching <see cref="StageLengths"/>. Exactly as
    /// authored — `03` §3's weighted draw consumes it verbatim, with no kind excluded, so a
    /// content author who wants a mandatory-placement kind (e.g. <see cref="TileKind.Shop"/>) to
    /// also be reachable by the weighted draw states it here and gets exactly that.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<TileKind, double>> TileWeights { get; }

    /// <summary>The chapter's boss identity — carried through onto the boss <see cref="BoardNode"/> but not otherwise interpreted here.</summary>
    public string BossId { get; }

    /// <summary>Builds a config, validating the shape `03` §3's generator needs.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="stageLengths"/> or <paramref name="eliteCount"/> or <paramref name="tileWeights"/>
    /// does not have exactly 3 entries (`03` §1's fixed 3-stage shape); a stage length is not
    /// positive; an elite count is negative or exceeds its stage length; a stage's weight table is
    /// empty, states <see cref="TileKind.Boss"/> (never drawable — the boss is placed once, by
    /// <see cref="BoardGenerator"/> itself, never by weighted draw), or has no positive weight;
    /// or <paramref name="bossId"/> is empty.
    /// </exception>
    public static ChapterBoardConfig From(
        int chapterId,
        IReadOnlyList<int> stageLengths,
        IReadOnlyList<int> eliteCount,
        IReadOnlyList<IReadOnlyDictionary<TileKind, double>> tileWeights,
        string bossId)
    {
        ArgumentNullException.ThrowIfNull(stageLengths);
        ArgumentNullException.ThrowIfNull(eliteCount);
        ArgumentNullException.ThrowIfNull(tileWeights);
        ArgumentNullException.ThrowIfNull(bossId);

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

        return new ChapterBoardConfig(chapterId, stageLengths.ToArray(), eliteCount.ToArray(), frozenWeights, bossId);
    }
}
