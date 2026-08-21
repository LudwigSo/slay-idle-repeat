using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Tests.Handlers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// Which elite a stage's mini-boss IS — at the reader that decides it, and at the fight that has to
/// actually use the reader's answer.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Driven against two content sets that differ in <b>nothing but</b> <c>miniBossIds</c>, and
/// built from one factory so that stays true. A reader that answered from the chapter's enemy pool
/// instead — the other place elite ids live — would hand back the same id for both, so the pair is
/// what makes this about the authored member rather than about the chapter.
/// </para>
/// <para>
/// 🔴 <b>The reader cases are not enough on their own, and that was measured rather than guessed.</b>
/// <c>SimulationResult</c> and <c>CombatEvent</c> name actors positionally, so no test can read an
/// enemy's id off a fight — which is why the identity claim was first stated at the reader alone.
/// Neutralising the parameter that carries the reader's answer into the encounter then left the
/// whole suite green: the reader was pinned and the wire from it into the fight was not, so the
/// plumbing could have been deleted outright with nothing to show it. <c>LogHash</c> is the observable
/// that closes it — a different elite is a different archetype, so it is a different replay.
/// </para>
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
    /// The wire, not the reader: a real mini-boss fight, composed twice over the shipped content set
    /// and over a copy of it whose only difference is a swapped <c>miniBossIds</c>, is two different
    /// replays — and the same pair of sets leaves an ordinary Elite fight untouched.
    /// </summary>
    /// <remarks>
    /// The Elite half is the control, and it carries the weight here: it proves the two sets are
    /// otherwise identical, so the mini-boss's moved hash is attributable to the authored id and not
    /// to some other difference the copy introduced. Without it, a helper that accidentally rebuilt
    /// the content set differently would move BOTH hashes and this case would still pass.
    /// </remarks>
    [Fact]
    public void A_mini_boss_fight_is_composed_over_the_id_its_chapter_authored()
    {
        var authored = TileWorlds.Context.Content;
        var swapped = WithMiniBossIdsReversed(authored);

        // The premise, in two halves: the sets name different elites at stage 1's gate, and those
        // two elites derive from different archetypes — two ids sharing an archetype would fight
        // identically and this case would be unfalsifiable.
        ChapterBoardTuning.MiniBossId(authored, Chapter, stage: 1).ShouldBe("EL_THORN_SENTINEL");
        ChapterBoardTuning.MiniBossId(swapped, Chapter, stage: 1).ShouldBe("EL_MOSSBACK_ALPHA");
        Archetype(authored, "EL_THORN_SENTINEL").ShouldNotBe(Archetype(authored, "EL_MOSSBACK_ALPHA"));

        Replay(TileKind.MiniBoss, authored).ShouldNotBe(
            Replay(TileKind.MiniBoss, swapped),
            "the same run, the same seed, the same everything but which elite the chapter names at " +
            "stage 1's gate — and the fight came out identical. The authored id is being read and " +
            "then not used: the encounter drew from the chapter's own elite pool instead.");

        Replay(TileKind.Elite, authored).ShouldBe(
            Replay(TileKind.Elite, swapped),
            "an ordinary Elite draws from the chapter pool, which these two sets share, so its " +
            "fight must not move. If it did, the two sets differ somewhere beyond miniBossIds and " +
            "the case above proves nothing.");
    }

    /// <summary>One fight of <paramref name="kind"/>, on stage 1, as its replay hash.</summary>
    private static ulong Replay(TileKind kind, ContentSnapshot content)
    {
        var slice = TileWorlds.OnTile(kind, phase: RunPhase.BattlePending);

        return RunBattle.Simulate(slice.Player.ToSnapshot(), slice.Run!.ToSnapshot(), content).LogHash;
    }

    private static string Archetype(ContentSnapshot content, string eliteId) =>
        content.ReadText(
            $"content/enemies/enemies.json#/elites/identities/{IdentityIndex(content, eliteId)}/baseArchetype");

    private static int IdentityIndex(ContentSnapshot content, string eliteId)
    {
        var identities = content.Read("content/enemies/enemies.json#/elites/identities");

        for (var i = 0; i < identities.Items.Count; i++)
        {
            identities.Items[i].TryGetMember("id", out var id);

            if (id!.AsText("id") == eliteId)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"'{eliteId}' has no identity row to read an archetype off.");
    }

    /// <summary>
    /// The same content set with chapter 1's <c>miniBossIds</c> reversed and nothing else touched —
    /// every other document carried over by reference, and the version kept, so "differ in nothing
    /// but" is a property of this helper rather than a claim about it.
    /// </summary>
    private static ContentSnapshot WithMiniBossIdsReversed(ContentSnapshot content)
    {
        // First, not Single, and it mirrors ChapterBoardTuning.FindChapterDocument deliberately: it
        // scans DocumentPaths in order and takes the first document declaring the chapter's id. The
        // merged fixture set carries more than one that does, so picking a different one here would
        // edit a document the reader never looks at and the case would fail for the wrong reason.
        var path = content.DocumentPaths.First(
            candidate => candidate.StartsWith("content/chapters/", StringComparison.Ordinal) &&
                         content.ReadInt32($"{candidate}#/id") == Chapter);

        var root = content.GetDocument(path).Root;
        var members = new Dictionary<string, ContentValue>(StringComparer.Ordinal);

        foreach (var name in root.MemberNames)
        {
            root.TryGetMember(name, out var value);
            members[name] = value!;
        }

        members["miniBossIds"] = ContentValue.Array(members["miniBossIds"].Items.Reverse());

        return new ContentSnapshot(
            content.Version,
            content.DocumentPaths.Select(
                candidate => candidate == path
                    ? new ContentDocument(path, ContentValue.Object(members))
                    : content.GetDocument(candidate)));
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
