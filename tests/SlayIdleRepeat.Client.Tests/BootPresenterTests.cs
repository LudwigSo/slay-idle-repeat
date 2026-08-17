using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Shared;
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
        presenter.Atlas!.Detail.ShouldNotBeNullOrWhiteSpace(
            "and it has to say why, or 'absent' is a state with no trace and nobody can tell a " +
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

        clock.Reads.ShouldBeGreaterThan(
            1,
            "a presenter that never reads the injected clock reports zero, and zero would agree with " +
            "a clock that never moved. Two readings are the minimum a measurement can be made from.");
        presenter.Elapsed.ShouldBe(
            clock.Moved,
            "the number that gets compared against a cold-start budget has to come from the clock " +
            "that was injected. Stated as the span this clock actually moved, so an implementation " +
            "reading the ambient clock fails here instead of producing a plausible small number " +
            "that no test can pin.");
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

        presenter.Elapsed.ShouldBe(
            clock.Moved,
            "a failed boot is the one whose duration matters most — 'it hung for eleven seconds and " +
            "then said the profile was unreadable' is a different report from 'it failed at once'. A " +
            "presenter that only stops the measurement on the success path loses exactly that.");
    }

    // ------------------------------------------------------------------------ null guards

    [Fact]
    public void Constructor_rejects_a_null_game_host()
    {
        Should.Throw<ArgumentNullException>(() => new BootPresenter(
                  gameHost: null!, Strings(BootContent.Complete()), LoadedAtlas(), Frozen()))
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
                  StubGameHost.Opening(OpenedProfile), strings: null!, LoadedAtlas(), Frozen()))
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
                  StubGameHost.Opening(OpenedProfile), Strings(BootContent.Complete()), atlas: null!, Frozen()))
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
                  StubGameHost.Opening(OpenedProfile), Strings(BootContent.Complete()), LoadedAtlas(), clock: null!))
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

    private static BootStringCatalogue Strings(ContentSnapshot content) =>
        new(content, BootContent.English);

    private static BootPresenter Boot(
        IGameHost host, ContentSnapshot content, RecordingBootAtlasCatalogue atlas, IClockPort clock)
    {
        var presenter = new BootPresenter(host, Strings(content), atlas, clock);
        atlas.Observed = presenter;
        return presenter;
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
