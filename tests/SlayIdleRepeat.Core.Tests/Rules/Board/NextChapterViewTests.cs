using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// <c>NextChapterView</c> — the chapter the campaign offers next and the power it is balanced
/// against, and the only sanctioned route out of <c>Core</c> for the par table.
/// </summary>
/// <remarks>
/// 🔒 <b>The load-bearing claim is that the recommendation is READ.</b>
/// <c>tuning/par_power.json</c> owns those numbers and <see cref="ParPowerTuning"/> is
/// <c>internal</c>, so the alternative to this projection was a transcription in the client that no
/// reader would ever see move — which is why
/// <see cref="The_recommendation_moves_with_the_par_table"/> varies the document rather than the row,
/// and why nothing here compares against a literal the fixture does not also read.
/// </remarks>
public sealed class NextChapterViewTests
{
    /// <summary>The shipped content set — the chapter documents and the par table together.</summary>
    private static ContentSnapshot Shipped => ShippedHarness.Content;

    /// <summary>Chapter 1's authored Normal par, read straight from the document.</summary>
    /// <remarks>
    /// By raw pointer rather than through <c>ParPowerTuning</c>: an expectation computed by the
    /// reader under test proves only that the reader agrees with itself.
    /// </remarks>
    private static double ParOf(int rowIndex) =>
        Shipped.ReadDouble("tuning/par_power.json#/parPower/" + rowIndex + "/NORMAL");

    // --------------------------------------------------------------------- which chapter

    /// <summary>A profile that has cleared nothing is pointed at the campaign's first chapter.</summary>
    [Fact]
    public void A_profile_that_has_cleared_nothing_is_pointed_at_the_first_chapter()
    {
        var view = NextChapterView.Project(Row(), Shipped).ShouldNotBeNull();

        view.ChapterId.ShouldBe(1);
        view.RecommendedPower.ShouldBe(
            ParOf(0), "the recommendation is chapter 1's own cell in the par table.");
    }

    /// <summary>
    /// 🔒 …and a profile that has cleared chapter 1 on Normal is pointed at the next one, with the
    /// next one's par.
    /// </summary>
    /// <remarks>
    /// The discriminating case, and it discriminates twice: a projection that ignored the clear
    /// history answers chapter 1 forever, and one that read the wrong par row answers 140 for a
    /// chapter balanced at 2 000. The two pars are asserted to differ first, so the comparison is
    /// evidence rather than two equal numbers agreeing.
    /// </remarks>
    [Fact]
    public void A_cleared_chapter_moves_the_offer_and_the_recommendation_with_it()
    {
        ParOf(1).ShouldNotBe(
            ParOf(0), "the fixture only discriminates while the two chapters are balanced differently.");

        var view = NextChapterView.Project(Cleared(1, DifficultyTier.NORMAL), Shipped).ShouldNotBeNull();

        view.ChapterId.ShouldBe(2, "chapter 1 is behind them, so chapter 2 is what is new.");
        view.RecommendedPower.ShouldBe(ParOf(1));
    }

    /// <summary>
    /// 🔒 A clear at another tier is not a Normal clear, and does not move the offer.
    /// </summary>
    /// <remarks>
    /// The negative control on the key: the ladder opens a chapter's Normal rung on the PREVIOUS
    /// chapter's NORMAL clear, so a projection matching on the chapter alone would push a player who
    /// has only ever cleared Heroic past a chapter they can still legitimately be offered — and a
    /// Heroic clear of chapter 1 implies its Normal clear in the shipped ladder but not in the row,
    /// which is the whole difference between reading the history and guessing at it.
    /// </remarks>
    [Fact]
    public void A_clear_at_another_tier_does_not_move_the_offer()
    {
        var view = NextChapterView.Project(Cleared(1, DifficultyTier.HEROIC), Shipped).ShouldNotBeNull();

        view.ChapterId.ShouldBe(
            1, "the row records a Heroic clear and the offer is keyed on the Normal one.");
    }

    /// <summary>
    /// ⚠️ A profile that has cleared every authored chapter is pointed at nothing, rather than at a
    /// chapter nobody has written.
    /// </summary>
    /// <remarks>
    /// Steering S6: the campaign runs out before the par table does — <c>par_power.json</c> authors
    /// eight chapters and <c>content/chapters/</c> holds two — so extrapolating here would offer a
    /// run at a chapter with no board, no enemies and no boss. Absent and greppable instead.
    /// </remarks>
    [Fact]
    public void A_profile_that_has_cleared_everything_authored_is_pointed_at_nothing()
    {
        var chapters = AuthoredChapters();

        chapters.Count.ShouldBeGreaterThan(
            0, "a floor: with no authored chapter this case would prove nothing at all.");

        var everything = PlayerSnapshots.With(
            clearedChapterTiers: PlayerSnapshots.Counters(
                chapters
                    .Select(id => (PlayerAggregate.ChapterTierKey(id, DifficultyTier.NORMAL), 1L))
                    .ToArray()));

        NextChapterView.Project(everything, Shipped).ShouldBeNull(
            "every authored chapter is behind this profile, and the next one is not written yet.");
    }

    /// <summary>
    /// ⚠️ A chapter the par table authors no cell for is pointed at nothing, rather than at a
    /// recommendation extrapolated from its neighbours.
    /// </summary>
    /// <remarks>
    /// 🔴 The branch no shipped data reaches: <c>par_power.json</c> authors all eight chapters and
    /// <c>content/chapters/</c> holds two, so today every offerable chapter has a cell. Steering S25
    /// says the deliverable for a rule whose triggering state no shipped content reaches is the
    /// fixture that constructs it anyway — this one drops chapter 1's row from the table and leaves
    /// everything else alone.
    /// </remarks>
    [Fact]
    public void A_chapter_the_par_table_does_not_author_is_pointed_at_nothing()
    {
        NextChapterView.Project(Row(), WithoutChapterOnePar).ShouldBeNull(
            "the table authors no cell for the chapter this profile is up to, and inventing one " +
            "would state the balance point of a chapter nobody has tuned.");
    }

    // ------------------------------------------------------------------- which number

    /// <summary>
    /// 🔒 The recommendation is <c>par_power.json</c>'s, not a number in the projection.
    /// </summary>
    /// <remarks>
    /// One row, two documents: the retuned table balances chapter 1 at a figure the shipped one does
    /// not, and a projection carrying its own copy answers the shipped 140 to both.
    /// </remarks>
    [Fact]
    public void The_recommendation_moves_with_the_par_table()
    {
        var retuned = NextChapterView.Project(Row(), RetunedPars).ShouldNotBeNull();

        retuned.RecommendedPower.ShouldBe(RetunedChapterOnePar);
        RetunedChapterOnePar.ShouldNotBe(
            ParOf(0), "the fixture only discriminates while the two documents disagree.");
    }

    // -------------------------------------------------------------------------- fixtures

    /// <summary>A chapter-1 par nothing ships, so a transcribed table disagrees with it.</summary>
    private const double RetunedChapterOnePar = 333d;

    /// <summary>The shipped set with chapter 1's par cell — and only that cell — moved.</summary>
    private static readonly ContentSnapshot RetunedPars = WithChapterOnePar(RetunedChapterOnePar);

    /// <summary>The shipped set with chapter 1's par ROW removed, leaving the other seven.</summary>
    private static readonly ContentSnapshot WithoutChapterOnePar = WithParRowsExcept(chapterId: 1);

    /// <summary>Where the par table lives.</summary>
    private const string ParDocumentPath = "tuning/par_power.json";

    /// <summary>The shipped set with one chapter's Normal cell moved and nothing else touched.</summary>
    private static ContentSnapshot WithChapterOnePar(double par)
    {
        var root = Shipped.GetDocument(ParDocumentPath).Root;

        var replaced = Member(root, "parPower").Items
            .Select((row, index) => index == 0 ? RowWithNormal(row, par) : row)
            .ToArray();

        return WithParTable(root, ContentValue.Array(replaced));
    }

    /// <summary>The shipped set with one chapter's par row dropped from the table.</summary>
    private static ContentSnapshot WithParRowsExcept(int chapterId)
    {
        var root = Shipped.GetDocument(ParDocumentPath).Root;

        var kept = Member(root, "parPower").Items
            .Where(row => Member(row, "chapter").AsInt32(ParDocumentPath) != chapterId)
            .ToArray();

        return WithParTable(root, ContentValue.Array(kept));
    }

    /// <summary>The shipped set with the par document's <c>parPower</c> member replaced.</summary>
    private static ContentSnapshot WithParTable(ContentValue root, ContentValue table)
    {
        var members = root.MemberNames.ToDictionary(
            name => name,
            name => string.Equals(name, "parPower", StringComparison.Ordinal)
                ? table
                : Member(root, name),
            StringComparer.Ordinal);

        var documents = Shipped.DocumentPaths
            .Where(path => !string.Equals(path, ParDocumentPath, StringComparison.Ordinal))
            .Select(Shipped.GetDocument)
            .Append(new ContentDocument(ParDocumentPath, ContentValue.Object(members)))
            .ToArray();

        return new ContentSnapshot(Shipped.Version, documents);
    }

    /// <summary>One member of an object, which the fixture knows is there.</summary>
    private static ContentValue Member(ContentValue value, string name) =>
        value.TryGetMember(name, out var member)
            ? member!
            : throw new InvalidOperationException("the fixture document has no member '" + name + "'.");

    /// <summary>One par row with its Normal cell replaced.</summary>
    private static ContentValue RowWithNormal(ContentValue row, double par) =>
        ContentValue.Object(
            row.MemberNames.ToDictionary(
                name => name,
                name => string.Equals(name, "NORMAL", StringComparison.Ordinal)
                    ? ContentValue.Number((decimal)par)
                    : Member(row, name),
                StringComparer.Ordinal));

    /// <summary>Every chapter the shipped set authors a document for.</summary>
    private static IReadOnlyList<int> AuthoredChapters() =>
        Shipped.DocumentPaths
            .Where(path => path.StartsWith("content/chapters/", StringComparison.Ordinal))
            .Select(path => Shipped.ReadInt32(path + "#/id"))
            .Order()
            .ToArray();

    private static PlayerSnapshot Row() => PlayerSnapshots.Valid;

    private static PlayerSnapshot Cleared(int chapter, DifficultyTier tier) =>
        PlayerSnapshots.With(
            clearedChapterTiers: PlayerSnapshots.Counters(
                (PlayerAggregate.ChapterTierKey(chapter, tier), 1L)));
}
