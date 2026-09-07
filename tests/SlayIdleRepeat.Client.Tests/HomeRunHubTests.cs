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

    /// <summary>The primary button's word while a submission is outstanding.</summary>
    private const string StartingActionKey = "loc.home.launch.starting.action";

    /// <summary>The line a destination with no screen yet answers a tap with.</summary>
    private const string NotOpenYetKey = "loc.home.not_open_yet.status";

    /// <summary>…and the refill offer's own wording of it.</summary>
    private const string RefillNotOpenYetKey = "loc.home.launch.refill_not_open_yet.status";

    /// <summary>A Crowns balance well past the point the shortened form takes over.</summary>
    private const long HeldCrowns = 412_345L;

    /// <summary>
    /// A power reading with a fraction in it, so a floor can be told apart from a rounding.
    /// </summary>
    /// <remarks>
    /// 🔴 Every other power fixture on this screen is a whole number, and a whole number is the
    /// one input for which flooring and rounding agree — which left the pill's stated rule ("the
    /// power a player HAS rather than the one nearest") pinned by nothing at all.
    /// </remarks>
    /// <remarks>
    /// 🔴 The fraction alone is not enough now that the pill SHORTENS what it writes: at 12,480.7
    /// the floor and the ceiling both shorten to <c>12.4k</c>, so the rule would be unpinned again.
    /// This reading straddles a rung of the shortened form — 12,999 writes <c>12.9k</c> and 13,000
    /// writes <c>13.0k</c> — so a ceiling is visible in the pill's own text.
    /// </remarks>
    private const double SettledPower = 12_999.7d;

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
    /// 🔴 The button says the press registered, for as long as the press is outstanding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The latch above keeps a second submission off the seam, and until this it did so SILENTLY:
    /// the button kept the word Start Run, kept its ember and did not move for however long the host
    /// took to answer — up to the whole request timeout on the shipped HTTP adapter. A player who
    /// taps the one primary action on the core-loop screen and sees nothing change has been told the
    /// tap did not land, and taps again. The refusal has to be visible, not merely correct.
    /// </para>
    /// <para>
    /// 🔒 Proved on the same in-flight fixture the latch is, because the claim is about the state
    /// DURING the call: a seam that answered synchronously would be settled again before anything
    /// could be asked, and every assertion below would pass against a presenter that shows nothing
    /// at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_button_says_a_run_is_being_started_while_the_press_is_outstanding()
    {
        var strings = ScreenContent.Catalogue();
        var screen = GatedHomeScreen.Answering(Ready());
        var presenter = new HomePresenter(screen, strings);

        await presenter.LoadAsync(CancellationToken.None);

        var settled = presenter.PrimaryActionLabel;

        presenter.PrimaryAction.ShouldBe(
            HomePrimaryAction.StartRun, "the precondition: a press from here starts a run.");

        var press = presenter.PressStartAsync(CancellationToken.None);

        presenter.PrimaryAction.ShouldBe(
            HomePrimaryAction.Starting,
            "the latch is taken before the seam is awaited, so the screen is already in its " +
            "in-flight state by the time the press hands a task back — which is the only moment a " +
            "renderer has to draw it.");
        presenter.PrimaryActionLabel.ShouldBe(
            strings.Resolve(StartingActionKey),
            "its own authored word. Left on the launch state's, the button would read Start Run " +
            "through the whole submission and be the same button it was before the tap.");
        presenter.PrimaryActionLabel.ShouldNotBe(
            settled, "which is the point: something the player can see has to have changed.");
        presenter.PrimaryActionColour.ShouldBe(
            HomeColourRole.Action,
            "the ember it was already wearing. A colour that moved under the finger would read as " +
            "a different button having arrived rather than as this one working.");

        screen.Release();

        await press;

        presenter.PrimaryAction.ShouldBe(
            HomePrimaryAction.StartRun,
            "and the in-flight state is not a state the screen can be stuck in: the latch is " +
            "dropped in a finally, so the button comes back however the submission ended.");
    }

    /// <summary>
    /// 🔴 Every destination this build has no screen for has a sentence to answer a tap with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eleven of them — three tabs, three rail entries, the settings control, the three resource
    /// sheets and the refill offer — and each used to answer a press by printing one line to the
    /// engine log. That is a fact for whoever runs the build and nothing at all for the player
    /// holding the phone: a control that was tapped and did not visibly do anything is a control
    /// they read as broken, and tap again.
    /// </para>
    /// <para>
    /// 🔒 The refill offer gets its OWN line, and that is the half worth pinning. It is the primary
    /// action of <see cref="HomeLaunchState.InsufficientEnergy"/>, so the player making that press
    /// is blocked from the thing this whole screen exists to do — and unlike every other stub there
    /// IS a next step to name, because Energy regenerates on its own and the pill above is counting
    /// down to the next point. The two lines sharing one key would leave that player told only that
    /// something is missing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_destinations_with_no_screen_yet_each_have_a_line_to_answer_a_tap_with()
    {
        var strings = ScreenContent.Catalogue();
        var presenter = new HomePresenter(ScriptedHomeScreen.Answering(Ready()), strings);

        await presenter.LoadAsync(CancellationToken.None);

        presenter.NotOpenYetNotice.ShouldBe(
            strings.Resolve(NotOpenYetKey),
            "the one acknowledgement every unbuilt destination gives back, authored and localised " +
            "like every other string on this screen.");
        presenter.RefillNotOpenYetNotice.ShouldBe(
            strings.Resolve(RefillNotOpenYetKey),
            "and the refill offer's own, because that press is made by somebody who cannot start a " +
            "run and needs to be told what does get them one.");
        presenter.RefillNotOpenYetNotice.ShouldNotBe(
            presenter.NotOpenYetNotice,
            "two keys, not one resolved twice: a single line for both cannot name the way out of " +
            "the one state where a way out exists.");
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
            GatedHomeScreen.Answering(Ready() with { Power = SettledPower }),
            ScreenContent.Catalogue());

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

    /// <summary>
    /// 🔴 Each pill spells its value by its own rule: Crowns shortened, Energy as the bar over its
    /// maximum, and power shortened and FLOORED.
    /// </summary>
    /// <remarks>
    /// The case above compares each settled text against the rule the pill is HANDED — which is
    /// what the production property is defined as, so it pins the delegation and says nothing
    /// whatever about the spelling. Proved by mutation: an Energy pill writing <c>40/120X</c>, and
    /// a power pill formatted to three decimals, each left all 1413 cases green. The three
    /// spellings are the screen's own number rules, and this is where they are stated.
    /// </remarks>
    [Fact]
    public async Task Each_pill_spells_its_value_by_the_rule_that_pill_is_written_by()
    {
        var presenter = new HomePresenter(
            GatedHomeScreen.Answering(Ready() with { Power = SettledPower }),
            ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);

        presenter.CrownsPillText.ShouldBe(
            "412.3k",
            "Crowns is the one pill the shortening rule reaches, and it is shortened above ten " +
            "thousand for every screen in this game.");
        presenter.EnergyPillText.ShouldBe(
            $"{RunCost * 2}/{ScriptedHomeScreen.FixtureEnergyMax}",
            "the bar over the bar's own maximum, and nothing else on either side of the separator. " +
            "The Reserve is a separate bank and is deliberately not in the denominator.");
        presenter.PowerPillText.ShouldBe(
            "12.9k",
            "shortened above ten thousand like every other number this client draws — a pill " +
            "writing '12,999' would be the only number in the game outside PlayerNumber's rule, " +
            "and NINE characters wide at the 1.2M the brief names. Floored rather than rounded " +
            "too: 12,999.7 rounds up over a rung of the shortened form and would read 13.0k, " +
            "quoting a player a power they do not have.");
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

    /// <summary>
    /// The root node's own property block: everything between its header and the next node's.
    /// </summary>
    /// <remarks>
    /// Scoped to the root rather than scanned over the whole file, because the three pill instances
    /// each export a <c>Glyph</c> and a whole-file scan cannot tell an export the layout is built
    /// from apart from one an instance overrides.
    /// </remarks>
    private static readonly Regex RootProperties = new(
        @"^\[node name=""Home"" type=""Node3D""\]\r?\n(?<block>(?:.*\r?\n)*?)\r?\n\[node ",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Every number Home.tscn hands HomeLayout, read back off the scene.</summary>
    private static readonly Regex ExportedNumber = new(
        @"^(?<name>[A-Z][A-Za-z]*) = (?<value>[0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>The canvas the project declares, and the short shipping profile's height.</summary>
    private const float CanvasWidth = 1080f;

    private const float CanvasHeight = 1920f;

    /// <summary>
    /// 🔴 The three pills fit the row at the widest values THE PRESENTER ACTUALLY WRITES, measured
    /// against the numbers THE SCENE ACTUALLY EXPORTS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>This is the case <c>HomeLayoutTests</c> could not be.</b> That one measures a fixture's
    /// own metrics against strings a case chose, and both halves drifted from the shipped screen at
    /// once: it took the power pill's width from <c>PlayerNumber.Abbreviated(1_200_000)</c> — four
    /// characters — while <c>PowerPillTextFor</c> wrote a thousands-separated <c>1,200,000</c>, nine.
    /// Nine characters reserve 252 units; the three pills came to 744 against the 684 the bar had,
    /// and the widest pill was drawn past the screen edge with every case green. Here the strings
    /// come from a loaded presenter and the budget from <c>Home.tscn</c>, so neither can drift
    /// without this going red.
    /// </para>
    /// <para>
    /// ⚠️ The values are the widest each pill REACHES, not the widest the brief happens to name: the
    /// shortened form is longest just below a suffix rung ("999.9k"), not at the brief's 1.2M, which
    /// is four characters.
    /// </para>
    /// <para>
    /// ⚠️ <b>The revealed row is not covered and does not fit.</b> Under a long press the same three
    /// pills reserve 714 against 708 at the brief's own values, and 729 at a seven-digit Crowns
    /// balance. It is a transient gesture state and closing it means changing what
    /// <c>PlayerNumber.Full</c> writes or what the band geometry reserves — both functional, both
    /// pinned elsewhere. Stated rather than asserted, so nobody reads this case as covering it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_scenes_own_numbers_hold_the_three_pills_at_the_widest_values_they_reach()
    {
        var metrics = SceneMetrics();
        var presenter = new HomePresenter(
            GatedHomeScreen.Answering(Widest()), ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);

        var crowns = presenter.CrownsPillText;
        var energy = presenter.EnergyPillText;
        var caption = presenter.EnergyPillCaption;
        var power = presenter.PowerPillText;

        var total =
            HomeLayout.PillWidth(crowns.Length, 0, metrics.Pill)
            + HomeLayout.PillWidth(energy.Length, caption.Length, metrics.Pill)
            + HomeLayout.PillWidth(power.Length, 0, metrics.Pill);

        total.ShouldBeLessThanOrEqualTo(
            HomeLayout.For(CanvasWidth, CanvasHeight, default, metrics).PillRowWidth,
            $"'{crowns}', '{energy} {caption}' and '{power}' are what the three pills write at " +
            "their widest, and the bar has the canvas less its own padding, the avatar, the " +
            "settings TARGET and four gaps. A row that cannot hold them does not clip — the " +
            "container grows past the canvas and the avatar and the settings control are drawn " +
            "off both edges.");
    }

    /// <summary>The exports Home.tscn actually holds, as the metrics the layout is built from.</summary>
    /// <remarks>
    /// Read off the scene rather than restated, because a fixture restating them is a second copy
    /// that drifts — which is exactly how the budget above came to be checked against numbers the
    /// screen was not laid out from.
    /// </remarks>
    private static HomeLayoutMetrics SceneMetrics()
    {
        var root = RootProperties.Match(SceneText.Read(HomeScene));

        root.Success.ShouldBeTrue(
            "Home.tscn no longer opens with a Node3D root called Home, so the exports the layout " +
            "is built from cannot be told apart from an instance's overrides.");

        var exported = ExportedNumber.Matches(root.Groups["block"].Value)
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => float.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

        float Of(string name) => exported.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Home.tscn exports no '{name}', so the screen is laid out from a default " +
                "HomeLayoutMetrics does not offer.");

        return new HomeLayoutMetrics(
            new HomeBandMetrics(Of("TopBarHeight"), Of("LaunchHeight"), Of("TabBarHeight")),
            new HomeTopBarMetrics(
                Of("TopBarSidePadding"),
                Of("TopBarItemGap"),
                Of("AvatarWidth"),
                Of("SettingsGlyphWidth")),
            new HomePillMetrics(
                Of("PillHorizontalPadding"),
                Of("PillIconWidth"),
                Of("PillInnerGap"),
                Of("PillValueCharacterAdvance"),
                Of("PillCaptionCharacterAdvance")),
            new HomeLaunchMetrics(
                Of("LaunchSidePadding"),
                Of("LaunchRowGap"),
                Of("StageCardHeight"),
                Of("StartButtonHeight"),
                Of("RewardLineHeight"),
                Of("ChangeButtonWidth"),
                Of("ChangeButtonHeight")));
    }

    /// <summary>
    /// The row at every pill's widest: a Crowns balance and a power index one unit below the next
    /// suffix rung, and a bar one point short of full so the countdown is on the page.
    /// </summary>
    private static HomeViewModel Widest() =>
        Ready() with
        {
            Crowns = 999_999L,
            Energy = ScriptedHomeScreen.FixtureEnergyMax - 1,
            Power = 999_999d,
        };

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
