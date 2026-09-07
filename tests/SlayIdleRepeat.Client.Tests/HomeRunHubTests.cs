using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The run hub's remaining claims: the latch that keeps a double tap off the seam, the reading a
/// gain accent is decided from, the chapter name the client resolves for itself, and the two font
/// budgets the scene is supposed to be holding.
/// </summary>
/// <remarks>
/// 🔒 Written beside <see cref="HomePresenterTests"/> rather than inside it, because each of these
/// needs a fixture that file does not have: a seam that can be left genuinely in flight, a content
/// set with chapters in it, and the scene file read as text.
/// </remarks>
public sealed class HomeRunHubTests
{
    private const int RunCost = 20;

    private const int NextChapter = 7;

    /// <summary>The line the stage card shows when the campaign offers no next chapter.</summary>
    private const string NoStageKey = "loc.home.launch.no_stage.label";

    /// <summary>What a refusal the launch block has no sentence for arrives as.</summary>
    private const string UnsayableRefusal = "START_RUN was refused ILLEGAL_STATE";

    /// <summary>A Crowns balance well past the point the shortened form takes over.</summary>
    private const long HeldCrowns = 412_345L;

    // ------------------------------------------------------------------- 🔒 the double tap

    /// <summary>
    /// 🔴 A second press while the first <c>START_RUN</c> is still outstanding submits nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seam RAISES rather than translates a refusal it has no sentence for, and the plainest
    /// one a player can produce is <c>ILLEGAL_STATE</c> from a second <c>START_RUN</c> arriving
    /// after the first has already opened a run. The presenter is what must not send it.
    /// </para>
    /// <para>
    /// 🔒 The fake is left genuinely in flight, which is the only fixture this can be proved
    /// against: a seam that answers synchronously runs the first press to completion before it
    /// returns, so the second press finds a settled screen and is turned away by whatever guard
    /// happens to be there — including one taken AFTER the await, which is no guard at all. Both
    /// presses are started, then the gate is released, then both are awaited.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_second_press_while_a_run_is_being_started_submits_nothing()
    {
        var screen = GatedHomeScreen.Answering(Ready());
        var presenter = new HomePresenter(screen, ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);

        presenter.CanStartRun.ShouldBeTrue("the fixture must be in a state that starts runs at all.");

        var first = presenter.PressStartAsync(CancellationToken.None);
        var second = presenter.PressStartAsync(CancellationToken.None);

        screen.Release();

        await first;
        var refused = await second;

        screen.StartCallCount.ShouldBe(
            1,
            "the first press owns the run being started. A second submission lands after the run " +
            "is open and comes back ILLEGAL_STATE, which this seam has no sentence for and raises " +
            "rather than translates — so a double tap would take an exception out through an " +
            "engine callback where nothing catches it.");
        refused.ShouldBeNull(
            "and the second press answers with nothing rather than with the first press's outcome: " +
            "a screen that navigated on it would open the board twice.");
    }

    /// <summary>
    /// The negative control: two presses that do not overlap are two runs asked for.
    /// </summary>
    /// <remarks>
    /// Without this, a presenter that simply never submitted twice — or never submitted at all
    /// after the first — would satisfy the case above perfectly.
    /// </remarks>
    [Fact]
    public async Task A_press_after_the_first_has_answered_submits_again()
    {
        var screen = GatedHomeScreen.Answering(Ready());
        var presenter = new HomePresenter(screen, ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);
        screen.Release();

        await presenter.PressStartAsync(CancellationToken.None);
        await presenter.PressStartAsync(CancellationToken.None);

        screen.StartCallCount.ShouldBe(
            2,
            "the latch is held for the length of one submission and no longer. A latch that stayed " +
            "set would leave the button dead for the rest of the session after the first press.");
    }

    /// <summary>The stage the press names is the one the campaign offered, never a number chosen here.</summary>
    [Fact]
    public async Task The_press_starts_the_chapter_the_campaign_offered()
    {
        var screen = GatedHomeScreen.Answering(Ready());
        var presenter = new HomePresenter(screen, ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);
        screen.Release();

        await presenter.PressStartAsync(CancellationToken.None);

        screen.LastStageStarted.ShouldBe(
            NextChapter,
            "the launch block offers one stage and starts that one. A screen sending a stage of its " +
            "own would start a chapter the player was never shown.");
    }

    /// <summary>
    /// 🔒 With no chapter to offer, the press starts nothing rather than naming a stage.
    /// </summary>
    /// <remarks>
    /// A player who has cleared every authored chapter is pointed at nothing, and
    /// <c>NextStageId</c> is null for them. Defaulting to a chapter — the first, the last, zero —
    /// would start a run the campaign never offered (steering S6).
    /// </remarks>
    [Fact]
    public async Task A_press_with_no_chapter_to_offer_starts_nothing()
    {
        var screen = GatedHomeScreen.Answering(Ready() with { NextStageId = null });
        var presenter = new HomePresenter(screen, ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);
        screen.Release();

        await presenter.PressStartAsync(CancellationToken.None);

        screen.StartCallCount.ShouldBe(
            0, "there is no stage to name, and inventing one starts a run nobody was offered.");
    }

    /// <summary>
    /// 🔴 A submission the seam RAISED on leaves the block saying what failed, never the offer it
    /// had already made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>HomeScreen.StartRunAsync</c> raises for every refusal it has no sentence for, and two of
    /// them are reachable without a double tap: a run opened on another device, and a stage this
    /// screen read before the ladder moved under it. A press that faulted and changed nothing is a
    /// dead control — the button keeps the word it had, the player presses it again, and nothing
    /// on the screen ever says why. The failure state is the one state that offers a way out, since
    /// its button is Retry.
    /// </para>
    /// <para>
    /// 🔒 The screen is loaded into a startable state FIRST, so the state it ends in cannot be the
    /// state it began in — a presenter that did nothing at all with the fault would leave Ready
    /// behind and this case would be satisfied by the wrong thing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_submission_the_seam_raised_on_leaves_the_block_saying_what_failed()
    {
        var screen = ScriptedHomeScreen.RaisingOnStart(
            Ready(), new InvalidOperationException(UnsayableRefusal));
        var presenter = new HomePresenter(screen, ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);

        presenter.LaunchState.ShouldBe(
            HomeLaunchState.Ready, "the press below has to be made from a state that offers one.");

        var outcome = await presenter.PressStartAsync(CancellationToken.None);

        outcome.ShouldBeNull("nothing was started, so there is no outcome to carry.");
        presenter.LaunchState.ShouldBe(
            HomeLaunchState.PresenterFailure,
            "a press that raised has to move the block. Left in Ready it is a button that was " +
            "pressed, did nothing, and still offers the same run.");
        var line = presenter.FailureLine.ShouldNotBeNull(
            "the line names what failed, so a log is not the only place the refusal exists.");

        line.ShouldContain(UnsayableRefusal);
    }

    /// <summary>
    /// 🔒 A press that raised may be made again — the in-flight latch is released either way.
    /// </summary>
    /// <remarks>
    /// The latch is taken before the await and dropped in a <c>finally</c>. A fault that escaped
    /// past it would leave it set for the rest of the session, and the Retry the failure state
    /// offers would be a second dead press on top of the first.
    /// </remarks>
    [Fact]
    public async Task A_press_that_raised_does_not_leave_the_latch_set()
    {
        var screen = ScriptedHomeScreen.RaisingOnStart(
            Ready(), new InvalidOperationException(UnsayableRefusal));
        var presenter = new HomePresenter(screen, ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);
        await presenter.PressStartAsync(CancellationToken.None);

        await presenter.LoadAsync(CancellationToken.None);
        await presenter.PressStartAsync(CancellationToken.None);

        screen.StartCallCount.ShouldBe(
            2,
            "the second press reached the seam. A latch left set by the first would make every " +
            "later press a control that does nothing at all.");
    }

    // ---------------------------------------------------- 🔒 the long press's full figures

    /// <summary>
    /// 🔒 A pill held down writes the Crowns figure in full; letting go shortens it again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project's number rule is one rule for every screen: shortened above ten thousand, exact
    /// on a long press. <c>PerkDraft</c> and <c>EventScreen</c> both hold it, and the pills are
    /// where this screen's shortened figures are.
    /// </para>
    /// <para>
    /// 🔒 Asserted through <c>CrownsPillTextFor</c> — the rule the pill is HANDED, and the one a
    /// count-up interpolates each step with — rather than through the settled text alone, so a
    /// reveal that reached the settled value and not the rule would still be caught.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_held_pill_writes_the_Crowns_figure_in_full()
    {
        var presenter = new HomePresenter(
            ScriptedHomeScreen.Answering(Ready() with { Crowns = HeldCrowns }),
            ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);

        presenter.CrownsPillTextFor(HeldCrowns).ShouldBe(
            "412.3k", "nobody is holding anything yet, so the pill is shortened.");

        presenter.RevealFullValues();

        presenter.CrownsPillTextFor(HeldCrowns).ShouldBe(
            "412345",
            "a long press is how a player asks 'how many, exactly?', and a pill that answered the " +
            "shortened figure to it leaves the gesture with nothing to reveal.");

        presenter.ConcealFullValues();

        presenter.CrownsPillTextFor(HeldCrowns).ShouldBe(
            "412.3k", "the release of the press ends the reveal; a reveal that never ends is not one.");
    }

    // ------------------------------------------------------------- 🔒 the gain accent's reading

    /// <summary>A FIRST power reading is not a rise, whatever the number is.</summary>
    /// <remarks>
    /// The flash says "you gained power since you were last here". On the opening frame there is no
    /// last time, and a presenter comparing against zero would say it to every player who opened
    /// the game.
    /// </remarks>
    [Fact]
    public async Task The_first_power_reading_is_not_a_rise()
    {
        var presenter = new HomePresenter(
            GatedHomeScreen.Answering(Ready() with { Power = 9_000d }), ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);

        presenter.PowerRose.ShouldBeFalse(
            "nothing has been read before this, so nothing has risen.");
    }

    /// <summary>A second reading above the first is, and one at or below it is not.</summary>
    [Theory]
    [InlineData(9_000d, 9_001d, true)]
    [InlineData(9_000d, 9_000d, false)]
    [InlineData(9_000d, 8_999d, false)]
    public async Task A_later_power_reading_is_a_rise_only_when_it_is_higher(
        double before, double after, bool rose)
    {
        var screen = GatedHomeScreen.Answering(Ready() with { Power = before });
        var presenter = new HomePresenter(screen, ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);

        screen.Answer(Ready() with { Power = after });

        await presenter.LoadAsync(CancellationToken.None);

        presenter.PowerRose.ShouldBe(
            rose,
            $"{before} to {after}. A flash on an unchanged or a fallen reading is the screen " +
            "congratulating a player for nothing.");
    }

    /// <summary>
    /// 🔒 Every value the pills show is settled the moment the read lands, whether or not anything
    /// animated — which is what makes the reduced-motion arm READABLE rather than merely quieter.
    /// </summary>
    /// <remarks>
    /// The count-up interpolates between two numbers and writes each step with the same rule the
    /// settled value is written by, so the animation cannot be carrying information the settled
    /// text does not. Stated over the three pills at once because the claim is about the screen.
    /// </remarks>
    [Fact]
    public async Task Every_pill_reads_its_settled_value_without_any_animation_having_run()
    {
        var presenter = new HomePresenter(
            GatedHomeScreen.Answering(Ready() with { Power = 12_480d }), ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);

        new[] { presenter.CrownsPillText, presenter.EnergyPillText, presenter.PowerPillText }
            .ShouldBe(
                new[]
                {
                    presenter.CrownsPillTextFor(presenter.HubCrowns),
                    presenter.EnergyPillTextFor(presenter.HubEnergy),
                    presenter.PowerPillTextFor(presenter.HubPower),
                },
                "the settled text IS the text the last step of a count-up would write. A pill that " +
                "reached its real value only through an animation would be blank, or wrong, for " +
                "every player who has motion turned off.");
    }

    // ------------------------------------------------- 🔒 the chapter the client names for itself

    /// <summary>
    /// The stage card names the chapter, and the name comes out of the chapter documents.
    /// </summary>
    /// <remarks>
    /// 🔴 The layer below deliberately leaves <c>NextStageName</c> absent: a chapter document
    /// authors its name as a LOC KEY, and resolving one belongs to the catalogue in this assembly.
    /// A key carried in a field called a name is drawn on screen as if it were one, so the client
    /// resolves it from the id instead — and this is the case that says it did.
    /// </remarks>
    [Fact]
    public async Task The_stage_card_names_the_next_chapter_out_of_the_chapter_documents()
    {
        var presenter = HubOverContent(NextChapter);

        await presenter.LoadAsync(CancellationToken.None);

        presenter.StageName.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.ChapterNameKey(NextChapter)),
            "the chapter's authored name, resolved. A presenter carrying the document's key " +
            "straight through would put 'loc.chapter.7.name' on the card.");
        presenter.StageTitle.ShouldBe(
            presenter.StageName,
            "and the card's line is that name rather than the stand-in for having none.");
    }

    /// <summary>A chapter the content set does not author has no name, and the card says so.</summary>
    /// <remarks>
    /// The discriminating half: without it, a presenter that formatted the id — "Chapter 7" — would
    /// pass the case above under any content set at all.
    /// </remarks>
    [Fact]
    public async Task A_chapter_the_content_does_not_author_is_the_unnamed_stage_line()
    {
        var presenter = HubOverContent(authoredChapter: NextChapter + 1);

        await presenter.LoadAsync(CancellationToken.None);

        presenter.StageName.ShouldBeNull(
            "there is no document for chapter 7 in this content set, so there is no name to show.");
        presenter.StageTitle.ShouldBe(
            NoStageKey,
            "and the card falls back to the authored no-stage line rather than to an id, a blank, " +
            "or a name it made up. It reads as the KEY here because this fixture authors no value " +
            "for it and a catalogue miss renders its own key — which is the documented behaviour, " +
            "and is still not 'Chapter 7', which is what a presenter formatting the id would say.");
    }

    // ------------------------------------------------- ⚠️ the two budgets the scene is holding

    private const string HomeScene = "src/SlayIdleRepeat.Client/game/scenes/Home.tscn";

    /// <summary>The exports Home.tscn states its per-character budgets in.</summary>
    private static readonly Regex ExportedAdvance = new(
        @"^Pill(?<which>Value|Caption)CharacterAdvance = (?<value>[0-9.]+)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// 🔴 The scene's exported character budgets are the ones the pill-row arithmetic was checked
    /// at.
    /// </summary>
    /// <remarks>
    /// <c>HomeLayoutTests</c> proves the three pills fit at 15 units a value character and 12 a
    /// caption character, and says in as many words that those two numbers are the fixture's own
    /// rather than a measured face. Nothing connected that budget to the scene: the exports could
    /// drift to 20 and 16 and the row would overflow on a handset with every case still green.
    /// This is the connection — the scene may hold the budget or come in under it, never over.
    /// </remarks>
    [Fact]
    public void The_scenes_exported_character_budgets_are_the_ones_the_pill_row_was_measured_at()
    {
        var exported = ExportedAdvance.Matches(SceneText.Read(HomeScene))
            .ToDictionary(
                match => match.Groups["which"].Value,
                match => float.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

        exported.Count.ShouldBe(
            2,
            "Home.tscn states neither budget as an export, so the layout is running on " +
            "HomeLayoutMetrics' own defaults — which do not exist, because that type offers none.");
        exported["Value"].ShouldBeLessThanOrEqualTo(
            15f,
            "the pill row's fit was proved at 15 units a value character. A scene exporting more " +
            "pushes a pill off the screen edge, and no case anywhere would notice.");
        exported["Caption"].ShouldBeLessThanOrEqualTo(
            12f, "and the same for the Energy pill's countdown.");
    }

    // ---------------------------------------------------------------------------- fixtures

    /// <summary>A view model with a chapter to offer and the Energy to run it.</summary>
    private static HomeViewModel Ready() =>
        ScriptedHomeScreen.ViewModel(energy: RunCost * 2, energyCost: RunCost) with
        {
            NextStageId = NextChapter,
        };

    /// <summary>
    /// A hub over a real seam and a real content set, so the chapter documents are really read.
    /// </summary>
    /// <param name="authoredChapter">The one chapter the content set authors.</param>
    private static HomePresenter HubOverContent(int authoredChapter)
    {
        var content = ScreenContent.Authoring([authoredChapter]);

        return new HomePresenter(
            GatedHomeScreen.Answering(Ready()), ScreenContent.Catalogue(content), content);
    }
}
