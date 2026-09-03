using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The two progress readings the Home screen shows: the furthest clear a profile records, and the
/// stage a continuable run stands in.
/// </summary>
public sealed class ChapterProgressReadoutTests
{
    private static readonly PlayerId Profile = new("PLAYER_progress_2e40");

    private static readonly RunId OpenRun = new("RUN_progress_5a17");

    private const string GreenwoodName = "FIXTURE Greenwood";

    private const string AshenName = "FIXTURE Ashen";

    private static readonly IReadOnlyList<ChapterDocument> TwoChapters =
        [new ChapterDocument(1, GreenwoodName), new ChapterDocument(2, AshenName)];

    // ------------------------------------------------------------------- highest clear

    [Fact]
    public void HighestClear_prefers_the_further_chapter_over_the_harder_tier()
    {
        var cleared = PlayerState.Cleared((1, DifficultyTier.MYTHIC), (2, DifficultyTier.NORMAL));

        ChapterProgressReadout.HighestClear(cleared, TwoChapters).ShouldBe(
            new HighestChapterClear(2, AshenName, DifficultyTier.NORMAL),
            "the tile answers 'how far', and chapter 2 is further than chapter 1 whatever tier " +
            "either was cleared at. An ordering by tier first names chapter 1 Mythic here.");
    }

    [Fact]
    public void HighestClear_prefers_the_higher_tier_within_one_chapter()
    {
        var cleared = PlayerState.Cleared((2, DifficultyTier.NORMAL), (2, DifficultyTier.HEROIC));

        ChapterProgressReadout.HighestClear(cleared, TwoChapters).ShouldNotBeNull().Tier.ShouldBe(
            DifficultyTier.HEROIC,
            "the same chapter twice is the same distance, so the tier breaks the tie — and a " +
            "presenter taking the first key it met would answer whichever the dictionary happened to " +
            "enumerate first.");
    }

    [Fact]
    public void HighestClear_drops_a_clear_of_a_chapter_the_content_does_not_author()
    {
        var cleared = PlayerState.Cleared((1, DifficultyTier.NORMAL), (9, DifficultyTier.MYTHIC));

        ChapterProgressReadout.HighestClear(cleared, TwoChapters).ShouldNotBeNull().ChapterId.ShouldBe(
            1,
            "chapter 9 has no document and so no name to show. Rather than a blank name or a thrown " +
            "lookup, the clear is skipped and the tile names the furthest chapter it can actually name.");
    }

    /// <summary>
    /// Each row is a key the store could hold but the format does not describe. Alone in the map,
    /// so that a reader that parsed any of them leniently has to produce a clear out of nothing.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1:")]
    [InlineData(":NORMAL")]
    [InlineData("1-NORMAL")]
    [InlineData("x:NORMAL")]
    [InlineData("1:LEGENDARY")]
    [InlineData("1:NORMAL:MYTHIC")]
    public void HighestClear_drops_a_key_that_does_not_parse(string key)
    {
        var cleared = new Dictionary<string, long>(StringComparer.Ordinal) { [key] = 1L };

        ChapterProgressReadout.HighestClear(cleared, TwoChapters).ShouldBeNull(
            $"'{key}' is not '{{chapter}}:{{TIER}}'. A reader that guessed at it would show a clear " +
            "the player never made, and a reader that threw would take the screen down over one row.");
    }

    [Fact]
    public void HighestClear_is_null_for_a_null_map() =>
        ChapterProgressReadout.HighestClear(clearedChapterTiers: null, TwoChapters).ShouldBeNull(
            "null is the row's own documented 'nothing cleared yet', and it is what a fresh profile holds.");

    [Fact]
    public void HighestClear_is_null_for_an_empty_map() =>
        ChapterProgressReadout.HighestClear(PlayerState.NothingCleared(), TwoChapters).ShouldBeNull(
            "an empty map is nothing cleared as well — the shape a row takes once something has " +
            "written to it and nothing has been recorded.");

    // -------------------------------------------------------------------- run progress

    [Fact]
    public void RunProgress_reads_stage_one_at_the_trailhead()
    {
        var run = Run(position: -1);

        ChapterProgressReadout.RunProgress(run, BootContent.Shipped, TwoChapters).ShouldNotBeNull().Stage.ShouldBe(
            1,
            "a run that has not rolled yet stands on no node and so in no stage — and the panel shows " +
            "it in the first of three rather than in a stage zero, or with no stage at all.");
    }

    [Fact]
    public void RunProgress_reads_the_stage_of_the_node_the_run_stands_on()
    {
        var run = Run(position: FirstSpineNodeInStage(2));

        ChapterProgressReadout.RunProgress(run, BootContent.Shipped, TwoChapters).ShouldNotBeNull().Stage.ShouldBe(
            2,
            "the node carries its stage and the panel shows that stage. A reader that always answered " +
            "1 passes the trailhead case and fails here.");
    }

    /// <summary>
    /// The boss belongs to no stage — the generator stamps it <c>BoardGraph.BossStage</c>, which is
    /// outside 1..3 — and a run standing on it is in the last stage as far as a player is concerned.
    /// </summary>
    [Fact]
    public void RunProgress_reads_the_boss_as_the_last_stage()
    {
        var run = Run(position: BossNode());

        ChapterProgressReadout.RunProgress(run, BootContent.Shipped, TwoChapters).ShouldNotBeNull().Stage.ShouldBe(
            3,
            "'0/3' or '4/3' on the panel is a stage the player has never heard of. The boss ends the " +
            "third stage, so the run stands in it.");
    }

    [Fact]
    public void RunProgress_is_null_for_a_chapter_the_authored_list_does_not_hold()
    {
        var run = Run(position: -1, chapterId: 2);

        ChapterProgressReadout.RunProgress(run, BootContent.Shipped, [new ChapterDocument(1, GreenwoodName)]).ShouldBeNull(
            "the shipped content generates chapter 2's board perfectly well, but the list of chapters " +
            "the screen was handed does not name it — so there is no chapter name to put on the panel, " +
            "and a panel with a stage and no chapter is a panel half drawn.");
    }

    [Fact]
    public void RunProgress_is_null_for_a_chapter_the_content_does_not_author()
    {
        var run = Run(position: -1, chapterId: 99);

        ChapterProgressReadout.RunProgress(run, BootContent.Shipped, [.. TwoChapters, new ChapterDocument(99, "FIXTURE Ghost")]).ShouldBeNull(
            "no board can be generated for a chapter the content does not author, and the projection " +
            "says so by throwing. A reader that let it out would take the Home read down over a run " +
            "row written by a newer build.");
    }

    [Fact]
    public void RunProgress_names_the_chapter_from_the_authored_list()
    {
        var run = Run(position: -1, chapterId: 2);

        var progress = ChapterProgressReadout.RunProgress(run, BootContent.Shipped, TwoChapters).ShouldNotBeNull();

        progress.ChapterId.ShouldBe(2);
        progress.ChapterName.ShouldBe(
            AshenName,
            "the panel's title is the chapter's authored name, taken from the same list the progress " +
            "tile names chapters from — not a second lookup that could disagree with it.");
    }

    [Fact]
    public void RunProgress_carries_the_hit_points_as_the_row_holds_them()
    {
        var run = Run(position: -1, currentHp: 37, maxHp: 120);

        var progress = ChapterProgressReadout.RunProgress(run, BootContent.Shipped, TwoChapters).ShouldNotBeNull();

        progress.CurrentHp.ShouldBe(37, "the hero's health is stored on the run and shown as stored.");
        progress.MaxHp.ShouldBe(120, "and the pool it is out of, likewise — the rules sized it, the screen repeats it.");
    }

    // ---------------------------------------------------------------------------- fixtures

    private static RunSnapshot Run(int position, int chapterId = 1, int currentHp = 100, int maxHp = 100) =>
        PlayerState.Run(
            OpenRun, Profile, RunPhase.InProgress,
            position: position, currentHp: currentHp, maxHp: maxHp, chapterId: chapterId);

    /// <summary>The board chapter 1 generates from the fixture seed — where the node ids below come from.</summary>
    private static BoardView Chapter1Board() => BoardView.Project(Run(position: -1), BootContent.Shipped);

    private static int FirstSpineNodeInStage(int stage) =>
        Chapter1Board().Spine.First(node => node.Stage == stage).NodeId;

    private static int BossNode() => Chapter1Board().Spine[^1].NodeId;
}
