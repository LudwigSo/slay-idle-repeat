using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The timing-bar minigame's authored numbers: <c>tuning/minigames.json</c>, the schema that governs
/// it, and the one place its strike count has to agree with another document.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>A client-only PRESENTATION file, and the one tuning file no design document catalogues.</b>
/// How fast a cursor sweeps and how wide its window is decide nothing the server adjudicates — the
/// rules layer validates a claimed tier and nothing else. It lives under <c>tuning/</c> because that
/// is where numbers a designer retunes live, and its own <c>_doc</c> is what says it is not one of
/// the balance files the catalogue lists.
/// </para>
/// <para>
/// 🔴 <b>The load-bearing agreement is <c>strikes + 1 == the MG_TIMING_BAR row count</c>.</b> A
/// strike scores or it does not, so a game of <c>N</c> strikes ends on one of <c>N + 1</c> hit
/// counts and the tier is the hit count. Authored in two documents that nothing else compares, a
/// drift means the best play a player can make submits a tier <c>MINIGAME_SUBMIT</c> refuses — a
/// perfect game that pays nothing, reported to the player as a rules refusal.
/// </para>
/// </remarks>
public sealed class MinigameTuningDataTests
{
    private const string Document = "tuning/minigames.json";

    private const string Schema = "schema/minigames.schema.json";

    private const string TimingBar = Document + "#/timingBar";

    private const string StrikesPointer = TimingBar + "/strikes";

    private const string SweepSecondsPointer = TimingBar + "/sweepSeconds";

    private const string HitWindowHalfWidthPointer = TimingBar + "/hitWindowHalfWidth";

    private const string ReducedMotionStepFractionPointer = TimingBar + "/reducedMotionStepFraction";

    /// <summary>Where the outcome tiers the strike count has to agree with are authored.</summary>
    private const string TimingBarRewards = "tuning/currencies.json#/minigameRewards/MG_TIMING_BAR";

    /// <summary>The word the document's own note has to carry, because the note is the authorisation.</summary>
    /// <remarks>
    /// The tuning-file count pins elsewhere in this suite say the catalogue lists sixteen balance
    /// files and that this seventeenth is not one of them. That claim is only honest if the file says
    /// so itself — otherwise the exemption lives in a because-string and the file looks like a
    /// balance document nobody catalogued.
    /// </remarks>
    private const string SelfDescription = "presentation";

    /// <summary>The loaded shipped set, read once for the whole class rather than per assertion.</summary>
    private static readonly Lazy<ContentSnapshot> LazyShipped =
        new(() => ContentLoader.Load(RepoData.Source()).Require());

    private static ContentSnapshot Data() => LazyShipped.Value;

    [Fact]
    public void The_minigame_tuning_document_pairs_with_the_minigames_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(Document).ShouldBe(
            Schema,
            "an unpaired data file is one nobody validates, and these four numbers decide whether a " +
            "skill game is winnable at all.");
        shipped.ShouldContain(
            Document,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored.");
        shipped.ShouldContain(
            Schema,
            "likewise the schema half: deleting it leaves SchemaFor answering exactly as it does " +
            "today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The document is there, <b>and then</b> the whole set still loads with it in.
    /// </summary>
    [Fact]
    public void The_shipped_data_set_still_validates_with_the_minigame_tuning_present()
    {
        RepoData.Documents.Keys.ShouldContain(
            Document,
            "the document is absent, so the load below succeeds without ever seeing it and this " +
            "case's name is a claim about a file nobody shipped.");

        ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical).Succeeded.ShouldBeTrue(
            "the timing bar's numbers were added without the schema that governs them, or in a " +
            "shape it refuses — which is a client that cannot load its own content.");
    }

    /// <summary>The four numbers the client plays the timing bar by.</summary>
    /// <remarks>
    /// Read with the typed readers, which throw on a deliberate <c>null</c>, so a quietly nulled
    /// number fails here rather than reading as zero — and a sweep width of zero is a game that
    /// cannot be won.
    /// </remarks>
    [Theory]
    [InlineData(StrikesPointer, 3.0)]
    [InlineData(SweepSecondsPointer, 1.2)]
    [InlineData(HitWindowHalfWidthPointer, 0.08)]
    [InlineData(ReducedMotionStepFractionPointer, 0.05)]
    public void The_four_authored_numbers_are_the_ones_the_client_plays_by(
        string pointer, double expected) =>
        Data().ReadDouble(pointer).ShouldBe(expected);

    /// <summary>
    /// 🔴 <b>The strike count and the reward table's row count are one number in two documents.</b>
    /// </summary>
    /// <remarks>
    /// Both sides read rather than either transcribed: a case naming 3 and 4 would agree with a
    /// timing bar retuned to five strikes against a table nobody grew, which is precisely the drift
    /// this exists to catch.
    /// </remarks>
    [Fact]
    public void The_strike_count_and_the_reward_tables_row_count_agree()
    {
        var data = Data();

        var strikes = data.ReadInt64(StrikesPointer);
        var rows = data.Read(TimingBarRewards).Items.Count;

        rows.ShouldBeGreaterThan(
            1,
            "the reward table authors " + rows + " outcome tiers for the timing bar. With one row " +
            "or none the equality below holds over a game that has nothing to play for.");
        (strikes + 1).ShouldBe(
            (long)rows,
            "the timing bar gives " + strikes + " strikes, so it can end on " + (strikes + 1) +
            " different hit counts, and the reward table authors " + rows + " outcome tiers. The " +
            "tier IS the hit count, so a mismatch means either the best play a player can make " +
            "submits a tier MINIGAME_SUBMIT refuses, or a table row nothing can ever reach.");
    }

    /// <summary>
    /// The window is narrower than the bar and the step really moves the cursor.
    /// </summary>
    /// <remarks>
    /// 🔒 Bounds rather than restatements of the values above. A half-width at or past 0.5 makes
    /// every strike a hit and the game a formality; a reduced-motion step of zero leaves a player who
    /// cannot watch a moving cursor with no way to aim at all, and one at or past 1 skips the window
    /// entirely.
    /// </remarks>
    [Fact]
    public void The_window_is_narrower_than_the_bar_and_the_reduced_motion_step_can_reach_it()
    {
        var data = Data();

        data.ReadDouble(SweepSecondsPointer).ShouldBeGreaterThan(
            0.0, "a sweep of no duration leaves the cursor motionless at one end of the bar.");

        var halfWidth = data.ReadDouble(HitWindowHalfWidthPointer);

        halfWidth.ShouldBeGreaterThan(0.0, "a window of no width can never be struck.");
        halfWidth.ShouldBeLessThan(
            0.5,
            "the window is measured from the centre of the bar, so a half-width of 0.5 covers the " +
            "whole bar and every strike scores.");

        var step = data.ReadDouble(ReducedMotionStepFractionPointer);

        step.ShouldBeGreaterThan(
            0.0, "a step of nothing leaves a reduced-motion player unable to move the cursor at all.");
        step.ShouldBeLessThan(
            halfWidth,
            "one step is wider than the whole scoring window, so a reduced-motion player can step " +
            "straight over it and never land inside — the accessible arm of this game would be " +
            "unwinnable while the timed one is not.");
    }

    /// <summary>
    /// 🔒 The document says in its own words that it is not a balance file.
    /// </summary>
    /// <remarks>
    /// 🔴 Three other cases in this suite pin the tuning directory at a count the design catalogue
    /// authorises, and their because-strings claim this file is exempt because it is client-side
    /// presentation. That claim belongs in the file, not only in a test message — otherwise the next
    /// reader finds a seventeenth balance document nobody catalogued.
    /// </remarks>
    [Fact]
    public void The_document_says_in_its_own_note_that_it_is_presentation_and_not_balance()
    {
        var data = Data();

        var note = data.ReadText(Document + "#/_doc");

        note.ShouldNotBeNullOrWhiteSpace();
        note.ShouldContain(
            SelfDescription,
            Case.Insensitive,
            "the note does not say what this file IS. The tuning-file count pins elsewhere carry an " +
            "exemption for it on the grounds that it is client-side presentation rather than " +
            "balance, and that sentence has to live here rather than only in a test's message.");

        data.ReadText(TimingBar + "/_doc").ShouldNotBeNullOrWhiteSpace(
            "the block carrying the four numbers has no note of its own, so a reader who found the " +
            "sweep or the window surprising has nothing saying where they came from — which is " +
            "nowhere, since no design document authors them.");
    }
}
