using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The chapter clear gate is authored <b>twice</b>, and only one of the two is read: every chapter
/// document carries an <c>unlockCondition</c>, and <c>tuning/progression.json#/chapterGating</c>
/// states the same ladder generically. This case pins that the two agree.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Nothing in this repository reads <c>unlockCondition</c> at runtime. The one screen that gates
/// chapters decides from <c>#/chapterGating</c> alone, so the two sources agree today only because
/// the two shipped chapters were authored by hand to agree. <c>schema/chapter.schema.json</c>
/// permits <c>clearChapter</c> 1–8 at <em>any</em> tier — an <c>unlockCondition</c> the generic
/// ladder cannot express — and a chapter authored with one would be gated by the ladder and
/// <em>opened anyway</em>, with the document it disagreed with sitting unread beside it.
/// </para>
/// <para>
/// 🔒 <b>This case pins agreement; it does not decide the content model.</b> Whether a chapter's
/// own <c>unlockCondition</c> <em>replaces</em> the generic rung or merely <em>adds</em> to it is a
/// content-model decision no task owns, and choosing one here would be inventing it. What this
/// buys is that the divergence becomes loud instead of silent: whoever authors the first chapter
/// the ladder cannot describe is told, at the point of authoring, that they have written a rule
/// nobody reads.
/// </para>
/// <para>
/// The expectation is derived from the <b>authored ladder</b> rather than restated as a rule in
/// C#: the rung's <c>requiresClear</c> token is read first, and only the shape
/// <c>PREVIOUS_CHAPTER_NORMAL</c> implies is asserted. A different token means this case is
/// describing a ladder that no longer exists, and it says so and goes red rather than quietly
/// asserting nothing.
/// </para>
/// </remarks>
public sealed class ChapterUnlockConditionAgreementTests
{
    /// <summary>Where the chapter documents sit — the subject set, discovered rather than listed.</summary>
    private const string ChaptersDirectory = "content/chapters/";

    /// <summary>🔒 The floor's named members. See the case's own remarks for why a count would not do.</summary>
    private const string ChapterOne = "content/chapters/CH_01_GREENWOOD_VALE.json";

    private const string ChapterTwo = "content/chapters/CH_02_ASHEN_MIRE.json";

    /// <summary>The rung the whole case is derived from.</summary>
    private const string NormalRung = "tuning/progression.json#/chapterGating/NORMAL";

    /// <summary>The one token this case knows how to translate into an <c>unlockCondition</c>.</summary>
    private const string PreviousChapterNormal = "PREVIOUS_CHAPTER_NORMAL";

    /// <summary>The tier that token names — the same spelling the chapter schema's enum permits.</summary>
    private const string NormalTier = "NORMAL";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    /// <summary>
    /// `10` §7 — every authored chapter's own <c>unlockCondition</c> says exactly what the Normal
    /// rung of <c>#/chapterGating</c> implies for it: nothing for chapter 1, and clearing the
    /// preceding chapter on Normal for every chapter after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The floor is by NAMED MEMBER, and it has to be.</b> This case quantifies over "every
    /// document under <c>content/chapters/</c>", a set that is discovered from the loaded snapshot
    /// and can silently become empty — the directory is renamed, the files move under a chapter
    /// pack, the loader stops surfacing them. An empty set satisfies a <c>foreach</c> perfectly,
    /// so the two chapters that exist today are named. A count would not do either: "at least two
    /// documents" is satisfied by two chapters neither of which is the one that used to carry the
    /// <c>null</c>.
    /// </para>
    /// <para>
    /// Naming both is deliberate rather than tidy. Chapter 1 is the only subject that exercises the
    /// <c>null</c> arm and chapter 2 the only one that exercises the pair arm, so losing either
    /// leaves half of this case asserted over nothing while the other half keeps it green.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_authored_chapters_unlock_condition_agrees_with_the_ladders_normal_rung()
    {
        var data = Data();
        var chapters = AuthoredChapters(data);
        var documents = chapters.Select(chapter => chapter.Document).ToArray();

        documents.ShouldContain(
            ChapterOne,
            $"'{ChapterOne}' is not among the documents under {ChaptersDirectory}, so the loop below is " +
            "quantifying over a set that has lost the one chapter carrying the null unlockCondition — and " +
            "a set that loses every chapter passes this case outright. Point the discovery at wherever the " +
            "chapter documents went rather than leaving it green.");
        documents.ShouldContain(
            ChapterTwo,
            $"'{ChapterTwo}' is not among the documents under {ChaptersDirectory}. It is the only chapter " +
            "that authors an unlockCondition at all, so without it this case checks that one file says " +
            "null and nothing else — which is true of a checkout with no gate authored anywhere.");

        data.ReadText($"{NormalRung}/requiresClear").ShouldBe(
            PreviousChapterNormal,
            $"the Normal rung no longer says {PreviousChapterNormal}, and this case only knows how to " +
            "translate that one token into a per-chapter unlockCondition. Every assertion below now " +
            "describes a ladder the tuning no longer authors, so it is reported here rather than left to " +
            "pass against the wrong rule: decide what the new token implies for a chapter document, and " +
            "restate the expectation — do not delete the case.");

        foreach (var (document, id) in chapters)
        {
            var condition = data.Read($"{document}#/unlockCondition");

            if (id == 1)
            {
                condition.Kind.ShouldBe(
                    ContentValueKind.Unauthorised,
                    $"{document} is chapter 1, and {PreviousChapterNormal} names no chapter before it — so " +
                    "the ladder demands nothing and the document must author null. An unlockCondition here " +
                    "is a prerequisite for the game's first chapter that the screen gating it will never " +
                    "read, which is a chapter locked in the data and open on screen.");
                continue;
            }

            condition.Kind.ShouldBe(
                ContentValueKind.Object,
                $"{document} authors no unlockCondition, but {PreviousChapterNormal} requires clearing " +
                $"chapter {id - 1} before it. A null here reads as 'no prerequisite' in the one file that " +
                "states the chapter's own rule, while the ladder keeps gating it — the two sources of the " +
                "same gate disagreeing, which is exactly what this case exists to make loud.");

            data.ReadInt32($"{document}#/unlockCondition/clearChapter").ShouldBe(
                id - 1,
                $"{document} is chapter {id}, and {PreviousChapterNormal} means the chapter immediately " +
                "before it. A different chapter id here is a prerequisite the ladder cannot express, so the " +
                "screen would gate on the previous chapter and open this one regardless of what its own " +
                "document asked for.");
            data.ReadText($"{document}#/unlockCondition/tier").ShouldBe(
                NormalTier,
                $"{document}'s prerequisite is a clear at a tier the ladder never mentions. " +
                $"{PreviousChapterNormal} is the previous chapter on {NormalTier}; the chapter schema " +
                "permits HEROIC and MYTHIC here, and either would be a harder gate than the one actually " +
                "enforced — authored, unread, and silently ignored.");
        }
    }

    /// <summary>
    /// Every chapter document in the loaded set, paired with the id it carries, ordered by path.
    /// </summary>
    /// <remarks>
    /// The id is read out of the document rather than parsed out of the file name: the name is a
    /// convention and the field is the data, and a case about two sources agreeing should not
    /// introduce a third.
    /// </remarks>
    private static IReadOnlyList<(string Document, int Id)> AuthoredChapters(ContentSnapshot data) =>
        data.DocumentPaths
            .Where(path => path.StartsWith(ChaptersDirectory, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => (Document: path, Id: data.ReadInt32($"{path}#/id")))
            .ToArray();
}
