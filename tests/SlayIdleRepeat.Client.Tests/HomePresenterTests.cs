using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The Home screen's own seam: the one read it makes, the one decision it makes from it, the header
/// values it carries through unchanged, and the values it deliberately does not carry at all.
/// </summary>
/// <remarks>
/// 🔒 "Start a run" and "resume the run you are in" send the player to two different places, and the
/// four states that can produce one of those two answers — no run, a finished run, no such profile,
/// a read that did not answer — look identical to anything asking a yes/no question. Every case
/// below therefore asserts a named <see cref="HomeContinueDecision"/> rather than a boolean.
/// </remarks>
public sealed class HomePresenterTests
{
    private static readonly PlayerId Profile = new("PLAYER_home_4c1e");

    private static readonly RunId OpenRun = new("RUN_home_9b22");

    private const string ReadFailureMessage = "the run pointer names a cache row that is not there";

    private const int CarriedEnergy = 137;

    private const int CarriedReserve = 42;

    private const int CarriedLegendLevel = 23;

    private const string CarriedDisplayName = "Fixture Hero";

    // ------------------------------------------------------------ before anything happens

    [Fact]
    public void Decision_is_NotYetRead_before_StartAsync_is_called()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow()));

        presenter.Decision.ShouldBe(
            HomeContinueDecision.NotYetRead,
            "the first frame is drawn before the read answers, so the decision a freshly built " +
            "presenter reports is what the scene renders against. A default of StartNewRun would " +
            "show a start button to a player who is mid-run, and one tap would throw their run away.");
    }

    [Fact]
    public void StatusText_before_StartAsync_is_the_loading_line()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow()));

        presenter.StatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.LoadingStatusKey),
            "the frame before the read answers has to say something, and it has to be words rather " +
            "than a key or a blank. An empty line for exactly the interval a slow read takes is the " +
            "state that looks like a hung game.");
    }

    // ---------------------------------------------------------------------- the one read

    [Fact]
    public async Task StartAsync_reads_the_state_of_the_player_the_screen_was_built_for()
    {
        var host = RecordingGameHost.Finding(PlayerRow());
        var presenter = Home(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadPlayer.ShouldBe(
            Profile,
            "the profile boot opened is the one this screen is about. A presenter reading someone " +
            "else's row — or a default id — would draw a header belonging to nobody.");
    }

    /// <summary>
    /// 🔒 The argument, not the answer. Both shapes of this call return the same type.
    /// </summary>
    [Fact]
    public async Task StartAsync_names_no_run_so_the_read_answers_with_whatever_run_the_player_is_in()
    {
        var host = RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress));
        var presenter = Home(host);

        await presenter.StartAsync(CancellationToken.None);

        host.ReadCallCount.ShouldBe(
            1,
            "an anchor for the claim below, which a presenter that never called the host would " +
            "satisfy with a null run argument it never passed.");
        host.ReadRun.ShouldBeNull(
            "a read that names a run asks 'does THIS run exist', and answers NoSuchRun when it does " +
            "not. A read that names none asks 'what run is this player in', which is the question " +
            "Home has. Home cannot name a run: finding out whether there is one is the whole point " +
            "of the call.");
    }

    [Fact]
    public async Task StartAsync_hands_the_host_the_cancellation_token_it_was_given()
    {
        using var cancellation = new CancellationTokenSource();
        var host = RecordingGameHost.Finding(PlayerRow());
        var presenter = Home(host);

        await presenter.StartAsync(cancellation.Token);

        host.ReadToken.ShouldBe(
            cancellation.Token,
            "the token is how the app says 'the screen is going away, stop'. A presenter that " +
            "swallows it and passes None leaves a read running against a screen that is already " +
            "gone, and nothing can call it off.");
    }

    // ------------------------------------------------------- 🔒 the decision, state by state

    [Fact]
    public async Task StartAsync_decides_StartNewRun_when_the_player_is_in_no_run_at_all()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.StartNewRun,
            "a player between runs — which is every player who has just installed the game — has " +
            "nothing to resume, and the screen's only useful action is to send them to pick a chapter.");
    }

    /// <summary>
    /// 🔒 A separate case from "no run at all" although both end in StartNewRun. A finished run is a
    /// row that still exists and still comes back on the read; a player who has never started one
    /// has no row. A later change can break one without touching the other.
    /// </summary>
    [Fact]
    public async Task StartAsync_decides_StartNewRun_when_the_players_last_run_has_ended()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.Ended)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.StartNewRun,
            "a second run IS startable: the rules let START_RUN through on an ended run precisely so " +
            "it can clear the finished row, and nothing else in the game clears it. A presenter that " +
            "offered CONTINUE here would resume a run that is over and the player would be stuck on " +
            "the results screen with no way back.");
    }

    [Fact]
    public async Task StartAsync_decides_ContinueRun_when_the_players_run_is_in_progress()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.ContinueRun,
            "the player is standing on a board with perks drafted and gold banked. Offering START " +
            "here is offering to throw all of it away, and the button that does it looks exactly " +
            "like the one that resumes.");
    }

    /// <summary>
    /// 🔒 A separate case from InProgress. BattlePending is the phase a run persists <em>between</em>
    /// the two commands a fight resolves over, which is the state a player is most likely to be in
    /// when they background the app — and the one a phase check written as
    /// <c>phase == InProgress</c> would drop.
    /// </summary>
    [Fact]
    public async Task StartAsync_decides_ContinueRun_when_the_players_run_has_a_battle_open()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.BattlePending)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.ContinueRun,
            "a run with a battle open is a run in progress that happens to be mid-fight. Reading it " +
            "as 'not resumable' strands the player on the one phase they cannot leave by playing.");
    }

    /// <summary>
    /// 🔴 <b>A run left alone past its authored window is not continuable, and this case exists
    /// because the screen offered to continue one and stranded a real save.</b>
    /// </summary>
    /// <remarks>
    /// <c>GameRules</c> refuses every run command on a lapsed run with <c>RUN_EXPIRED</c>, and
    /// settles it only on the next command the player is ALLOWED to make — every meta command and
    /// <c>START_RUN</c>, deliberately not anything the board or its decision screens submit. So a
    /// Home that read the phase alone sent the player into a run where nothing worked: the observed
    /// failure was the perk draft, whose Pick, Reroll and Skip are all run commands, with no back
    /// control and the same offer waiting after a restart. `16` D70.
    /// </remarks>
    [Fact]
    public async Task StartAsync_refuses_to_continue_a_run_whose_window_has_passed()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress)),
            PlayerState.FixtureInstant.AddHours(ScreenContent.FixtureRunExpiryHours));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.RunLapsed,
            "the run's window has passed, so every command it would accept is refused. Offering to " +
            "resume it is offering a door that opens onto a wall.");

        presenter.ContinuableRun.ShouldBeNull(
            "and nothing may carry its id forward — an id here is what the next screen resumes on, " +
            "and there is nothing left to resume.");
    }

    /// <summary>
    /// 🔒 The negative control for the case above, and the reason it is one hour rather than a
    /// different fixture: a presenter that simply stopped offering CONTINUE would satisfy the
    /// expiry case perfectly.
    /// </summary>
    [Fact]
    public async Task StartAsync_still_continues_a_run_an_hour_short_of_its_window()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress)),
            PlayerState.FixtureInstant.AddHours(ScreenContent.FixtureRunExpiryHours - 1));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.ContinueRun,
            "inside the window the run is playable, and a screen that gave up on it early would " +
            "throw away a run the rules would still have accepted commands for.");
    }

    /// <summary>
    /// A battle left open lapses on the same clock. Stated separately because <c>BattlePending</c>
    /// is the phase a player is most likely to background the app in, so it is the one most likely
    /// to be found expired — and a phase check written for one of the two would miss it.
    /// </summary>
    [Fact]
    public async Task StartAsync_refuses_to_continue_a_lapsed_run_with_a_battle_open()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.BattlePending)),
            PlayerState.FixtureInstant.AddHours(ScreenContent.FixtureRunExpiryHours));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(HomeContinueDecision.RunLapsed);
    }

    /// <summary>
    /// The window comes from CONTENT, not from a number in the presenter.
    /// </summary>
    /// <remarks>
    /// 🔒 The fixture authors five hours where the shipped chapters author forty-eight, so a
    /// presenter carrying its own copy of the shipped figure reads this run as live and fails here.
    /// Without this case, every expiry case above passes against a hardcoded 48.
    /// </remarks>
    [Fact]
    public async Task StartAsync_reads_the_window_from_content_rather_than_carrying_one()
    {
        ScreenContent.FixtureRunExpiryHours.ShouldBeLessThan(
            48, "the fixture's window has to differ from the shipped one or this case proves nothing.");

        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress)),
            PlayerState.FixtureInstant.AddHours(ScreenContent.FixtureRunExpiryHours + 1));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.RunLapsed,
            "this run is older than the window THIS content authors. A presenter reading the shipped " +
            "48 hours would still be offering to continue it.");
    }

    /// <summary>
    /// A lapsed run offers the same button a fresh start does, and says why.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The BUTTON must start a run, because `03`'s expiry rule makes
    /// <c>START_RUN</c> the one command a lapsed run accepts — a screen offering anything else
    /// offers a refusal. The SENTENCE must be there, because a player who left a run going and
    /// returns to a fresh-start button has had something taken away, and silence about it reads as
    /// lost progress rather than as an authored window.
    /// </remarks>
    [Fact]
    public async Task A_lapsed_run_offers_the_start_action_and_says_what_happened()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress)),
            PlayerState.FixtureInstant.AddHours(ScreenContent.FixtureRunExpiryHours));

        await presenter.StartAsync(CancellationToken.None);

        presenter.ActionText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.StartRunActionKey),
            "START_RUN is the one command a lapsed run accepts, and it is what settles it. A button " +
            "saying anything else offers the player a command that will be refused.");

        presenter.StatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.RunLapsedStatusKey),
            "the run the player left is gone; a screen that changed its button and said nothing " +
            "reads as lost progress rather than as the two-day window the rules authored.");
    }

    [Fact]
    public async Task StartAsync_carries_the_id_of_the_run_it_decided_to_continue()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.ContinuableRun.ShouldBe(
            OpenRun,
            "CONTINUE has to resume THAT run. A decision with no id behind it makes the next screen " +
            "read the player's state a second time to find out which run it is looking at, on the " +
            "path a player takes every single session.");
    }

    [Fact]
    public async Task StartAsync_carries_no_run_to_continue_when_it_decided_to_start_a_new_one()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.Ended)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.ContinuableRun.ShouldBeNull(
            "the finished run's id is still in the answer the host gave. A presenter that carried it " +
            "forward anyway hands the next screen a resumable-looking id for a run that has ended.");
    }

    [Fact]
    public async Task StartAsync_decides_ProfileMissing_rather_than_StartNewRun_when_no_such_player_is_stored()
    {
        var presenter = Home(RecordingGameHost.FindingNoSuchPlayer());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.ProfileMissing,
            "'there is no row for you' is not 'you have no run yet'. Collapsing them puts a start " +
            "button in front of a player whose profile the device has lost, and the run they start " +
            "is written against a player that does not exist.");
    }

    [Fact]
    public async Task StartAsync_decides_ReadUnavailable_rather_than_throwing_when_the_read_faults()
    {
        var presenter = Home(RecordingGameHost.FaultingItsRead(ReadFailure()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.ReadUnavailable,
            "a failure is a state, never an escape. A presenter that let the exception out would " +
            "take it through an engine callback where nothing catches it, and the player would be " +
            "left looking at whatever the last frame drew.");
    }

    [Fact]
    public async Task StartAsync_carries_a_failure_detail_that_names_the_failure_that_actually_happened()
    {
        var presenter = Home(RecordingGameHost.FaultingItsRead(ReadFailure()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.FailureDetail!.ShouldContain(
            ReadFailureMessage,
            Case.Sensitive,
            "the detail has to identify the failure that happened, not merely be non-blank. A fixed " +
            "string like 'could not load' satisfies every 'is it set?' check and tells whoever is " +
            "holding the handset exactly as much as an empty one would.");
    }

    /// <summary>
    /// 🔒 The case that fails if the three no-run answers are ever collapsed. Compared against each
    /// other rather than against literals: an implementation reporting one shared value would
    /// satisfy every literal assertion elsewhere by simply being that literal.
    /// </summary>
    [Fact]
    public async Task The_three_ways_Home_can_answer_without_a_run_to_resume_carry_three_different_decisions()
    {
        var between = await DecisionFrom(RecordingGameHost.Finding(PlayerRow()));
        var missing = await DecisionFrom(RecordingGameHost.FindingNoSuchPlayer());
        var unavailable = await DecisionFrom(RecordingGameHost.FaultingItsRead(ReadFailure()));

        new[] { between, missing, unavailable }
            .Distinct()
            .Count()
            .ShouldBe(
                3,
                "three situations, three names. 'You can start a run', 'your profile is gone' and " +
                "'we could not ask' need three different screens and three different things said to " +
                "the player, and two of them collapsing means one of those screens can never be built.");
    }

    // -------------------------------------------------- the header, carried rather than derived

    [Fact]
    public async Task StartAsync_carries_the_display_name_the_snapshot_holds()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.DisplayName.ShouldBe(
            CarriedDisplayName,
            "the name is stored and never interpreted, so the header shows it as it is stored. " +
            "Anything else here is the screen editing a value the player chose.");
    }

    [Fact]
    public async Task StartAsync_carries_the_Legend_Level_the_snapshot_holds()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.LegendLevel.ShouldBe(
            CarriedLegendLevel,
            "the level is a stored number, not a function of lifetime XP evaluated here. A screen " +
            "that recomputed it would be holding a second copy of the Legend curve.");
    }

    /// <summary>
    /// 🔒 Both banks, exactly as the row carries them. The two amounts are the <em>whole</em> Energy
    /// readout on this screen: the maximum and the Reserve capacity are functions of Legend Level
    /// and tuning that this layer cannot evaluate, so there is no denominator to divide them by.
    /// </summary>
    [Fact]
    public async Task StartAsync_carries_both_Energy_amounts_exactly_as_the_snapshot_holds_them()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Energy.ShouldBe(
            CarriedEnergy,
            "the main bar's amount, unmodified. A presenter that clamped, scaled or rounded it would " +
            "be applying a rule the rules layer already applied.");
        presenter.EnergyReserve.ShouldBe(
            CarriedReserve,
            "and the Reserve behind it, separately. Two banks that every operation moves together " +
            "are still two numbers, and a screen showing their sum tells the player they have Energy " +
            "they cannot spend on a run.");
    }

    // ------------------------------------------------------ 🔒 the absences, pinned as facts

    /// <summary>
    /// 🔒 The values this screen must NOT produce, pinned so that filling one means deleting a case
    /// that says why it must not be filled.
    /// </summary>
    /// <remarks>
    /// Every tuning reader that could answer these is <c>internal</c> to the rules assembly and the
    /// host seam exposes no derived-value read, so a number produced here would be a second copy of
    /// a formula the rules already own — and two copies of a balance formula disagree the first time
    /// either is tuned. The layout the design calls for (an Energy bar with a maximum and a
    /// regeneration countdown, a Legend XP percentage, a run's Energy cost) is therefore left absent
    /// and greppable rather than filled with a plausible number.
    /// <para>
    /// A substring match, because what is forbidden is the <em>idea</em>: <c>MaxEnergy</c>,
    /// <c>EnergyDenominator</c>, <c>RegenSecondsRemaining</c>, <c>LegendXpPercent</c> and
    /// <c>RunEnergyCost</c> are the same mistake spelled five ways.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Max")]
    [InlineData("Denominator")]
    [InlineData("Regen")]
    [InlineData("Countdown")]
    [InlineData("Percent")]
    [InlineData("Cost")]
    public void The_home_screen_exposes_no_member_naming_a_value_only_the_rules_layer_can_compute(string fragment)
    {
        HomeMemberNames()
            .Where(n => n.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty(
                $"a member naming '{fragment}' would be an Energy maximum, a regeneration countdown, " +
                "a Legend-XP percentage or a run's Energy cost computed on the screen. Each is a " +
                "function of tuning that only the rules layer can evaluate, and a client-side copy " +
                "of it goes wrong silently on the first balance patch. If a later task genuinely " +
                "gets one of these through the host seam, deleting this case is the deliberate act " +
                "that records the change.");
    }

    /// <summary>
    /// The floor under the rule above: it is stated over a member set that really is the screen's.
    /// </summary>
    /// <remarks>
    /// Named members rather than a count, so a renamed property fails here rather than quietly
    /// shrinking the set the absence rule scans.
    /// </remarks>
    [Fact]
    public void The_absence_rule_is_stated_over_the_members_the_home_screen_actually_exposes()
    {
        var members = HomeMemberNames();

        members.ShouldContain(nameof(HomePresenter.Energy), "the amount the absent maximum would have divided");
        members.ShouldContain(nameof(HomePresenter.EnergyReserve), "the other bank, likewise without a capacity");
        members.ShouldContain(nameof(HomePresenter.LegendLevel), "the level the absent XP percentage would have sat under");
        members.ShouldContain(nameof(HomePresenter.Decision), "the decision the whole screen exists to make");
    }

    // -------------------------------------------------------------------------- the strings

    /// <summary>
    /// 🔒 Every caption is resolved, never written. Stated over the whole set at once because the
    /// failure is a property of the screen rather than of any one label.
    /// </summary>
    [Fact]
    public async Task Every_caption_the_home_screen_renders_comes_from_the_catalogue_rather_than_a_literal()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow()));

        await presenter.StartAsync(CancellationToken.None);

        new[]
        {
            presenter.LegendLevelLabel, presenter.EnergyLabel,
            presenter.EnergyReserveLabel, presenter.ActionText,
        }.ShouldBe(
            new[]
            {
                ScreenContent.EnglishValueOf(ScreenContent.LegendLevelLabelKey),
                ScreenContent.EnglishValueOf(ScreenContent.EnergyLabelKey),
                ScreenContent.EnglishValueOf(ScreenContent.EnergyReserveLabelKey),
                ScreenContent.EnglishValueOf(ScreenContent.StartRunActionKey),
            },
            "an English literal in the presenter renders identically to a resolved English string on " +
            "every English handset, and ships a German build in English. Held against fixture values " +
            "no reasonable literal would ever be, so the only way to satisfy this is to go through " +
            "the catalogue.");
    }

    [Fact]
    public async Task ActionText_is_the_continue_caption_rather_than_the_start_caption_when_a_run_can_be_resumed()
    {
        var presenter = Home(
            RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.ActionText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.ContinueRunActionKey),
            "the button is the same button in both states and it does two different things. A screen " +
            "that says 'Start Run' and resumes, or says 'Continue' and discards, is a screen whose " +
            "one control cannot be trusted.");
    }

    /// <summary>
    /// 🔒 The status line is authored for the two states that have nothing to offer, and a settled
    /// screen is neither of them.
    /// </summary>
    /// <remarks>
    /// Without this the loading line is only ever asserted to be <em>present</em> at the start, and
    /// a presenter that never cleared it would satisfy every case here while leaving "reading your
    /// profile…" under a working button for the rest of the session. The content document authors
    /// exactly two status strings — loading and unavailable — so a screen with a decision to offer
    /// has no third line to show and must show none.
    /// </remarks>
    [Fact]
    public async Task StatusText_is_cleared_once_the_read_has_settled_on_a_decision()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.StatusText.ShouldBeEmpty(
            "the read answered and the button says what it does, so there is nothing left for a " +
            "status line to say. Leaving the loading line up is the state that reads as a screen " +
            "still working on something, next to a control that is already live.");
    }

    [Fact]
    public async Task StatusText_is_the_unavailable_line_when_no_such_player_is_stored()
    {
        var presenter = Home(RecordingGameHost.FindingNoSuchPlayer());

        await presenter.StartAsync(CancellationToken.None);

        presenter.StatusText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.UnavailableStatusKey),
            "leaving the loading line up is the state that looks exactly like a screen still " +
            "working, and the player waits for something that is never coming.");
    }

    // ------------------------------------------------------------------------- null guards

    [Fact]
    public void Constructor_rejects_a_null_game_host()
    {
        Should.Throw<ArgumentNullException>(
                  () => new HomePresenter(
                      gameHost: null!,
                      ScreenContent.Catalogue(),
                      ScreenContent.Strings(),
                      new ManualClock(Now),
                      Profile))
              .ParamName.ShouldBe(
                  "gameHost",
                  "a null collaborator turns into a NullReferenceException at whichever line touches " +
                  "it first, which here is inside the read — so the screen would report the read as " +
                  "unavailable and hide a composition root that was never wired.");
    }

    [Fact]
    public void Constructor_rejects_a_null_string_catalogue()
    {
        Should.Throw<ArgumentNullException>(
                  () => new HomePresenter(
                      RecordingGameHost.Finding(PlayerRow()),
                      strings: null!,
                      ScreenContent.Strings(),
                      new ManualClock(Now),
                      Profile))
              .ParamName.ShouldBe(
                  "strings",
                  "without the catalogue there are no captions at all, and the first thing that would " +
                  "break is the loading frame — before the read has run and before there is anywhere " +
                  "to report it.");
    }

    // ---------------------------------------------------------------------------- fixtures

    private static InvalidOperationException ReadFailure() => new(ReadFailureMessage);

    private static PlayerSnapshot PlayerRow() =>
        PlayerState.Player(
            Profile,
            CarriedDisplayName,
            CarriedLegendLevel,
            CarriedEnergy,
            CarriedReserve);

    /// <summary>The instant every case here reads "now" as, unless it says otherwise.</summary>
    /// <remarks>
    /// 🔒 Anchored to the instant the fixture stamps a run at, one hour on — so every case that is
    /// not ABOUT expiry has a run comfortably inside its window and keeps meaning what it meant.
    /// Fixed rather than <c>UtcNow</c> for the obvious reason: Home decides against the clock now,
    /// and a case built on the real one measures a run's age against whenever the suite happened to
    /// run, which is a case that passes today and fails on a slow morning.
    /// </remarks>
    private static readonly DateTimeOffset Now = PlayerState.FixtureInstant.AddHours(1);

    private static HomePresenter Home(RecordingGameHost host) =>
        Home(host, Now);

    private static HomePresenter Home(RecordingGameHost host, DateTimeOffset nowUtc)
    {
        var content = ScreenContent.Strings();

        return new HomePresenter(
            host, ScreenContent.Catalogue(content), content, new ManualClock(nowUtc), Profile);
    }

    private static async Task<HomeContinueDecision> DecisionFrom(RecordingGameHost host)
    {
        var presenter = Home(host);

        await presenter.StartAsync(CancellationToken.None);

        return presenter.Decision;
    }

    private static IReadOnlyList<string> HomeMemberNames() =>
        typeof(HomePresenter)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();
}
