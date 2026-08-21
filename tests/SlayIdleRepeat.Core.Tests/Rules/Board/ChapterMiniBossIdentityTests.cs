using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// Which elite a stage's mini-boss IS. The fight itself carries no enemy identity anywhere a test
/// can read — <c>SimulationResult</c> and <c>CombatEvent</c> both name actors positionally — so the
/// claim is stated where the identity is decided: the reader that answers it off the chapter's own
/// document.
/// </summary>
/// <remarks>
/// 🔒 Driven against two chapter documents that differ in <b>nothing but</b> <c>miniBossIds</c>, and
/// built from one factory so that stays true. A reader that answered from the chapter's enemy pool
/// instead — the other place elite ids live — would hand back the same id for both, so the pair is
/// what makes this about the authored member rather than about the chapter.
/// </remarks>
public sealed class ChapterMiniBossIdentityTests
{
    private const int Chapter = 1;
    private const string DocumentPath = "content/chapters/CH_01_TEST.json";

    [Fact]
    public void Each_stages_mini_boss_is_the_id_that_chapter_authored_for_it()
    {
        var greenwood = ChapterNaming("EL_THORN_SENTINEL", "EL_MOSSBACK_ALPHA");
        var swapped = ChapterNaming("EL_MOSSBACK_ALPHA", "EL_THORN_SENTINEL");

        ChapterBoardTuning.MiniBossId(greenwood, Chapter, stage: 1).ShouldBe("EL_THORN_SENTINEL");
        ChapterBoardTuning.MiniBossId(greenwood, Chapter, stage: 2).ShouldBe("EL_MOSSBACK_ALPHA");

        ChapterBoardTuning.MiniBossId(swapped, Chapter, stage: 1).ShouldBe(
            "EL_MOSSBACK_ALPHA",
            "the two documents differ in nothing but miniBossIds, so a stage's answer moving with " +
            "that member is the whole claim: the identity is authored, not drawn.");
        ChapterBoardTuning.MiniBossId(swapped, Chapter, stage: 2).ShouldBe("EL_THORN_SENTINEL");
    }

    /// <summary>Stage 3 ends on the boss node, so it has no gate and no id to answer with.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_stage_that_carries_no_gate_is_refused(int stage)
    {
        var content = ChapterNaming("EL_THORN_SENTINEL", "EL_MOSSBACK_ALPHA");

        Should.Throw<ArgumentOutOfRangeException>(
            () => ChapterBoardTuning.MiniBossId(content, Chapter, stage)).ParamName.ShouldBe("stage");
    }

    [Fact]
    public void A_chapter_with_no_document_is_a_missing_content_exception()
    {
        var content = ChapterNaming("EL_THORN_SENTINEL", "EL_MOSSBACK_ALPHA");

        Should.Throw<MissingContentException>(
            () => ChapterBoardTuning.MiniBossId(content, chapterId: 999, stage: 1));
    }

    /// <summary>
    /// A chapter document carrying only what this reader looks at: the id it is found by, and the
    /// two ids it answers with.
    /// </summary>
    private static ContentSnapshot ChapterNaming(string stageOne, string stageTwo) => new(
        ContentVersion.FromHex(new string('c', ContentVersion.HexLength)),
        [
            new ContentDocument(DocumentPath, ContentValue.Object(
                new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["id"] = ContentValue.Number(Chapter),
                    ["miniBossIds"] = ContentValue.Array(
                        [ContentValue.Text(stageOne), ContentValue.Text(stageTwo)]),
                })),
        ]);
}
