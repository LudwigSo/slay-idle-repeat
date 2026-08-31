using Shouldly;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The five things a player may be shown about the connection, and the one thing they may never be.
/// </summary>
/// <remarks>
/// 🔴 The connection overlay is the only part of this build that draws over every screen, so both
/// ways of getting it wrong are global: furniture on a healthy connection is on screen almost always,
/// and a blocking error stops a run the specification says is never stopped. Both have a named case
/// here so a change that introduces either has to delete an assertion to do it.
/// </remarks>
public sealed class ConnectionPresenterTests
{
    private static readonly DateTimeOffset FixtureInstant = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The authored durations, written out rather than read back off the presenter.</summary>
    /// <remarks>
    /// 🔒 Literals on purpose. A case that advances the clock by the very constant it is checking
    /// agrees with any value that constant could hold, and these three are spec-locked presentation
    /// numbers rather than dials — the point of pinning them is that a retune is a design decision
    /// somebody has to come here and make, not a number a passing suite lets through.
    /// </remarks>
    private static readonly TimeSpan AuthoredPillThreshold = TimeSpan.FromSeconds(2);

    /// <summary>How long the green flash and its line of text are up for.</summary>
    private static readonly TimeSpan AuthoredResyncAnnouncement = TimeSpan.FromMilliseconds(400);

    /// <summary>How long the run-resumed card is up for.</summary>
    private static readonly TimeSpan AuthoredResumeCard = TimeSpan.FromMilliseconds(1200);

    /// <summary>What a control the server would have to answer is drawn at while it cannot be used.</summary>
    private const float AuthoredUnavailableOpacity = 0.40f;

    // ---- 🔒 Connected draws nothing --------------------------------------------------------------

    /// <summary>
    /// 🔒 Not a tick, not a badge, not a green dot. A working connection is the ordinary case.
    /// </summary>
    [Fact]
    public async Task Nothing_at_all_is_drawn_while_the_connection_is_working()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Reachable(clock);

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        world.Presenter.State.ShouldBe(ConnectionState.Connected);
        world.Presenter.PillVisible.ShouldBeFalse(
            "the pill belongs to Reconnecting alone. A pill on a healthy connection is a permanent " +
            "piece of furniture on a handset screen, and it is the state a player is in almost all " +
            "of the time.");
        world.Presenter.CloudSlashGlyphVisible.ShouldBeFalse(
            "the glyph marks the DIMMED controls, and nothing is dimmed while the server can be " +
            "reached. Drawing it here would mark controls that work perfectly well as unusable.");
        world.Presenter.ToastText.ShouldBeNull(
            "there is nothing to say. A connected client that announces its connectedness is a client " +
            "that has made its network into a feature of the interface.");
        world.Presenter.ResyncFlashActive.ShouldBeFalse();
        world.Presenter.ServerActionsAvailable.ShouldBeTrue(
            "Connected is the ONE state in which a server-backed action is available, so this is also " +
            "the negative control for the dimming below: if it were false here every control in the " +
            "build would be dim all of the time.");
    }

    // ---- the offline, read-only presentation ------------------------------------------------------

    [Fact]
    public async Task Server_backed_actions_are_unavailable_the_moment_an_attempt_fails()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Unreachable(clock);

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        world.Presenter.State.ShouldBe(
            ConnectionState.Waiting,
            "under the pill threshold, so nothing is drawn yet — but the connection is already gone.");
        world.Presenter.PillVisible.ShouldBeFalse(
            "the threshold has not elapsed. This is the case that proves Waiting is a state and not a " +
            "cosmetic name for Reconnecting: a blink of failure must leave the screen untouched.");
        world.Presenter.ServerActionsAvailable.ShouldBeFalse(
            "availability follows the CONNECTION, not the pill. A build that kept the controls live " +
            "until the pill appeared would accept two seconds of taps it cannot deliver, and each one " +
            "would look to the player like it worked.");
        world.Presenter.CloudSlashGlyphVisible.ShouldBeTrue(
            "the glyph goes with the dimming, so a player who cannot separate a 40 % control from a " +
            "full one still reads the state. Tying it to the pill instead would leave the colourblind " +
            "path with nothing at all during the threshold.");
    }

    [Fact]
    public async Task The_pill_carries_its_own_loc_key_once_the_threshold_has_passed()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Unreachable(clock);

        await world.Manager.PollAsync(CancellationToken.None);
        clock.Advance(AuthoredPillThreshold);
        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        world.Presenter.PillVisible.ShouldBeTrue();
        world.Presenter.PillText.ShouldBe(
            "Reconnecting…",
            "the pill's words come out of the locale catalogue, and the catalogue answers with the KEY " +
            "itself when a key is missing. So a red here is one of two things: the presenter wrote a " +
            "literal instead of resolving a key, or the key is not in the shipped locale documents and " +
            "a player would be reading 'loc.net.reconnecting.status' off the top of the screen.");
    }

    /// <summary>
    /// 🔒 A tap on an unavailable control answers with an inline toast, never a modal, and never a
    /// blocking error.
    /// </summary>
    [Fact]
    public async Task A_tap_on_an_unavailable_action_answers_with_an_inline_toast()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Unreachable(clock);

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();
        world.Presenter.ReportUnavailableActionTapped();

        world.Presenter.ToastText.ShouldBe(
            "Waiting for connection.",
            "the answer is one line of text through the locale catalogue. A red here means either a " +
            "literal was written into the presenter or the key is missing from the shipped locales, " +
            "and in the second case the player reads the key.");
        world.Presenter.BlockingErrorVisible.ShouldBeFalse(
            "a tap on something the connection cannot carry is answered and nothing more. It is the " +
            "single most likely place a full-screen error would be introduced, which is why the " +
            "prohibition is asserted here as well as in its own case.");
    }

    [Fact]
    public async Task A_tap_while_connected_says_nothing()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Reachable(clock);

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();
        world.Presenter.ReportUnavailableActionTapped();

        world.Presenter.ToastText.ShouldBeNull(
            "the action was available, so the tap was delivered and there is nothing to explain. " +
            "Without this the case above is satisfied by a presenter that raises the toast on every " +
            "tap, which would put 'Waiting for connection.' under a control that had just worked.");
    }

    // ---- 🔒 there is never a blocking connection error --------------------------------------------

    /// <summary>
    /// 🔒 There is no full-screen blocking connection error in this game, during a run or outside one.
    /// </summary>
    /// <remarks>
    /// The prohibition is the specification's only hard one about the connection. Stated as its own
    /// case so that a change introducing a blocking error has to delete an assertion whose name says
    /// what it is deleting.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    public async Task No_blocking_connection_error_is_ever_shown_however_long_the_connection_is_down(
        int secondsDown)
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Unreachable(clock);

        await world.Manager.PollAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(secondsDown));
        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();
        world.Presenter.ReportUnavailableActionTapped();

        world.Presenter.BlockingErrorVisible.ShouldBeFalse(
            $"the connection has been down for {secondsDown} s and a player has tapped something it " +
            "cannot carry, which is every ingredient a blocking error would ever have. A run is never " +
            "stopped by the network: a player who is out of signal keeps browsing their inventory, " +
            "their perks and their codex, and a modal over the top of that ends the session instead of " +
            "waiting out a lift.");
    }

    // ---- the resync announcement -------------------------------------------------------------------

    [Fact]
    public async Task A_resync_that_changed_the_state_flashes_and_says_so()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Recovering(clock, NetWorlds.State(NetWorlds.AnotherHash, sequence: 9));
        world.Mirror.Apply(NetWorlds.Accepted(sequence: 4, NetWorlds.SomeHash));

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        clock.Advance(ReconnectManager.DelayAfterFailure(1));
        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        world.Presenter.ResyncFlashActive.ShouldBeTrue(
            "the reconnect found the server somewhere the client had not been, so there is something " +
            "to announce. The flash is keyed off the mirror MOVING rather than off the reconnect " +
            "happening, which is the distinction the whole announcement hangs on.");
        world.Presenter.ToastText.ShouldBe(
            "Caught up.",
            "resolved through the locale catalogue like everything else a player reads. A red is a " +
            "literal in the presenter or a key missing from the shipped locales.");
    }

    [Fact]
    public async Task A_resync_that_changed_nothing_announces_nothing()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Recovering(clock, NetWorlds.State(NetWorlds.SomeHash, sequence: 9));
        world.Mirror.Apply(NetWorlds.Accepted(sequence: 4, NetWorlds.SomeHash));

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        clock.Advance(ReconnectManager.DelayAfterFailure(1));
        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        world.Presenter.State.ShouldBe(
            ConnectionState.Connected,
            "the reconnect succeeded, so this case really is about a resync that happened.");
        world.Presenter.ResyncFlashActive.ShouldBeFalse(
            "the server was exactly where the client left it. Flashing here would announce the network " +
            "rather than the game, on every reconnect, and a player learns to ignore an indicator that " +
            "fires when nothing has happened — including the time something did.");
        world.Presenter.ToastText.ShouldBeNull(
            "and nothing to say, for the same reason.");
    }

    /// <summary>🔒 The announcement is up for its authored window and not a frame longer.</summary>
    /// <remarks>
    /// Both sides are asserted, because either alone is satisfiable by the wrong number: an
    /// announcement that never expired passes the first, and one that expired instantly passes the
    /// second.
    /// </remarks>
    [Fact]
    public async Task The_resync_announcement_is_still_up_just_under_its_authored_window()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Recovering(clock, NetWorlds.State(NetWorlds.AnotherHash, sequence: 9));
        world.Mirror.Apply(NetWorlds.Accepted(sequence: 4, NetWorlds.SomeHash));

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        clock.Advance(ReconnectManager.DelayAfterFailure(1));
        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        clock.Advance(AuthoredResyncAnnouncement - TimeSpan.FromMilliseconds(1));
        world.Presenter.Poll();

        world.Presenter.ResyncFlashActive.ShouldBeTrue(
            "one millisecond under 400 ms the flash is still up. A shortened window is a green flash " +
            "a player on a 60 Hz handset may never see a single frame of, and nothing else in the " +
            "build would report it missing.");
    }

    [Fact]
    public async Task The_resync_announcement_expires()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Recovering(clock, NetWorlds.State(NetWorlds.AnotherHash, sequence: 9));
        world.Mirror.Apply(NetWorlds.Accepted(sequence: 4, NetWorlds.SomeHash));

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        clock.Advance(ReconnectManager.DelayAfterFailure(1));
        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        clock.Advance(AuthoredResyncAnnouncement);
        world.Presenter.Poll();

        world.Presenter.ResyncFlashActive.ShouldBeFalse(
            "the announcement is a moment, not a state: it is up for its authored window and then it " +
            "is gone. A flash that never cleared would sit green over the screen for the rest of the " +
            "session, and every case above would still pass.");
        world.Presenter.ToastText.ShouldBeNull(
            "the flash and the line of text are one announcement of one event, so they go together. A " +
            "sentence outliving the flash would be a caption on a moment that has visibly passed.");
    }

    // ---- the resume card ---------------------------------------------------------------------------

    [Fact]
    public async Task The_resume_card_names_the_chapter_and_stage_the_run_projection_carries()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Reachable(clock);
        world.Mirror.Apply(
            NetWorlds.State(NetWorlds.SomeHash, run: NetWorlds.RunOnAPendingTile(chapterId: 2, stage: 3)));

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        world.Presenter.ResumeCardVisible.ShouldBeTrue(
            "the mirror holds a run the moment the presenter first looks, which is what 'the app opened " +
            "into a run already in progress' looks like from here.");
        world.Presenter.ResumeCardText.ShouldBe(
            "Picking up where you left off — Chapter 2, Stage 3.",
            "the catalogue is a lookup and does no interpolation, so the two runtime parameters are " +
            "substituted by the presenter. A red showing the raw '{chapter}' and '{stage}' means the " +
            "substitution was left to a localisation runtime this project does not have and never " +
            "will; a red showing different numbers means the card is reading a different field of the " +
            "run projection from the one the sentence promises.");
    }

    [Fact]
    public async Task The_resume_card_expires()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Reachable(clock);
        world.Mirror.Apply(NetWorlds.State(NetWorlds.SomeHash));

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        clock.Advance(AuthoredResumeCard - TimeSpan.FromMilliseconds(1));
        world.Presenter.Poll();

        world.Presenter.ResumeCardVisible.ShouldBeTrue(
            "one millisecond under 1.2 s the card is still up. Without this half the case below is " +
            "satisfied by a card that never appeared at all.");

        clock.Advance(TimeSpan.FromMilliseconds(1));
        world.Presenter.Poll();

        world.Presenter.ResumeCardVisible.ShouldBeFalse(
            "the card is a greeting, not a header. Left up it would cover the board it just told the " +
            "player they were back on.");
    }

    /// <summary>
    /// 🔒 The dim state's opacity, pinned as a value because a scene reads it as one.
    /// </summary>
    /// <remarks>
    /// ⚠️ It is presentation the specification locks, not a dial: 40 % is chosen so a control still
    /// reads as a control rather than as an absence, and a build that pushed it toward zero would be
    /// removing the interface a player is meant to be able to see waiting for them.
    /// </remarks>
    [Fact]
    public void An_unavailable_control_is_drawn_at_the_authored_opacity()
    {
        ConnectionPresenter.UnavailableActionOpacity.ShouldBe(
            AuthoredUnavailableOpacity,
            "the overlay scene has not been written yet, so this constant is the only place the value " +
            "exists in the build. Retuning it is a design decision taken in the specification, not a " +
            "live-ops dial and not a number a scene may pick for itself — and nothing else would " +
            "notice it moving.");
    }

    /// <summary>
    /// ⚠️ The run projection carries a stage only while a tile is pending, so a run between tiles has
    /// no stage this sentence could name.
    /// </summary>
    [Fact]
    public async Task No_resume_card_is_shown_when_the_projection_carries_no_stage()
    {
        var clock = new ManualClock(FixtureInstant);
        var world = Reachable(clock);
        world.Mirror.Apply(NetWorlds.State(NetWorlds.SomeHash, run: NetWorlds.RunBetweenTiles()));

        await world.Manager.PollAsync(CancellationToken.None);
        world.Presenter.Poll();

        world.Presenter.ResumeCardVisible.ShouldBeFalse(
            "the run is between tiles, so PendingTileStage carries no stage — the field spells both " +
            "'nothing pending' and 'the boss node, which belongs to no stage' as zero. The authored " +
            "sentence names a chapter AND a stage and there is no second sentence that names only a " +
            "chapter, so the honest answer is to show nothing. A red here means a number was invented " +
            "to fill the placeholder, and a player would be told they are on Stage 0.");
        world.Presenter.ResumeCardText.ShouldBeEmpty(
            "and no half-substituted sentence is left behind for a scene to draw anyway.");
    }

    private sealed record World(
        ReconnectManager Manager, StateMirror Mirror, CommandQueue Queue, ConnectionPresenter Presenter);

    /// <summary>A world whose server answers, and whose presenter therefore starts Connected.</summary>
    private static World Reachable(ManualClock clock) =>
        Build(RecordingGameApi.Reachable().Answering(NetWorlds.State(NetWorlds.SomeHash)), clock, follow: false);

    /// <summary>A world whose server never answers.</summary>
    private static World Unreachable(ManualClock clock) =>
        Build(RecordingGameApi.Reachable().UnreachableFor(int.MaxValue), clock, follow: true);

    /// <summary>A world that fails once and then comes back with the given read.</summary>
    private static World Recovering(ManualClock clock, Application.Wire.WireRunState state) =>
        Build(RecordingGameApi.Reachable().UnreachableFor(1).Answering(state), clock, follow: true);

    private static World Build(RecordingGameApi api, ManualClock clock, bool follow)
    {
        var mirror = new StateMirror();
        var queue = new CommandQueue(CountingIdGenerator.Counting());
        var manager = new ReconnectManager(api, mirror, queue, clock);

        if (follow)
        {
            manager.Follow(NetWorlds.Run);
        }

        return new World(
            manager,
            mirror,
            queue,
            new ConnectionPresenter(manager, mirror, Strings(), clock));
    }

    /// <summary>
    /// The catalogue over the SHIPPED content set, so a case reads what a player would.
    /// </summary>
    /// <remarks>
    /// 🔒 Deliberately not a hermetic fixture. Every claim here is about a key surviving the trip from
    /// the presenter to the locale documents, and the catalogue answers with the key itself on a miss
    /// — so a fixture that authored the four keys locally would prove the presenter can read a table
    /// this file wrote, which is not the thing that can break.
    /// </remarks>
    private static LocaleStringCatalogue Strings() => new(BootContent.Shipped, BootContent.English);
}
