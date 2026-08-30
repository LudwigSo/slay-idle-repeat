using Shouldly;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The boot screen's own seam: the stages it walks, what it carries out of a successful start, and
/// — the part that matters most — the four different reasons a start can end without one.
/// </summary>
/// <remarks>
/// 🔒 A boot can fail because the content set is not there, because the profile cannot be opened,
/// because the app is being shut down mid-start, or because something nobody named went wrong.
/// All four look identical to a player and identical in a crash report unless the presenter keeps
/// them apart by name. Every failure case below therefore asserts a specific
/// <c>(BootStage, BootFailureKind)</c> pair, and two of them compare one cause against another
/// rather than against a literal — a presenter that collapsed the four into one identity would
/// satisfy every "did it fail?" assertion and none of those.
/// </remarks>
public sealed class BootPresenterTests
{
    private static readonly PlayerId OpenedProfile = new("PLAYER_7f3c1a");

    private static readonly DateTimeOffset BootStartedAt = new(2026, 4, 9, 6, 15, 0, TimeSpan.Zero);

    private static readonly TimeSpan ClockStep = TimeSpan.FromMilliseconds(250);

    private const string HostFailureMessage = "the profile pointer names a cache row that is not there";

    private const string AtlasFailureMessage = "the atlas manifest is present and will not parse";

    private const string AtlasAbsenceReason = "no atlas manifest under artifacts/placeholders";

    private const int LoadedAtlasCount = 2;

    private const int LoadedPlacementCount = 47;

    // ---------------------------------------------------------------- before anything happens

    [Fact]
    public void Stage_is_Splash_before_StartAsync_is_called()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen());

        presenter.Stage.ShouldBe(
            BootStage.Splash,
            "the first frame is drawn before any work starts, so the stage a freshly built presenter " +
            "reports is what the scene renders against. Starting anywhere later shows a progress " +
            "line for work that has not begun.");
    }

    [Fact]
    public void StatusText_before_StartAsync_is_the_splash_line()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen());

        presenter.StatusText.ShouldBe(
            BootContent.EnglishSplashStatus,
            "the splash frame has to say something, and it has to be words rather than a key. A " +
            "presenter that leaves this blank until the first stage completes shows an empty screen " +
            "for exactly the interval the player is most likely to think the game has hung.");
    }

    // ------------------------------------------------------------------------ the happy path

    [Fact]
    public async Task StartAsync_reaches_Ready_when_the_content_the_profile_and_the_atlas_all_come_up()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(
            BootStage.Ready,
            "Ready is what the scene above waits for before handing over to the next screen. A boot " +
            "that finishes its work and stays on an earlier stage is a splash screen nothing ever " +
            "leaves.");
    }

    [Fact]
    public async Task StartAsync_carries_the_PlayerId_the_host_opened()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.PlayerId.ShouldBe(
            OpenedProfile,
            "everything after boot is keyed on this id. A presenter that reaches Ready without it " +
            "has made the next screen open the profile a second time, which costs a cache read on " +
            "the exact path a four-second cold-start budget is measured over.");
    }

    [Fact]
    public async Task StartAsync_records_no_failure_when_the_boot_completes()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Failure.ShouldBeNull(
            "a failure on a Ready boot is a failure that did not happen. Anything reading it to " +
            "decide whether to report a broken start would report every successful one.");
    }

    [Fact]
    public async Task StartAsync_hands_the_host_the_cancellation_token_it_was_given()
    {
        using var cancellation = new CancellationTokenSource();
        var host = StubGameHost.Opening(OpenedProfile);
        var presenter = Boot(host, BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(cancellation.Token);

        host.ReceivedToken.ShouldBe(
            cancellation.Token,
            "the token is how the app says 'the window is closing, stop'. A boot that swallows it " +
            "and passes None leaves a profile open running against a screen that is already gone, " +
            "and nothing can call it off.");
    }

    // ------------------------------------------------------------------ the stages, in order

    [Fact]
    public async Task StartAsync_reads_the_atlas_while_it_is_in_the_Atlas_stage()
    {
        var atlas = LoadedAtlas();
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), atlas, Frozen());

        await presenter.StartAsync(CancellationToken.None);

        atlas.StageWhenRead.ShouldBe(
            BootStage.Atlas,
            "the stage is what the progress line is drawn from, so it has to be true WHILE the work " +
            "is happening rather than set once at the end. A presenter that advances the stage after " +
            "each step shows the player the previous step's caption throughout the current one.");
    }

    [Fact]
    public async Task StatusText_is_the_stages_own_line_while_that_stage_is_running()
    {
        var atlas = LoadedAtlas();
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), atlas, Frozen());

        await presenter.StartAsync(CancellationToken.None);

        atlas.StatusTextWhenRead.ShouldBe(
            BootContent.EnglishAtlasStatus,
            "the stages in between the first frame and the last one are the ones the player spends " +
            "the boot looking at, and only a vantage point inside the boot can see them. A presenter " +
            "that showed the splash line until Ready would pass every assertion taken before and " +
            "after StartAsync while rendering one caption for the whole load — which is the mapping " +
            "the boot document's stageStatus block exists to make.");
    }

    [Fact]
    public async Task StartAsync_does_not_reach_the_atlas_before_the_profile_is_open()
    {
        var atlas = LoadedAtlas();
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), atlas, Frozen());

        await presenter.StartAsync(CancellationToken.None);

        atlas.PlayerWhenRead.ShouldBe(
            OpenedProfile,
            "the profile stage is the one a boot is most tempting to skip, because the atlas read " +
            "needs nothing from it and both would still end at Ready. Observing the open profile " +
            "from inside the atlas read is what proves the profile stage actually ran, and ran first.");
    }

    [Fact]
    public async Task StartAsync_does_not_read_the_atlas_when_the_profile_cannot_be_opened()
    {
        var atlas = LoadedAtlas();
        var presenter = Boot(StubGameHost.FaultingItsTask(HostFailure()), BootContent.Complete(), atlas, Frozen());

        await presenter.StartAsync(CancellationToken.None);

        atlas.WasRead.ShouldBeFalse(
            "a boot that has already failed must not keep working. Reading an atlas for a session " +
            "that does not exist spends part of the cold-start budget on a screen the player will " +
            "never see, and it is the shape that later turns a failure into two failures.");
    }

    [Fact]
    public async Task StartAsync_does_not_open_a_profile_when_the_content_set_is_unusable()
    {
        var host = StubGameHost.Opening(OpenedProfile);
        var presenter = Boot(host, BootContent.Nothing(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        host.ReceivedToken.ShouldBeNull(
            "content comes before the profile because every rule the profile is rehydrated against " +
            "is read out of the content snapshot. A boot that opens a profile over a content set " +
            "that is not there produces a session nothing can be resolved for, and fails later at a " +
            "point that no longer names content.");
    }

    // ------------------------------------------------- the four failures, told apart by name

    [Fact]
    public async Task StartAsync_fails_as_ContentUnavailable_in_the_Content_stage_when_the_content_set_is_empty()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Nothing(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Failure!.Kind.ShouldBe(
            BootFailureKind.ContentUnavailable,
            "a content set that holds nothing is the export having shipped no data — a build " +
            "problem, fixed by rebuilding, and nothing at all like a profile that will not open.");
        presenter.Failure!.Stage.ShouldBe(
            BootStage.Content,
            "the stage is the other half of the identity: it says WHERE the boot got to, which is " +
            "what turns a crash report into a place to look.");
    }

    [Fact]
    public async Task StartAsync_fails_as_ProfileUnavailable_in_the_Profile_stage_when_the_host_cannot_open_one()
    {
        var presenter = Boot(StubGameHost.FaultingItsTask(HostFailure()), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Failure!.Kind.ShouldBe(
            BootFailureKind.ProfileUnavailable,
            "a dangling profile pointer or a corrupt cache row is a LOCAL, per-device fault that a " +
            "reinstall clears. Reporting it under the same name as missing content sends everyone " +
            "who hits it to the wrong answer.");
        presenter.Failure!.Stage.ShouldBe(
            BootStage.Profile,
            "and it happened after content loaded, which is the fact that separates the two reports.");
    }

    [Fact]
    public async Task StartAsync_fails_as_ProfileUnavailable_when_the_host_throws_before_returning_a_task()
    {
        var presenter = Boot(
            StubGameHost.ThrowingBeforeReturning(HostFailure()), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Failure!.Kind.ShouldBe(
            BootFailureKind.ProfileUnavailable,
            "the same fault, thrown one instant earlier. A presenter that awaits the call inside a " +
            "try block catches both shapes; one that starts the call outside it and awaits inside " +
            "catches only the faulted task, and this arrangement is the one that tells them apart.");
        presenter.Failure!.Stage.ShouldBe(
            BootStage.Profile,
            "and it is still the profile stage that broke — a synchronous throw must not escape the " +
            "stage it happened in and get filed against whatever ran next.");
    }

    [Fact]
    public async Task StartAsync_fails_as_Cancelled_rather_than_Unexpected_when_the_boot_is_shut_down_mid_start()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var presenter = Boot(
            StubGameHost.FaultingItsTask(new OperationCanceledException(cancellation.Token)),
            BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(cancellation.Token);

        presenter.Failure!.Kind.ShouldBe(
            BootFailureKind.Cancelled,
            "closing the app during a slow start is a player action, not a defect. Filed as " +
            "Unexpected it becomes the loudest 'error' in the telemetry — every backgrounded launch " +
            "on every handset — and buries the failures that are real.");
    }

    [Fact]
    public async Task StartAsync_fails_as_Unexpected_in_the_Atlas_stage_when_the_atlas_read_throws()
    {
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile), BootContent.Complete(), ThrowingAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Failure!.Kind.ShouldBe(
            BootFailureKind.Unexpected,
            "an atlas that is ABSENT is not a failure at all; an atlas that is present and refuses " +
            "to parse is one nobody anticipated. Folding the second into the first would report a " +
            "corrupt manifest as the ordinary empty-checkout state and lose it entirely.");
        presenter.Failure!.Stage.ShouldBe(
            BootStage.Atlas,
            "carried rather than swallowed, and located: the stage is what says which read threw.");
    }

    [Fact]
    public async Task StartAsync_carries_a_detail_that_names_the_failure_that_actually_happened()
    {
        var presenter = Boot(StubGameHost.FaultingItsTask(HostFailure()), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Failure!.Detail.ShouldContain(
            HostFailureMessage,
            Case.Sensitive,
            "the detail has to identify the failure that happened, not merely be non-blank. A fixed " +
            "string like 'boot failed' satisfies every 'is it set?' check and tells whoever is " +
            "holding the handset exactly as much as an empty one would.");
    }

    [Fact]
    public async Task StartAsync_reports_the_Failed_stage_while_the_failure_remembers_where_it_happened()
    {
        var presenter = Boot(StubGameHost.FaultingItsTask(HostFailure()), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(
            BootStage.Failed,
            "what the screen renders is 'this did not start'. The stage the presenter reports is the " +
            "one the scene draws.");
        presenter.Failure!.Stage.ShouldNotBe(
            presenter.Stage,
            "and the stage the FAILURE carries is where it went wrong, which is a different fact. A " +
            "presenter that stamped Failed onto the failure too would throw away the only thing " +
            "that says which step broke.");
    }

    [Fact]
    public async Task StartAsync_leaves_the_PlayerId_unset_when_the_boot_fails()
    {
        var presenter = Boot(StubGameHost.FaultingItsTask(HostFailure()), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.PlayerId.ShouldBeNull(
            "PlayerId is a struct, so a presenter that assigns it before the open completes leaves a " +
            "default whose Value is null rather than an obviously missing id. Every screen keyed on " +
            "it would then run against a profile that was never opened.");
    }

    // ------------------------------------------------------- 🔒 the identities, compared

    /// <summary>
    /// 🔒 The case that fails if the four causes are ever collapsed into one identity. Compared
    /// against each other rather than against literals: an implementation that reported a single
    /// shared kind would satisfy every literal assertion elsewhere by simply being that literal.
    /// </summary>
    [Fact]
    public async Task A_content_failure_and_a_profile_failure_share_no_part_of_their_identity()
    {
        var content = await FailureFrom(BootContent.Nothing(), StubGameHost.Opening(OpenedProfile), LoadedAtlas());
        var profile = await FailureFrom(BootContent.Complete(), StubGameHost.FaultingItsTask(HostFailure()), LoadedAtlas());

        content.Kind.ShouldNotBe(
            profile.Kind,
            "'no content' and 'no profile' are fixed by different people doing different things — " +
            "one is a broken export, the other is one handset's cache. One kind for both is a " +
            "support queue nobody can triage.");
        content.Stage.ShouldNotBe(
            profile.Stage,
            "and they stopped at different points, which is what makes a log line locatable.");
        content.Detail.ShouldNotBe(
            profile.Detail,
            "a shared detail string undoes the whole arrangement even with the kinds kept apart — it " +
            "is the part a human actually reads.");
    }

    [Fact]
    public async Task The_four_ways_a_boot_can_end_badly_carry_four_different_kinds()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var content = await FailureFrom(BootContent.Nothing(), StubGameHost.Opening(OpenedProfile), LoadedAtlas());
        var profile = await FailureFrom(BootContent.Complete(), StubGameHost.FaultingItsTask(HostFailure()), LoadedAtlas());
        var cancelled = await FailureFrom(
            BootContent.Complete(),
            StubGameHost.FaultingItsTask(new OperationCanceledException(cancellation.Token)),
            LoadedAtlas(),
            cancellation.Token);
        var unexpected = await FailureFrom(BootContent.Complete(), StubGameHost.Opening(OpenedProfile), ThrowingAtlas());

        new[] { content.Kind, profile.Kind, cancelled.Kind, unexpected.Kind }
            .Distinct()
            .Count()
            .ShouldBe(
                4,
                "four causes, four names. Any two that collapse make a whole class of broken starts " +
                "unreportable, and the pair most likely to collapse is cancellation into Unexpected — " +
                "which is how a metric that should be near zero fills up with people closing the app.");
    }

    // ------------------------------------------------------ an absent atlas is not a failure

    [Fact]
    public async Task StartAsync_reaches_Ready_when_the_atlas_is_absent()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), AbsentAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(
            BootStage.Ready,
            "no checkout of this repository contains a generated atlas, so absent is the ordinary " +
            "state of every machine the game is built on. A boot that treated it as fatal would be " +
            "a game nobody can start.");
    }

    [Fact]
    public async Task StartAsync_records_no_failure_when_the_atlas_is_absent()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), AbsentAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Failure.ShouldBeNull(
            "reaching Ready and still carrying a failure is worse than failing outright: anything " +
            "reporting on Failure would report a broken start on a boot that worked.");
    }

    [Fact]
    public async Task StartAsync_carries_the_absent_atlas_result_and_the_reason_it_is_absent()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), AbsentAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Atlas!.IsAvailable.ShouldBeFalse(
            "non-fatal is not the same as invisible. Something has to be able to tell that the " +
            "artwork is placeholder rectangles rather than the real pages.");
        presenter.Atlas!.Detail.ShouldBe(
            AtlasAbsenceReason,
            "and it has to say why the catalogue said it was absent, not merely say something. " +
            "Stated as the reason this read actually produced, because any fixed line — 'no atlas' — " +
            "is non-blank, reads identically for every cause, and would leave nobody able to tell a " +
            "checkout that never generated one from a generator that silently produced nothing.");
    }

    [Fact]
    public async Task StartAsync_carries_the_atlas_counts_when_the_atlas_does_load()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Atlas!.IsAvailable.ShouldBeTrue("the loaded arm has to be distinguishable from the absent one");
        presenter.Atlas!.AtlasCount.ShouldBe(
            LoadedAtlasCount,
            "the counts are what the atlas stage produced. A presenter that reported availability " +
            "and dropped them has kept the boolean and thrown away the evidence behind it.");
        presenter.Atlas!.PlacementCount.ShouldBe(LoadedPlacementCount, "likewise the placements the pages hold");
    }

    // -------------------------------------------------------------------------- status text

    [Fact]
    public async Task StatusText_is_the_resolved_string_for_the_stage_rather_than_the_key()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.StatusText.ShouldBe(
            BootContent.EnglishReadyStatus,
            "the scene renders this straight into a label, so a presenter that hands over the raw " +
            "key puts 'loc.boot.ready.status' in front of the player. Resolving is the presenter's " +
            "job precisely because the scene has no way to do it.");
    }

    [Fact]
    public async Task StatusText_falls_back_to_the_key_itself_when_the_locale_does_not_carry_the_stages_string()
    {
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile),
            BootContent.Missing(BootContent.ReadyStatusKey), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.StatusText.ShouldBe(
            BootContent.ReadyStatusKey,
            "the failure mode a fallback exists to prevent is a silent one. An empty label reads as " +
            "a design choice; the key reads as a bug, names itself, and can be found with one grep.");
    }

    [Fact]
    public async Task StartAsync_still_reaches_Ready_when_one_boot_string_is_missing_from_the_locale()
    {
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile),
            BootContent.Missing(BootContent.ReadyStatusKey), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(
            BootStage.Ready,
            "one untranslated caption is not an unusable content set. Collapsing the two would make " +
            "the game refuse to start over a string, which is a far worse outcome than a caption " +
            "that renders as its own key — and it is why ContentUnavailable is driven by a content " +
            "set holding nothing rather than by a lookup that missed.");
    }

    [Fact]
    public async Task StatusText_is_the_failure_line_once_the_boot_has_failed()
    {
        var presenter = Boot(StubGameHost.FaultingItsTask(HostFailure()), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.StatusText.ShouldBe(
            BootContent.EnglishFailureStatus,
            "a boot that stops has to SAY it stopped. Leaving the last progress caption up is the " +
            "state that looks exactly like a game still loading, and the player waits for something " +
            "that is never coming.");
    }

    // ------------------------------------------------------------------------------ elapsed

    [Fact]
    public void Elapsed_is_zero_before_StartAsync_is_called()
    {
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Advancing());

        presenter.Elapsed.ShouldBe(
            TimeSpan.Zero,
            "nothing has happened yet, so no time has passed on the thing being measured. Anything " +
            "else here is a presenter that started its measurement when it was constructed rather " +
            "than when the boot began.");
    }

    [Fact]
    public async Task Elapsed_is_the_span_the_injected_clock_moved_across_the_boot()
    {
        var clock = Advancing();
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), clock);

        await presenter.StartAsync(CancellationToken.None);
        var movedDuringTheBoot = clock.Moved;

        clock.Reads.ShouldBeGreaterThan(
            1,
            "a presenter that never reads the injected clock reports zero, and zero would agree with " +
            "a clock that never moved. Two readings are the minimum a measurement can be made from.");
        presenter.Elapsed.ShouldBe(
            movedDuringTheBoot,
            "the number that gets compared against a cold-start budget has to come from the clock " +
            "that was injected. Stated as the span this clock actually moved, so an implementation " +
            "reading the ambient clock fails here instead of producing a plausible small number " +
            "that no test can pin. Captured BEFORE the property is read: reading the clock is what " +
            "moves it, so a presenter computing Elapsed live on every access would move both sides " +
            "together and agree with itself.");
    }

    [Fact]
    public async Task Elapsed_does_not_move_once_the_boot_has_finished()
    {
        var clock = Advancing();
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), clock);

        await presenter.StartAsync(CancellationToken.None);
        var whenTheBootFinished = presenter.Elapsed;
        var readAgainAfterwards = presenter.Elapsed;

        readAgainAfterwards.ShouldBe(
            whenTheBootFinished,
            "'the boot took this long' is a span that ended, not a stopwatch still running. A " +
            "presenter computing it live off the clock keeps growing after Ready, so the number a " +
            "cold-start report picks up depends on when the report was written rather than on how " +
            "long the boot took — and every assertion that reads it once agrees with it.");
    }

    [Fact]
    public async Task Elapsed_is_exactly_zero_when_the_injected_clock_does_not_move()
    {
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Elapsed.ShouldBe(
            TimeSpan.Zero,
            "a frozen clock means no time passed, whatever the wall clock did. This is the case that " +
            "catches DateTime.UtcNow or a Stopwatch behind the property: both would report the real " +
            "microseconds the boot took and both would pass any 'is it non-negative?' assertion.");
    }

    [Fact]
    public async Task Elapsed_does_not_go_backwards_between_the_atlas_stage_and_the_end_of_the_boot()
    {
        var atlas = LoadedAtlas();
        var clock = Advancing();
        var presenter = Boot(StubGameHost.Opening(OpenedProfile), BootContent.Complete(), atlas, clock);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Elapsed.ShouldBeGreaterThan(
            TimeSpan.Zero,
            "an anchor for the comparison below, which two zeros would satisfy on their own: this " +
            "clock advances on every reading and the boot takes several, so a span of nothing means " +
            "the measurement never happened rather than that it never went backwards.");
        atlas.ElapsedWhenRead!.Value.ShouldBeLessThanOrEqualTo(
            presenter.Elapsed,
            "sampled from inside the boot rather than only at the end, because a monotonic reading " +
            "is a claim about the whole run. A presenter that recomputed the span from a restarted " +
            "origin would report a smaller number later than earlier, and a latency graph built on " +
            "it would show boots getting faster the longer they take.");
    }

    [Fact]
    public async Task Elapsed_is_measured_the_same_way_when_the_boot_fails()
    {
        var clock = Advancing();
        var presenter = Boot(StubGameHost.FaultingItsTask(HostFailure()), BootContent.Complete(), LoadedAtlas(), clock);

        await presenter.StartAsync(CancellationToken.None);
        var movedDuringTheBoot = clock.Moved;

        presenter.Elapsed.ShouldBe(
            movedDuringTheBoot,
            "a failed boot is the one whose duration matters most — 'it hung for eleven seconds and " +
            "then said the profile was unreadable' is a different report from 'it failed at once'. A " +
            "presenter that only stops the measurement on the success path loses exactly that.");
    }

    // -------------------------------------------------- M5-15: the session the wire runs on

    /// <summary>
    /// 🔒 <b>A run is never blocked on the network.</b> An unreachable server is recorded and the
    /// ladder takes over; the boot walks on.
    /// </summary>
    [Fact]
    public async Task An_unreachable_server_does_not_stop_the_boot()
    {
        var api = RecordingGameApi.Reachable().UnreachableFor(int.MaxValue);
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen(),
            session: Opener(api));

        await presenter.StartAsync(CancellationToken.None);

        api.RegisterAttempts.ShouldBe(
            1,
            "the session stage never reached the server at all, so everything below is passing on a " +
            "stage that did not run. This is the one stage a composed server arm exists for.");
        presenter.Stage.ShouldBe(
            BootStage.Ready,
            "a server that cannot be reached is the ordinary state of a handset in a lift, and the " +
            "game is playable without one — the local host is what the presenters drive. A boot that " +
            "stopped here would make a network outage look like a broken installation.");
        presenter.Failure.ShouldBeNull(
            "and it is not a failure at all: anything reading Failure to decide whether to report a " +
            "broken start would report every launch made offline.");
        presenter.AccountPlayerId.ShouldBeNull(
            "no session opened, so there is no account. A presenter that filled this in anyway would " +
            "hand the next screen an identity the server has never heard of.");
    }

    /// <summary>
    /// 🔒 …and a refusal is fatal, because retrying it unchanged cannot help.
    /// </summary>
    /// <remarks>
    /// The distinction is the port's own and it is the one the whole backoff hangs off. Folding a
    /// refusal into the unreachable arm would leave the boot walking on into a build whose every
    /// later call is refused, with nothing anywhere naming why.
    /// </remarks>
    [Fact]
    public async Task A_refused_registration_fails_the_boot_as_SessionRefused()
    {
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen(),
            session: Opener(RecordingGameApi.Reachable().RefusingFor(int.MaxValue, statusCode: 403)));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Failure.ShouldNotBeNull(
            "the server understood the sign-in and said no, and the boot carried on regardless. " +
            "Nothing later in the run can recover from that, and nothing later names it either.")
                 .Kind.ShouldBe(
                     BootFailureKind.SessionRefused,
                     "one name per cause: a refused sign-in is fixed by whoever owns the account " +
                     "service, not by the player's network and not by a reinstall.");
        presenter.Failure!.Stage.ShouldBe(
            BootStage.Session,
            "and the stage is the other half of the identity — it says which read refused, which is " +
            "what turns a crash report into a place to look.");
    }

    /// <summary>
    /// 🔒 The control: the arm every shipped build composes opens no session, and skipping the stage
    /// is the reason its boot is byte-identical to what it was before a server existed.
    /// </summary>
    [Fact]
    public async Task No_session_opener_skips_the_stage_entirely()
    {
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(BootStage.Ready, "the local arm's boot is unchanged");
        presenter.AccountPlayerId.ShouldBeNull(
            "no opener was composed, so nothing may have opened an account. A presenter that " +
            "invented one would put an identity into a build that has no server to have issued it.");
        presenter.ContentSync.ShouldBeNull(
            "likewise the sync: null is 'this build ran none', which is a different fact from every " +
            "state the sync can end in, and the two must not be collapsed.");
    }

    // ------------------------------------------- M5-15: the content sync, which never stops a boot

    /// <summary>
    /// 🔒 <b>The installed content is playable, so a failed sync is recorded and walked past.</b>
    /// </summary>
    [Fact]
    public async Task A_content_sync_failure_leaves_the_boot_at_Ready()
    {
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen(),
            contentSync: Sync(() => throw new HttpRequestException("the socket said no")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(
            BootStage.Ready,
            "the game already has a content set — it shipped with one. A sync that could not reach " +
            "the server is a game that is one revision behind, not a game that cannot start, and " +
            "making it fatal would take every player offline the moment content distribution did.");
        presenter.Failure.ShouldBeNull(
            "and it is not a boot failure. A named sync state carries it instead, so the two kinds " +
            "of 'something went wrong' stay tellable apart.");
    }

    /// <summary>…and the named failure is carried rather than reduced to "it did not work".</summary>
    [Fact]
    public async Task A_content_sync_failure_records_the_named_failure()
    {
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen(),
            contentSync: Sync(() => throw new HttpRequestException("the socket said no")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.ContentSync.ShouldBe(
            SyncState.Failed,
            "the boot has to be able to say the sync did not finish. Walking on is not the same as " +
            "pretending it succeeded.");
        presenter.ContentSyncFailure.ShouldNotBeNull(
            "and it has to say which of the three causes it was — a server nobody could reach, a " +
            "bundle that would not re-stamp and a malformed pointer are fixed by different people.")
                 .Kind.ShouldBe(ContentSyncFailureKind.Unreachable);
    }

    /// <summary>
    /// 🔒 The control: a healthy sync also reaches Ready, so the cases above pin the failure arm
    /// rather than a boot that walks past this stage whatever happens in it.
    /// </summary>
    [Fact]
    public async Task A_content_set_that_already_matches_the_server_reaches_Ready_as_up_to_date()
    {
        var presenter = Boot(
            StubGameHost.Opening(OpenedProfile), BootContent.Complete(), LoadedAtlas(), Frozen(),
            contentSync: Sync(() => InstalledContent.Version.Value));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(BootStage.Ready, "nothing to download and nothing to report");
        presenter.ContentSync.ShouldBe(
            SyncState.UpToDate,
            "stated as the state the sync actually reached, because a presenter that recorded Failed " +
            "unconditionally would satisfy every case above and none of this one.");
    }

    // ------------------------------------------------------------------------ null guards

    [Fact]
    public void Constructor_rejects_a_null_game_host()
    {
        Should.Throw<ArgumentNullException>(() => new BootPresenter(
                  gameHost: null!, Strings(BootContent.Complete()), LoadedAtlas(), Frozen(),
                  contentSync: null, session: null))
              .ParamName.ShouldBe(
                  "gameHost",
                  "a null collaborator turns into a NullReferenceException at whichever line touches " +
                  "it first, which on this path is inside a stage whose name then blames the wrong " +
                  "step. Guarding at the constructor is what makes the composition root the thing " +
                  "that failed.");
    }

    [Fact]
    public void Constructor_rejects_a_null_string_catalogue()
    {
        Should.Throw<ArgumentNullException>(() => new BootPresenter(
                  StubGameHost.Opening(OpenedProfile), strings: null!, LoadedAtlas(), Frozen(),
                  contentSync: null, session: null))
              .ParamName.ShouldBe(
                  "strings",
                  "without the catalogue there is no status text at all, and the first thing that " +
                  "would break is the splash frame — before any stage has run and before there is " +
                  "anywhere to report it.");
    }

    [Fact]
    public void Constructor_rejects_a_null_atlas_catalogue()
    {
        Should.Throw<ArgumentNullException>(() => new BootPresenter(
                  StubGameHost.Opening(OpenedProfile), Strings(BootContent.Complete()), atlas: null!, Frozen(),
                  contentSync: null, session: null))
              .ParamName.ShouldBe(
                  "atlas",
                  "an ABSENT atlas is a stated result; a null catalogue is a graph that was never " +
                  "wired. Letting null stand in for absent would make a composition mistake look " +
                  "like the ordinary empty-checkout state and never get fixed.");
    }

    [Fact]
    public void Constructor_rejects_a_null_clock()
    {
        Should.Throw<ArgumentNullException>(() => new BootPresenter(
                  StubGameHost.Opening(OpenedProfile), Strings(BootContent.Complete()), LoadedAtlas(),
                  clock: null!, contentSync: null, session: null))
              .ParamName.ShouldBe(
                  "clock",
                  "the clock is the only sanctioned source of time here. A null one would push the " +
                  "presenter toward the ambient clock as a fallback, which is the one thing the port " +
                  "exists to stop.");
    }

    // ---------------------------------------------------------------------------- fixtures

    private static InvalidOperationException HostFailure() => new(HostFailureMessage);

    private static SteppingClock Frozen() => SteppingClock.Frozen(BootStartedAt);

    private static SteppingClock Advancing() => SteppingClock.Advancing(BootStartedAt, ClockStep);

    private static RecordingBootAtlasCatalogue LoadedAtlas() =>
        RecordingBootAtlasCatalogue.Returning(BootAtlasResult.Loaded(LoadedAtlasCount, LoadedPlacementCount));

    private static RecordingBootAtlasCatalogue AbsentAtlas() =>
        RecordingBootAtlasCatalogue.Returning(BootAtlasResult.Absent(AtlasAbsenceReason));

    private static RecordingBootAtlasCatalogue ThrowingAtlas() =>
        RecordingBootAtlasCatalogue.Throwing(new InvalidOperationException(AtlasFailureMessage));

    private static LocaleStringCatalogue Strings(ContentSnapshot content) =>
        new(content, BootContent.English);

    private static BootPresenter Boot(
        IGameHost host,
        ContentSnapshot content,
        RecordingBootAtlasCatalogue atlas,
        IClockPort clock,
        ContentSyncPresenter? contentSync = null,
        SessionOpener? session = null)
    {
        var presenter = new BootPresenter(host, Strings(content), atlas, clock, contentSync, session);
        atlas.Observed = presenter;
        return presenter;
    }

    /// <summary>A session opener over a scripted api, wired the way the composition root wires one.</summary>
    private static SessionOpener Opener(RecordingGameApi api)
    {
        var clock = Frozen();
        var mirror = new StateMirror();

        return new SessionOpener(
            api,
            new EphemeralDeviceCredentials(),
            new ReconnectManager(api, mirror, new CommandQueue(CountingIdGenerator.Counting()), clock));
    }

    /// <summary>A content sync over a scripted server, against a one-document installed snapshot.</summary>
    private static ContentSyncPresenter Sync(Func<string> version) =>
        new(new ScriptedContentClient(version, _ => throw new InvalidOperationException("no bundle scripted")),
            InstalledContent);

    private static readonly ContentSnapshot InstalledContent = OneDocumentSnapshot();

    private static ContentSnapshot OneDocumentSnapshot()
    {
        var documents = new[]
        {
            new ContentDocument(
                "tuning/a.json",
                ContentValue.Object([new KeyValuePair<string, ContentValue>("x", ContentValue.Number(1m))])),
        };

        return new ContentSnapshot(ContentHashing.Compute(documents), documents);
    }

    private static async Task<BootFailure> FailureFrom(
        ContentSnapshot content,
        IGameHost host,
        RecordingBootAtlasCatalogue atlas,
        CancellationToken ct = default)
    {
        var presenter = Boot(host, content, atlas, Frozen());

        await presenter.StartAsync(ct);

        presenter.Failure.ShouldNotBeNull(
            "this arrangement is supposed to end badly. A null failure here means the boot succeeded " +
            "against a collaborator that cannot succeed, so the comparison that follows would be " +
            "comparing nothing.");

        return presenter.Failure!;
    }
}
