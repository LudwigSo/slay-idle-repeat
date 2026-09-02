using System.Globalization;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// Reads one chapter's <c>content/chapters/CH_*.json</c> document: the
/// <see cref="ChapterBoardConfig"/> <see cref="BoardGenerator.GenerateBoard"/> needs, and the
/// identity of the mini-boss standing at each of the chapter's first two stage gates.
/// </summary>
/// <remarks>
/// Lives under <c>Rules/Board/</c>, not <c>Content/</c>: <c>Content</c> sits below <c>Rules</c> in
/// the layering, and this reader's return type — <see cref="ChapterBoardConfig"/> — is itself a
/// <c>Rules.Board</c> type, so a reader that produced it could never live one layer lower.
/// <see cref="Combat.Enemies.EnemyCatalogue"/> and <see cref="Stats.PowerCalculator"/> read
/// <see cref="ContentSnapshot"/> from <c>Rules</c> for the identical reason.
/// </remarks>
internal static class ChapterBoardTuning
{
    private const string ChaptersDirectory = "content/chapters/";

    /// <summary>Every chapter fixes exactly 3 stages.</summary>
    private const int StageCount = 3;

    /// <summary>The fork-bias multipliers, global rather than per-chapter — one table, not one per chapter.</summary>
    private const string BoardGenerationPointer = "tuning/currencies.json#/boardGeneration";

    /// <summary>
    /// Reads chapter <paramref name="chapterId"/>'s board-relevant content.
    /// </summary>
    /// <param name="content">The loaded content set.</param>
    /// <param name="chapterId">The chapter number.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">
    /// No document under <see cref="ChaptersDirectory"/> declares <c>id == chapterId</c>.
    /// </exception>
    internal static ChapterBoardConfig Read(ContentSnapshot content, int chapterId)
    {
        ArgumentNullException.ThrowIfNull(content);

        var documentPath = FindChapterDocument(content, chapterId);

        var stageLengths = ReadIntArray(content, documentPath, "stageLengths");
        var eliteCount = ReadIntArray(content, documentPath, "eliteCount");
        var tileWeights = ReadTileWeights(content, documentPath);
        var forksPerStage = ReadForksPerStage(content, documentPath);
        var bossId = content.ReadText($"{documentPath}#/bossId");
        var forkBiasPlus = content.ReadDouble($"{BoardGenerationPointer}/forkBiasPlusMultiplier");
        var forkBiasMinus = content.ReadDouble($"{BoardGenerationPointer}/forkBiasMinusMultiplier");

        return ChapterBoardConfig.From(
            chapterId, stageLengths, eliteCount, tileWeights, bossId, forksPerStage,
            forkBiasPlus, forkBiasMinus);
    }

    /// <summary>
    /// The elite a mini-boss fight at <paramref name="stage"/>'s gate is: the chapter's own
    /// <c>miniBossIds</c> entry for that stage.
    /// </summary>
    /// <remarks>
    /// Read from the CHAPTER document rather than from <c>content/enemies/enemies.json</c>'s chapter
    /// pool, for the reason <c>bossId</c> is: which enemy a chapter's structure puts a player in
    /// front of, and at which node, is a fact about the chapter's own run — the enemy catalogue
    /// states what an elite IS, not where the run schedules one. The two stay in agreement because
    /// each id here has to be a member of the same chapter's own <c>elitePool</c>.
    /// </remarks>
    /// <param name="content">The loaded content set.</param>
    /// <param name="chapterId">The chapter number.</param>
    /// <param name="stage">The stage whose gate the fight stands at — 1 or 2.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="stage"/> is not 1 or 2 — stage 3 ends on the boss node, not a gate.
    /// </exception>
    /// <exception cref="MissingContentException">
    /// The chapter has no document, or that document names no mini-boss for the stage.
    /// </exception>
    internal static string MiniBossId(ContentSnapshot content, int chapterId, int stage)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (stage is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage), stage,
                "A mini-boss stands on the last node of stage 1 and of stage 2. Stage 3's last node " +
                "is followed by the boss, so it carries no gate of its own.");
        }

        var documentPath = FindChapterDocument(content, chapterId);
        var index = (stage - 1).ToString(CultureInfo.InvariantCulture);

        return content.ReadText($"{documentPath}#/miniBossIds/{index}");
    }

    /// <summary>
    /// Finds the <c>content/chapters/</c> document whose <c>id</c> equals <paramref name="chapterId"/>.
    /// </summary>
    /// <remarks>
    /// A scan, not a naming convention: files are named after the chapter's biome
    /// (<c>CH_01_GREENWOOD_VALE.json</c>), so the chapter number cannot be recovered from the path
    /// alone — only from the document's own <c>id</c> field.
    /// </remarks>
    private static string FindChapterDocument(ContentSnapshot content, int chapterId)
    {
        foreach (var path in content.DocumentPaths)
        {
            if (!path.StartsWith(ChaptersDirectory, StringComparison.Ordinal))
            {
                continue;
            }

            if (content.ReadInt32($"{path}#/id") == chapterId)
            {
                return path;
            }
        }

        throw new MissingContentException(
            $"{ChaptersDirectory}(id={chapterId.ToString(CultureInfo.InvariantCulture)})",
            $"no document under {ChaptersDirectory} declares id {chapterId.ToString(CultureInfo.InvariantCulture)}");
    }

    private static IReadOnlyList<int> ReadIntArray(ContentSnapshot content, string documentPath, string member)
    {
        var array = content.Read($"{documentPath}#/{member}");
        var values = new int[array.Items.Count];

        for (var i = 0; i < array.Items.Count; i++)
        {
            values[i] = array.Items[i].AsInt32($"{documentPath}#/{member}/{i.ToString(CultureInfo.InvariantCulture)}");
        }

        return values;
    }

    /// <summary>
    /// Reads the chapter's <c>forksPerStage</c>: one <c>{ min, max }</c> object per stage.
    /// </summary>
    /// <remarks>
    /// Required rather than optional-with-a-fallback. A chapter that omitted it would generate a
    /// board whose fork density came from a default nobody authored, and the point of moving the
    /// number out of <see cref="BoardGenerator"/> was that a long stage's fork count is a chapter's
    /// decision. <see cref="ContentSnapshot.Read"/> raises <c>MissingContentException</c> for the
    /// absent pointer, which is the loud failure this wants.
    /// </remarks>
    private static IReadOnlyList<ForkCountRange> ReadForksPerStage(
        ContentSnapshot content, string documentPath)
    {
        var array = content.Read($"{documentPath}#/forksPerStage");

        if (array.Items.Count != StageCount)
        {
            throw new MissingContentException(
                $"{documentPath}#/forksPerStage",
                $"a chapter states one fork-count range per stage, so forksPerStage needs " +
                $"{StageCount.ToString(CultureInfo.InvariantCulture)} entries; this one has " +
                $"{array.Items.Count.ToString(CultureInfo.InvariantCulture)}");
        }

        var ranges = new ForkCountRange[StageCount];

        for (var i = 0; i < StageCount; i++)
        {
            var pointer = $"{documentPath}#/forksPerStage/{i.ToString(CultureInfo.InvariantCulture)}";

            ranges[i] = ForkCountRange.Of(
                content.ReadInt32($"{pointer}/min"),
                content.ReadInt32($"{pointer}/max"),
                pointer);
        }

        return ranges;
    }

    private static IReadOnlyList<IReadOnlyDictionary<TileKind, double>> ReadTileWeights(
        ContentSnapshot content, string documentPath)
    {
        var array = content.Read($"{documentPath}#/tileWeights");

        if (array.Items.Count != StageCount)
        {
            throw new InvalidTunableException(
                $"{documentPath}#/tileWeights",
                $"has {array.Items.Count.ToString(CultureInfo.InvariantCulture)} entries; 03 §1 fixes " +
                "exactly 3 stages, one weight table each.");
        }

        var stages = new IReadOnlyDictionary<TileKind, double>[StageCount];

        for (var i = 0; i < StageCount; i++)
        {
            var stagePointer = $"{documentPath}#/tileWeights/{i.ToString(CultureInfo.InvariantCulture)}";
            var stageObject = array.Items[i];
            var weights = new Dictionary<TileKind, double>(stageObject.MemberNames.Count);

            foreach (var tileId in stageObject.MemberNames)
            {
                stageObject.TryGetMember(tileId, out var weight);
                var kind = TileKindIds.TryParse(tileId, out var parsed)
                    ? parsed
                    : throw new ContentTypeMismatchException(
                        $"{stagePointer}/{tileId}", ContentValueKind.Text,
                        $"one of 03 §2's TILE_* ids ('{tileId}' is not).");

                weights[kind] = weight!.AsDouble($"{stagePointer}/{tileId}");
            }

            stages[i] = weights;
        }

        return stages;
    }
}
