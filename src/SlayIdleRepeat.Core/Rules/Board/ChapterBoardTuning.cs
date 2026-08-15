using System.Globalization;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// 🔒 `14` §6 / `03` §3 — reads one chapter's <c>content/chapters/CH_*.json</c> document into the
/// <see cref="ChapterBoardConfig"/> <see cref="BoardGenerator.GenerateBoard"/> needs. The seam
/// M3-02's movement engine calls to turn a <c>Run.ChapterId</c> into the config a board is
/// generated from.
/// </summary>
/// <remarks>
/// Lives under <c>Rules/Board/</c>, not <c>Content/</c>: `30` §11.4 puts <c>Content</c> below
/// <c>Rules</c> in the layering, and this reader's return type — <see cref="ChapterBoardConfig"/> —
/// is itself a <c>Rules.Board</c> type, so a reader that produced it could never live one layer
/// lower. <see cref="Combat.Enemies.EnemyCatalogue"/> and <see cref="Stats.PowerCalculator"/> read
/// <see cref="ContentSnapshot"/> from <c>Rules</c> for the identical reason.
/// </remarks>
internal static class ChapterBoardTuning
{
    /// <summary>`14` §6 — every chapter document lives here, one file per chapter.</summary>
    private const string ChaptersDirectory = "content/chapters/";

    /// <summary>`03` §1 — every chapter fixes exactly 3 stages.</summary>
    private const int StageCount = 3;

    /// <summary>
    /// `03` §3.1 — the fork-bias multipliers, global rather than per-chapter (the document's table
    /// is one table, not one per chapter). M3-01's judgment-call magnitude, authored as content per
    /// the milestone-review architecture pass rather than a Core constant.
    /// </summary>
    private const string BoardGenerationPointer = "tuning/currencies.json#/boardGeneration";

    /// <summary>
    /// Reads chapter <paramref name="chapterId"/>'s board-relevant content.
    /// </summary>
    /// <param name="content">The loaded content set.</param>
    /// <param name="chapterId">`14` §6's chapter number.</param>
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
        var bossId = content.ReadText($"{documentPath}#/bossId");
        var forkBiasPlus = content.ReadDouble($"{BoardGenerationPointer}/forkBiasPlusMultiplier");
        var forkBiasMinus = content.ReadDouble($"{BoardGenerationPointer}/forkBiasMinusMultiplier");

        return ChapterBoardConfig.From(
            chapterId, stageLengths, eliteCount, tileWeights, bossId, forkBiasPlus, forkBiasMinus);
    }

    /// <summary>
    /// Finds the <c>content/chapters/</c> document whose <c>id</c> equals <paramref name="chapterId"/>.
    /// </summary>
    /// <remarks>
    /// A scan, not a naming convention: `14` §6's files are named after the chapter's biome
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
