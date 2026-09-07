using Shouldly;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
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

    private const long CarriedCrowns = 8_431;

    private const long CarriedSoulShards = 19;

    // Five distinct values, each one past the edge PlayerNumber writes out in full, so the five
    // tile texts cannot be told apart by a shared literal and none of them is exact by accident.
    private const int LongEnergy = 30_003;

    private const int LongReserve = 40_004;

    private const long LongCrowns = 10_001;

    private const long LongSoulShards = 20_002;

    private const long LongGold = 50_005;

    private static readonly int[] TwoAuthoredChapters = [1, 2];

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

    /// <summary>
    /// 🔴 <b>Every decision that has a profile behind it says so — and this exists because the
    /// screen kept its own copy of this list and the copy went stale.</b>
    /// </summary>
    /// <remarks>
    /// <c>Home.cs</c> enumerated <c>StartNewRun or ContinueRun</c> to decide both the header's
    /// visibility and the primary action's disabled state. <see cref="HomeContinueDecision.RunLapsed"/>
    /// matched neither, so the screen hid a profile it had and disabled the one action that settles
    /// a lapsed run — leaving the player stranded on Home rather than on the perk draft, which is
    /// the same defect `16` D70 fixed one screen further down. Stated over EVERY declared member so
    /// a seventh cannot be added without answering for it here.
    /// </remarks>
    [Theory]
    [InlineData(HomeContinueDecision.StartNewRun, true)]
    [InlineData(HomeContinueDecision.ContinueRun, true)]
    [InlineData(HomeContinueDecision.RunLapsed, true)]
    [InlineData(HomeContinueDecision.NotYetRead, false)]
    [InlineData(HomeContinueDecision.ProfileMissing, false)]
    [InlineData(HomeContinueDecision.ReadUnavailable, false)]
    public async Task Every_decision_says_whether_a_profile_is_carried(
        HomeContinueDecision decision, bool carried)
    {
        var presenter = await PresenterDeciding(decision);

        presenter.Decision.ShouldBe(
            decision, "the fixture must actually reach the decision this case is about.");

        presenter.ProfileCarried.ShouldBe(
            carried,
            carried
                ? $"{decision} has a profile behind it and an action to take, so the header must draw " +
                  "its numbers and the primary action must be pressable."
                : $"{decision} has no profile, so drawing a Legend Level and an Energy of zero would " +
                  "be plausible values in a hole.");
    }

    /// <summary>
    /// The floor under the case above: it is stated over every member the enum declares, so a
    /// seventh cannot slip past by simply not being listed.
    /// </summary>
    [Fact]
    public void The_profile_case_covers_every_decision_the_enum_declares() =>
        Enum.GetValues<HomeContinueDecision>().Length.ShouldBe(
            6,
            "HomeContinueDecision has gained or lost a member. Add it to " +
            $"{nameof(Every_decision_says_whether_a_profile_is_carried)}'s rows and answer whether it " +
            "carries a profile — a member absent from those rows is a member nothing asks about, " +
            "which is exactly how RunLapsed shipped hiding the header and disabling the button.");

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

    // ---------------------------------------- 🔴 what USED to be pinned as absent, and is not
    //
    // Two cases lived here: one forbidding any member named Max / Denominator / Regen / Countdown /
    // Percent / Cost, and its floor. Their own words said "if a later task genuinely gets one of
    // these through the host seam, deleting this case is the deliberate act that records the
    // change." That task is this one: HomeEnergyView is the public projection the rules layer now
    // offers, IHomeScreen carries its numbers, and the screen below reads a cost badge and a
    // maximum it does not compute. Deleted rather than weakened — a narrowed rule stating the same
    // prohibition with exceptions would be a rule nobody could read.


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
            presenter.CrownsLabel, presenter.SoulShardsLabel, presenter.GoldLabel,
            presenter.PowerLabel, presenter.ProgressLabel, presenter.StageLabel,
        }.ShouldBe(
            new[]
            {
                ScreenContent.EnglishValueOf(ScreenContent.LegendLevelLabelKey),
                ScreenContent.EnglishValueOf(ScreenContent.EnergyLabelKey),
                ScreenContent.EnglishValueOf(ScreenContent.EnergyReserveLabelKey),
                ScreenContent.EnglishValueOf(ScreenContent.StartRunActionKey),
                ScreenContent.EnglishValueOf(ScreenContent.CrownsNameKey),
                ScreenContent.EnglishValueOf(ScreenContent.SoulShardsNameKey),
                ScreenContent.EnglishValueOf(ScreenContent.GoldNameKey),
                ScreenContent.EnglishValueOf(ScreenContent.PowerLabelKey),
                ScreenContent.EnglishValueOf(ScreenContent.ProgressLabelKey),
                ScreenContent.EnglishValueOf(ScreenContent.StageLabelKey),
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

    // ------------------------------------------------------------- the wallet tiles

    [Fact]
    public async Task StartAsync_carries_both_wallet_balances_exactly_as_the_snapshot_holds_them()
    {
        var row = PlayerRow() with { Wallet = Wallet(CarriedCrowns, CarriedSoulShards) };
        var presenter = Home(RecordingGameHost.Finding(row));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Crowns.ShouldBe(
            CarriedCrowns,
            "the Crowns tile shows the balance the row holds. A screen that read a different currency's " +
            "row, or a default for a missing one, shows a plausible number for the wrong bank.");
        presenter.SoulShards.ShouldBe(
            CarriedSoulShards,
            "and Soul Shards separately: two player-scoped currencies with two tiles, never summed and " +
            "never one read for the other.");
    }

    [Fact]
    public async Task A_wallet_with_no_row_for_a_currency_reads_as_a_balance_of_zero()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(
            HomeContinueDecision.StartNewRun,
            "a presenter indexing the wallet would throw on the missing row and StartAsync would " +
            "report that as ReadUnavailable — leaving the zeros below as untouched defaults.");
        presenter.Crowns.ShouldBe(0L, "a currency the wallet holds no row for is a balance of nothing, not a fault.");
        presenter.SoulShards.ShouldBe(0L);
    }

    /// <summary>`16`'s number rule at its edge: ten thousand is written out, ten thousand and one is not.</summary>
    [Theory]
    [InlineData(9_999L, "9999")]
    [InlineData(10_000L, "10000")]
    [InlineData(10_001L, "10.0k")]
    public async Task CrownsText_is_exact_up_to_ten_thousand_and_shortened_above_it(long crowns, string expected)
    {
        var row = PlayerRow() with { Wallet = Wallet(crowns, CarriedSoulShards) };
        var presenter = Home(RecordingGameHost.Finding(row));

        await presenter.StartAsync(CancellationToken.None);

        presenter.CrownsText.ShouldBe(
            expected,
            "a tile 468 units wide holds five digits and not seven, and the design's own rule puts " +
            "the edge at ten thousand inclusive. A presenter shortening at 1 000, or at 9 999, " +
            "breaks the one example the rule is stated by.");
    }

    /// <summary>
    /// 🔒 Stated over all four tile numbers at once, so a screen that shortened Crowns and forgot
    /// Energy — which is an int and tempting to print raw — fails here rather than on a handset.
    /// </summary>
    [Fact]
    public async Task Every_tile_number_is_shortened_by_the_same_rule()
    {
        var presenter = Home(RecordingGameHost.Finding(RichRow()));

        await presenter.StartAsync(CancellationToken.None);

        new[]
        {
            presenter.CrownsText, presenter.SoulShardsText, presenter.EnergyText, presenter.EnergyReserveText,
        }.ShouldBe(
            new[] { "10.0k", "20.0k", "30.0k", "40.0k" },
            "four tiles, one rule. A tile printing its number raw is the one whose text overflows " +
            "the moment a player passes ten thousand of anything.");
    }

    [Fact]
    public async Task RevealFullValues_shows_every_tile_number_in_full()
    {
        var presenter = Home(RecordingGameHost.Finding(RichRow()));
        await presenter.StartAsync(CancellationToken.None);

        presenter.RevealFullValues();

        presenter.FullValuesRevealed.ShouldBeTrue("the flag the scene reads is the state the texts below are computed from.");
        new[]
        {
            presenter.CrownsText, presenter.SoulShardsText, presenter.EnergyText, presenter.EnergyReserveText,
        }.ShouldBe(
            new[] { "10001", "20002", "30003", "40004" },
            "a long-press is how a player asks 'how many, exactly?', and the answer is the exact " +
            "digits — of every tile, since the gesture is one state of the screen and not of one tile.");
    }

    [Fact]
    public async Task ConcealFullValues_shortens_the_tile_numbers_again()
    {
        var presenter = Home(RecordingGameHost.Finding(RichRow()));
        await presenter.StartAsync(CancellationToken.None);
        presenter.RevealFullValues();

        presenter.ConcealFullValues();

        presenter.FullValuesRevealed.ShouldBeFalse("the release of the press is the end of the reveal.");
        presenter.CrownsText.ShouldBe(
            "10.0k",
            "a reveal that never ends leaves seven digits in a tile authored for five, and the next " +
            "long-press has nothing left to reveal.");
    }

    // -------------------------------------------------------------- the run panel's gold

    [Fact]
    public async Task RunGold_is_the_runs_own_balance_when_the_run_can_be_continued()
    {
        var presenter = Home(RecordingGameHost.Finding(PlayerRow(), OpenRunRow(gold: LongGold)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.RunGold.ShouldBe(
            LongGold,
            "Gold is run-scoped: the number on the panel is the run row's, not a wallet row's.");
        presenter.RunGoldText.ShouldBe(
            "50.0k", "and it is shortened by the same rule as every other tile number.");
    }

    /// <summary>
    /// The screen is re-read when a run ends and Home comes back. The second read is what has to
    /// take the run panel down.
    /// </summary>
    [Fact]
    public async Task A_second_read_after_the_run_has_ended_clears_the_run_panel()
    {
        var host = RecordingGameHost
            .Finding(PlayerRow(), OpenRunRow(gold: LongGold))
            .ThenFinding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.Ended, gold: LongGold));
        var presenter = Home(host);
        await presenter.StartAsync(CancellationToken.None);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(HomeContinueDecision.StartNewRun, "the ended run is a run to start over");
        presenter.RunProgress.ShouldBeNull(
            "the panel is drawn whenever RunProgress is set, so a value left over from the first read " +
            "puts a finished run's stage on a screen whose button now says START.");
        presenter.RunGold.ShouldBeNull("the Gold of a run that has been paid out is nobody's balance now.");
        presenter.ContinuableRun.ShouldBeNull("and there is no run to hand the board on the next press.");
    }

    /// <summary>
    /// 🔒 Both non-continuable decisions with a profile behind them, because <c>RunLapsed</c> still
    /// HAS a run row with a Gold balance in it — and that balance belongs to a run nobody can play.
    /// </summary>
    [Theory]
    [InlineData(HomeContinueDecision.StartNewRun)]
    [InlineData(HomeContinueDecision.RunLapsed)]
    public async Task RunGold_is_absent_when_there_is_no_run_to_continue(HomeContinueDecision decision)
    {
        var presenter = await PresenterDeciding(decision);

        presenter.Decision.ShouldBe(decision, "the fixture must actually reach the decision this case is about.");

        presenter.RunGold.ShouldBeNull(
            $"{decision} offers no run to continue, so there is no run whose Gold this could be. A " +
            "lapsed run's row still carries a balance, and showing it promises the player Gold they " +
            "will never spend.");
        presenter.RunGoldText.ShouldBeEmpty(
            "and the text has to be blank rather than '0': the panel is hidden, but a blank is what " +
            "a hidden panel reads when a later change shows it by mistake.");
    }

    // ------------------------------------------------------------------- the power tile

    /// <summary>
    /// 🔒 Floor, not round. 123.9 rounds to 124 and floors to 123, so a presenter using either of the
    /// obvious wrong calls fails here.
    /// </summary>
    [Fact]
    public async Task Power_is_the_floor_of_the_index_rather_than_its_rounding()
    {
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow()), StubHeroPowerSource.Computing(123.9));

        presenter.Power.ShouldBe(
            123.0,
            "the tile shows the power a player HAS, and rounding up shows a number they have not " +
            "reached — the same way a level is the one completed, not the one nearest.");
        presenter.PowerText.ShouldBe("123", "and the text is that whole number, with nothing after it.");
    }

    [Theory]
    [InlineData(HeroPowerStanding.BuildNotAggregable)]
    [InlineData(HeroPowerStanding.RowNotRehydratable)]
    [InlineData(HeroPowerStanding.LegendLevelOutsideCurve)]
    [InlineData(HeroPowerStanding.ContentUnavailable)]
    public async Task Power_is_absent_rather_than_zero_when_the_source_could_not_compute_one(
        HeroPowerStanding standing)
    {
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow()), StubHeroPowerSource.Standing(standing));

        presenter.Decision.ShouldBe(
            HomeContinueDecision.StartNewRun,
            "the read settled, so the absence below is the source's answer and not a failure StartAsync swallowed.");
        presenter.Power.ShouldBeNull(
            $"{standing} means no number exists, and a null is the only honest value for one.");
        presenter.PowerText.ShouldBeEmpty(
            "a power of '0' is a real reading — a hero with nothing — and is not what happened here. " +
            "An empty tile says 'no reading'; a zero says 'you are worthless', to a player whose " +
            "hero is fine.");
    }

    [Theory]
    [InlineData(HeroPowerStanding.Computed)]
    [InlineData(HeroPowerStanding.BuildNotAggregable)]
    [InlineData(HeroPowerStanding.RowNotRehydratable)]
    [InlineData(HeroPowerStanding.LegendLevelOutsideCurve)]
    [InlineData(HeroPowerStanding.ContentUnavailable)]
    public async Task PowerStanding_is_the_standing_the_source_reported(HeroPowerStanding standing)
    {
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow()), SourceReporting(standing));

        presenter.PowerStanding.ShouldBe(
            standing,
            "an empty power tile has four different causes and the scene has to be able to say which. " +
            "A presenter collapsing them to 'absent' leaves whoever holds the handset with a blank " +
            "and no way to tell a content fault from a row the domain refused.");
    }

    [Fact]
    public async Task StartAsync_reads_power_over_the_run_being_continued()
    {
        var row = PlayerRow();
        var run = OpenRunRow();
        var power = DefaultPower();

        await Started(RecordingGameHost.Finding(row, run), power);

        power.LastPlayer.ShouldBe(row, "the hero is composed from this profile's row and no other.");
        power.LastRun.ShouldBe(
            run,
            "inside a run the hero wears the loadout the run froze and the perks it drafted, and a " +
            "source handed no run composes the between-runs hero — a plausible number for the wrong hero.");
    }

    /// <summary>
    /// 🔒 <c>RunLapsed</c> is the case that matters: the row still carries a run, and that run's perks
    /// are drafted for a board nobody will stand on again.
    /// </summary>
    [Theory]
    [InlineData(HomeContinueDecision.StartNewRun)]
    [InlineData(HomeContinueDecision.RunLapsed)]
    public async Task StartAsync_reads_power_with_no_run_when_there_is_none_to_continue(HomeContinueDecision decision)
    {
        var power = DefaultPower();
        var presenter = await PresenterDeciding(decision, power);

        presenter.Decision.ShouldBe(decision, "the fixture must actually reach the decision this case is about.");

        power.ReadCallCount.ShouldBe(1, "an anchor for the claim below, which an unread source satisfies vacuously.");
        power.LastRun.ShouldBeNull(
            $"{decision} has no run to continue, so the hero shown is the one the player will start " +
            "the next run as — composed over the profile's own loadout, with no drafted perks.");
    }

    [Theory]
    [InlineData(HomeContinueDecision.ProfileMissing)]
    [InlineData(HomeContinueDecision.ReadUnavailable)]
    public async Task StartAsync_reads_no_power_when_the_read_produced_no_profile(HomeContinueDecision decision)
    {
        var power = DefaultPower();
        var presenter = await PresenterDeciding(decision, power);

        presenter.Decision.ShouldBe(decision, "the fixture must actually reach the decision this case is about.");

        power.ReadCallCount.ShouldBe(
            0,
            $"{decision} has no row to compose a hero from, so there is nothing to ask the source about.");
        presenter.Power.ShouldBeNull("and the tile has nothing to show.");
        presenter.PowerText.ShouldBeEmpty("blank, not a stale or default number, on a screen with no profile.");
    }

    // ---------------------------------------------------------------- the progress tile

    /// <summary>
    /// 🔒 Chapter beats tier. Chapter 1 at Mythic is a harder clear than chapter 2 at Normal and a
    /// presenter ordering by tier first would name it — but the tile answers "how far", not "how hard".
    /// </summary>
    [Fact]
    public async Task HighestClear_is_the_furthest_chapter_cleared_at_any_tier()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(ClearedRow((1, DifficultyTier.MYTHIC), (2, DifficultyTier.NORMAL))),
            ScreenContent.Authoring(TwoAuthoredChapters));

        presenter.HighestClear.ShouldBe(
            new HighestChapterClear(2, ScreenContent.EnglishValueOf(ScreenContent.ChapterNameKey(2)), DifficultyTier.NORMAL),
            "the furthest chapter reached, at the highest tier it was reached at.");
        presenter.HighestClearText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.ChapterNameKey(2)) + " · " + TierName(DifficultyTier.NORMAL),
            "chapter name, a middle dot, tier name — both halves resolved through the catalogue, " +
            "since a chapter's name and a tier's name are both authored strings.");
    }

    [Fact]
    public async Task HighestClear_ignores_a_clear_of_a_chapter_the_content_does_not_author()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(ClearedRow((1, DifficultyTier.NORMAL), (7, DifficultyTier.MYTHIC))),
            ScreenContent.Authoring(TwoAuthoredChapters));

        presenter.HighestClear.ShouldNotBeNull().ChapterId.ShouldBe(
            1,
            "a clear of a chapter this content set does not author has no name to show and no " +
            "row to look up — a retired chapter, or a row written by a newer build. It is skipped, " +
            "so the tile names a chapter the player can actually find.");
    }

    [Fact]
    public async Task HighestClearText_is_the_nothing_cleared_line_when_no_chapter_has_been_cleared()
    {
        var presenter = await Started(
            RecordingGameHost.Finding(PlayerRow()), ScreenContent.Authoring(TwoAuthoredChapters));

        presenter.Decision.ShouldBe(
            HomeContinueDecision.StartNewRun,
            "the read settled, so the absence below is a fresh profile's and not a failure StartAsync swallowed.");
        presenter.HighestClear.ShouldBeNull("nothing cleared is nothing, not chapter zero.");
        presenter.HighestClearText.ShouldBe(
            ScreenContent.EnglishValueOf(ScreenContent.NothingClearedStatusKey),
            "a new player's progress tile says so in words rather than sitting blank next to five " +
            "tiles that have numbers in them.");
    }

    // ------------------------------------------------------------------- the run panel

    /// <summary>
    /// Over the checkout's own content, because the stage a run stands in is read off a board that
    /// only the shipped chapter tuning can generate.
    /// </summary>
    [Fact]
    public async Task RunStageText_is_the_stage_the_run_stands_in_over_the_three_a_chapter_has()
    {
        var run = OpenRunRow(position: NodeInStage(2));
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow(), run), BootContent.Shipped);

        presenter.RunStageText.ShouldBe(
            "2/3",
            "the stage is the node's, read off the board — a presenter answering '1/3' for every " +
            "open run passes at the trailhead and is caught here.");
    }

    [Fact]
    public async Task RunHitPointsText_is_current_over_maximum_written_in_full()
    {
        var run = OpenRunRow(currentHp: 10_001, maxHp: 12_345);
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow(), run), BootContent.Shipped);

        presenter.RunHitPointsText.ShouldBe(
            "10001/12345",
            "both past the shortening edge and both written out: a health readout of '10.0k/12.3k' " +
            "hides the one number a player checks before continuing a run.");
    }

    /// <summary>
    /// 🔒 The run row is still there and still projects a board; only the decision says it is not
    /// the player's to continue.
    /// </summary>
    [Fact]
    public async Task RunProgress_is_absent_when_the_last_run_has_ended()
    {
        var ended = PlayerState.Run(OpenRun, Profile, RunPhase.Ended);
        var presenter = await Started(RecordingGameHost.Finding(PlayerRow(), ended), BootContent.Shipped);

        presenter.Decision.ShouldBe(HomeContinueDecision.StartNewRun, "the fixture must actually reach the decision this case is about.");
        presenter.RunProgress.ShouldBeNull("a finished run stands nowhere the player can go back to.");
        presenter.RunStageText.ShouldBeEmpty("blank rather than '1/3' on a screen whose run panel is hidden.");
        presenter.RunHitPointsText.ShouldBeEmpty("and no hit points either — there is no hero in a fight.");
    }

    [Fact]
    public async Task RunProgress_is_absent_when_the_open_run_has_lapsed()
    {
        var aYearOn = PlayerState.FixtureInstant.AddYears(1);
        var presenter = Home(RecordingGameHost.Finding(PlayerRow(), OpenRunRow()), aYearOn, BootContent.Shipped, DefaultPower());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Decision.ShouldBe(HomeContinueDecision.RunLapsed, "a run left alone for a year has lapsed under any authored window.");
        presenter.RunProgress.ShouldBeNull("the row still projects a board, and nobody will stand on it again.");
        presenter.RunStageText.ShouldBeEmpty();
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
                      DefaultPower(),
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
                      DefaultPower(),
                      Profile))
              .ParamName.ShouldBe(
                  "strings",
                  "without the catalogue there are no captions at all, and the first thing that would " +
                  "break is the loading frame — before the read has run and before there is anywhere " +
                  "to report it.");
    }

    [Fact]
    public void Constructor_rejects_a_null_power_source()
    {
        Should.Throw<ArgumentNullException>(
                  () => new HomePresenter(
                      RecordingGameHost.Finding(PlayerRow()),
                      ScreenContent.Catalogue(),
                      ScreenContent.Strings(),
                      new ManualClock(Now),
                      power: null!,
                      Profile))
              .ParamName.ShouldBe(
                  "power",
                  "the power source is first touched inside the read, where every failure is caught " +
                  "and reported as ReadUnavailable — so a composition root that never wired it would " +
                  "show up as a profile that could not be read, on every handset, with nothing naming " +
                  "the missing collaborator.");
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

    /// <summary>The index the default power source answers — a number no case is about.</summary>
    private const double AnyPowerIndex = 456.5;

    private static StubHeroPowerSource DefaultPower() => StubHeroPowerSource.Computing(AnyPowerIndex);

    /// <summary>A source reading the given standing — with a number when, and only when, it computed one.</summary>
    private static StubHeroPowerSource SourceReporting(HeroPowerStanding standing) =>
        standing == HeroPowerStanding.Computed ? DefaultPower() : StubHeroPowerSource.Standing(standing);

    private static HomePresenter Home(RecordingGameHost host) =>
        Home(host, Now);

    private static HomePresenter Home(RecordingGameHost host, DateTimeOffset nowUtc) =>
        Home(host, nowUtc, ScreenContent.Strings(), DefaultPower());

    private static HomePresenter Home(RecordingGameHost host, StubHeroPowerSource power) =>
        Home(host, Now, ScreenContent.Strings(), power);

    private static HomePresenter Home(RecordingGameHost host, ContentSnapshot content) =>
        Home(host, Now, content, DefaultPower());

    private static HomePresenter Home(
        RecordingGameHost host, DateTimeOffset nowUtc, ContentSnapshot content, StubHeroPowerSource power) =>
        new(host, ScreenContent.Catalogue(content), content, new ManualClock(nowUtc), power, Profile);

    private static async Task<HomePresenter> Started(RecordingGameHost host, StubHeroPowerSource power)
    {
        var presenter = Home(host, power);

        await presenter.StartAsync(CancellationToken.None);

        return presenter;
    }

    private static async Task<HomePresenter> Started(RecordingGameHost host, ContentSnapshot content)
    {
        var presenter = Home(host, content);

        await presenter.StartAsync(CancellationToken.None);

        return presenter;
    }

    /// <summary>A row whose two banks and two wallet balances all sit above the shortening edge.</summary>
    private static PlayerSnapshot RichRow() =>
        PlayerState.Player(Profile, CarriedDisplayName, CarriedLegendLevel, LongEnergy, LongReserve) with
        {
            Wallet = Wallet(LongCrowns, LongSoulShards),
        };

    private static Dictionary<CurrencyId, long> Wallet(long crowns, long soulShards) => new()
    {
        [CurrencyId.CROWNS] = crowns,
        [CurrencyId.SOUL_SHARDS] = soulShards,
    };

    private static PlayerSnapshot ClearedRow(params (int Chapter, DifficultyTier Tier)[] cleared) =>
        PlayerState.Player(Profile, clearedChapterTiers: PlayerState.Cleared(cleared));

    private static RunSnapshot OpenRunRow(long gold = 0, int position = -1, int currentHp = 100, int maxHp = 100) =>
        PlayerState.Run(OpenRun, Profile, RunPhase.InProgress, position: position, currentHp: currentHp, maxHp: maxHp, gold: gold);

    /// <summary>A node in the given stage of the board chapter 1 generates from the fixture run's seed.</summary>
    private static int NodeInStage(int stage) =>
        BoardView.Project(OpenRunRow(), BootContent.Shipped).Spine.First(node => node.Stage == stage).NodeId;

    private static string TierName(DifficultyTier tier) => tier switch
    {
        DifficultyTier.NORMAL => ScreenContent.EnglishValueOf(ScreenContent.TierNormalKey),
        DifficultyTier.HEROIC => ScreenContent.EnglishValueOf(ScreenContent.TierHeroicKey),
        _ => ScreenContent.EnglishValueOf(ScreenContent.TierMythicKey),
    };

    /// <summary>A started presenter that has settled on one particular decision.</summary>
    /// <remarks>
    /// Each arm reaches the decision the way the screen really reaches it, rather than setting it —
    /// so a case over these is a case over states the read can actually produce. <c>NotYetRead</c>
    /// is the one that is not started at all, because that is precisely what it means.
    /// </remarks>
    private static Task<HomePresenter> PresenterDeciding(HomeContinueDecision decision) =>
        PresenterDeciding(decision, DefaultPower());

    private static async Task<HomePresenter> PresenterDeciding(HomeContinueDecision decision, StubHeroPowerSource power)
    {
        if (decision == HomeContinueDecision.NotYetRead)
        {
            return Home(RecordingGameHost.Finding(PlayerRow()), power);
        }

        var (host, nowUtc) = decision switch
        {
            HomeContinueDecision.StartNewRun =>
                (RecordingGameHost.Finding(PlayerRow()), Now),
            HomeContinueDecision.ContinueRun =>
                (RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress)), Now),
            HomeContinueDecision.RunLapsed =>
                (RecordingGameHost.Finding(PlayerRow(), PlayerState.Run(OpenRun, Profile, RunPhase.InProgress)),
                 PlayerState.FixtureInstant.AddHours(ScreenContent.FixtureRunExpiryHours)),
            HomeContinueDecision.ProfileMissing =>
                (RecordingGameHost.FindingNoSuchPlayer(), Now),
            _ =>
                (RecordingGameHost.FaultingItsRead(ReadFailure()), Now),
        };

        var presenter = Home(host, nowUtc, ScreenContent.Strings(), power);

        await presenter.StartAsync(CancellationToken.None);

        return presenter;
    }

    private static async Task<HomeContinueDecision> DecisionFrom(RecordingGameHost host)
    {
        var presenter = Home(host);

        await presenter.StartAsync(CancellationToken.None);

        return presenter.Decision;
    }
    // =============================================== the run hub's launch block, state by state

    /// <summary>The state a presenter is in before the read answers: skeletons, not a blank screen.</summary>
    [Fact]
    public void LaunchState_is_Loading_before_the_view_model_has_been_read() =>
        Hub(Ready()).LaunchState.ShouldBe(
            HomeLaunchState.Loading,
            "the first frame is drawn before the read lands, and a default of Ready offers a start " +
            "button whose price nobody has read yet.");

    /// <summary>Energy covers the cost and nothing is wrong: the ember button, and the price on it.</summary>
    [Fact]
    public async Task LaunchState_is_Ready_when_the_banks_cover_the_run_cost()
    {
        var presenter = await LoadedHub(Ready());

        presenter.LaunchState.ShouldBe(HomeLaunchState.Ready);
        presenter.ActionColour.ShouldBe(HomeColourRole.Action);
        presenter.CostBadge.ShouldBe(
            new HomeCostBadge(HomeCostBadgeKind.Price, RunCost),
            "a ready button quotes what the run costs, which is the number that will be taken.");
        presenter.CanStartRun.ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 A shortfall the banks cannot cover becomes a refill offer, in the energy accent, quoting
    /// the SHORTFALL.
    /// </summary>
    /// <remarks>
    /// The shortfall rather than the price is the discriminating half: a badge showing 20 to a
    /// player holding 17 tells them to find twenty more. The bar here is short by three and the
    /// shortfall is two, so the two numbers a presenter could quote are different and only one of
    /// them is what the rules say is missing.
    /// </remarks>
    [Fact]
    public async Task LaunchState_is_InsufficientEnergy_when_the_banks_do_not_cover_the_run_cost()
    {
        var presenter = await LoadedHub(
            ScriptedHomeScreen.ViewModel(
                energy: RunCost - 3, energyCost: RunCost, energyShortfall: 2));

        presenter.LaunchState.ShouldBe(HomeLaunchState.InsufficientEnergy);
        presenter.ActionColour.ShouldBe(HomeColourRole.Energy);
        presenter.CostBadge.ShouldBe(
            new HomeCostBadge(HomeCostBadgeKind.Shortfall, 2),
            "the badge is the shortfall the rules reported, not the difference between the two " +
            "numbers the screen happens to be holding — the Reserve is covering the rest of it.");
        presenter.CanStartRun.ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 A bar below the price is NOT by itself a refusal: the Reserve pays the remainder.
    /// </summary>
    /// <remarks>
    /// <c>EnergyMath.Spend</c> draws the main bar first and the Reserve for what is left, so a
    /// player with 17 in a bar of 120 and a stocked Reserve starts a 20-cost run. The pill still
    /// reads <c>17/120</c> — the Reserve is a separate bank, not part of the denominator — so a
    /// presenter deciding the state from the two numbers it can see refuses this tap, offers a
    /// refill for Energy the player already has, and no other case in this file would notice.
    /// </remarks>
    [Fact]
    public async Task LaunchState_is_Ready_when_the_bar_is_short_but_nothing_is_missing()
    {
        var presenter = await LoadedHub(
            ScriptedHomeScreen.ViewModel(
                energy: RunCost - 3, energyCost: RunCost, energyShortfall: 0));

        presenter.LaunchState.ShouldBe(
            HomeLaunchState.Ready,
            "a shortfall of nothing is a run the rules will accept, whatever the main bar reads.");
        presenter.CanStartRun.ShouldBeTrue();
        presenter.CostBadge.ShouldBe(
            new HomeCostBadge(HomeCostBadgeKind.Price, RunCost),
            "there is nothing to make up, so the badge quotes what will be taken.");
    }

    /// <summary>🔒 …and it must not be able to start a run, however hard the button is pressed.</summary>
    /// <remarks>
    /// Asserted on the seam's call count rather than on the presenter's own answer: a presenter that
    /// reported <c>CanStartRun == false</c> and submitted anyway looks identical from outside, and
    /// the run would be started with the Energy taken.
    /// </remarks>
    [Fact]
    public async Task Pressing_start_without_the_energy_for_it_starts_nothing()
    {
        var screen = ScriptedHomeScreen.Answering(
            ScriptedHomeScreen.ViewModel(
                energy: 0, energyCost: RunCost, energyShortfall: RunCost));
        var presenter = new HomePresenter(screen, ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);
        await presenter.PressStartAsync(CancellationToken.None);

        presenter.CanStartRun.ShouldBeFalse();
        screen.StartCallCount.ShouldBe(
            0,
            "the button in this state offers a refill, and a refill that quietly started a run " +
            "would spend Energy the player was being told they did not have.");
    }

    /// <summary>A hero below the recommendation is warned, never stopped.</summary>
    /// <remarks>
    /// The brief is explicit that the run stays startable: this state is a warning tint on the stage
    /// card, and a presenter that gated it would lock a player out of the only stage they have.
    /// </remarks>
    [Fact]
    public async Task LaunchState_is_Underpowered_when_the_hero_is_below_the_recommendation()
    {
        var presenter = await LoadedHub(
            ScriptedHomeScreen.ViewModel(
                energy: RunCost, energyCost: RunCost, power: 9_400d, recommendedPower: 11_900d));

        presenter.LaunchState.ShouldBe(HomeLaunchState.Underpowered);
        presenter.CanStartRun.ShouldBeTrue("underpowered is a warning, not a gate.");
        presenter.CostBadge.ShouldBe(
            new HomeCostBadge(HomeCostBadgeKind.Price, RunCost),
            "the run still costs what it costs; only the stage card changes.");
    }

    /// <summary>A read that does not answer becomes an inline retry row, and names what failed.</summary>
    [Fact]
    public async Task LaunchState_is_PresenterFailure_when_the_read_does_not_answer()
    {
        var presenter = new HomePresenter(
            ScriptedHomeScreen.Faulting(new InvalidOperationException(ReadFailureDetail)),
            ScreenContent.Catalogue());

        await presenter.LoadAsync(CancellationToken.None);

        presenter.LaunchState.ShouldBe(HomeLaunchState.PresenterFailure);
        presenter.FailureLine.ShouldNotBeNull().ShouldContain(
            ReadFailureDetail,
            Case.Sensitive,
            "the row names the thing that failed. A line that said only 'something went wrong' " +
            "leaves the player with nothing to report and nothing to retry.");
    }

    /// <summary>Each of the five states says its own word on the button.</summary>
    /// <remarks>
    /// Pairwise distinct rather than five literals: the words are the locale's and this case is
    /// about the mapping, which a presenter resolving one key for every state fails and a presenter
    /// resolving five right keys passes whatever the copy says.
    /// </remarks>
    [Fact]
    public async Task The_five_launch_states_each_put_their_own_word_on_the_button()
    {
        var labels = await LabelsOfEveryState();

        labels.Length.ShouldBe(5, "a floor: the rule below is stated over all five states.");
        labels.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            5,
            "two states sharing a word are two states the player cannot tell apart — and one of " +
            "them starts a run while the other refuses to.");
    }

    /// <summary>
    /// 🔒 The launch block keeps its three rows in every state, so nothing on the screen jumps when
    /// the read lands.
    /// </summary>
    /// <remarks>
    /// The brief's loading state is skeletons in the same places. A block hidden while loading
    /// changes the hero band's height, and the whole diorama moves the instant the read answers.
    /// </remarks>
    [Fact]
    public async Task The_launch_block_keeps_its_rows_in_every_state()
    {
        var presenters = await EveryState();

        presenters.Length.ShouldBe(5, "a floor: the rule below is stated over all five states.");
        presenters.ShouldAllBe(
            presenter => presenter.LaunchRowsVisible,
            "a row that disappears while loading resizes the band above it, and the hero jumps.");
    }

    /// <summary>The loading badge holds the button's width without quoting a price nobody has read.</summary>
    [Fact]
    public void CostBadge_while_loading_is_a_placeholder_rather_than_a_number() =>
        Hub(Ready()).CostBadge.ShouldBe(
            new HomeCostBadge(HomeCostBadgeKind.Placeholder, 0),
            "a badge showing a price before the read answers is a number the screen invented, and " +
            "a badge that is not there at all makes the button resize when the read lands.");

    /// <summary>🔒 The energy accent belongs to the refill offer, and to nothing else.</summary>
    /// <remarks>
    /// Stated as the complement rather than one more equality: the failure mode is a presenter that
    /// picks the accent from "not ready" rather than from "cannot pay", which paints the loading and
    /// failure states blue too.
    /// </remarks>
    [Fact]
    public async Task Only_the_refill_offer_is_drawn_in_the_energy_accent()
    {
        var presenters = await EveryState();

        var others = presenters
            .Where(presenter => presenter.LaunchState != HomeLaunchState.InsufficientEnergy)
            .ToArray();

        // 🔒 The floor is on the FILTERED set, not on the five it was filtered from (steering S32):
        // a presenter reporting InsufficientEnergy in every state filters this to nothing, and a
        // rule stated over nothing is satisfied by anything.
        others.Length.ShouldBe(
            4, "a floor: four of the five states are not the refill offer, and this rule is theirs.");
        others.ShouldAllBe(
            presenter => presenter.ActionColour != HomeColourRole.Energy,
            "the energy accent means 'this button buys Energy'. On any other state it is a " +
            "button that does something else wearing the colour of the one that does not.");
    }

    // ---------------------------------------------------------------- the hub's fixtures

    /// <summary>What a run costs in these cases. The screen is handed it; it never derives it.</summary>
    private const int RunCost = 20;

    private const string ReadFailureDetail = "the home view model could not be projected";

    private static HomeViewModel Ready() =>
        ScriptedHomeScreen.ViewModel(energy: RunCost * 2, energyCost: RunCost);

    private static HomePresenter Hub(HomeViewModel view) =>
        new(ScriptedHomeScreen.Answering(view), ScreenContent.Catalogue());

    private static async Task<HomePresenter> LoadedHub(HomeViewModel view)
    {
        var presenter = Hub(view);

        await presenter.LoadAsync(CancellationToken.None);

        return presenter;
    }

    /// <summary>One presenter in each of the five states, reached the way the screen reaches them.</summary>
    private static async Task<HomePresenter[]> EveryState()
    {
        var loading = Hub(Ready());

        var failed = new HomePresenter(
            ScriptedHomeScreen.Faulting(new InvalidOperationException(ReadFailureDetail)),
            ScreenContent.Catalogue());

        await failed.LoadAsync(CancellationToken.None);

        return
        [
            await LoadedHub(Ready()),
            await LoadedHub(
                ScriptedHomeScreen.ViewModel(
                    energy: 0, energyCost: RunCost, energyShortfall: RunCost)),
            await LoadedHub(
                ScriptedHomeScreen.ViewModel(
                    energy: RunCost, energyCost: RunCost, power: 9_400d, recommendedPower: 11_900d)),
            loading,
            failed,
        ];
    }

    private static async Task<string[]> LabelsOfEveryState()
    {
        var presenters = await EveryState();

        return [.. presenters.Select(presenter => presenter.ActionLabel)];
    }
}
