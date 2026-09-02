using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The application root's lifecycle seam: composing, then either a profile or a stated failure.
/// </summary>
public sealed class AppRootPresenterTests
{
    private static readonly PlayerId OpenedProfile = new("PLAYER_7f3c1a");

    private const string HostFailureMessage = "the profile store is unreadable";

    private static InvalidOperationException HostFailure() => new(HostFailureMessage);

    [Fact]
    public void Phase_is_Composing_before_StartAsync_is_called()
    {
        var presenter = new AppRootPresenter(StubGameHost.Opening(OpenedProfile));

        presenter.Phase.ShouldBe(
            AppRootPhase.Composing,
            "a freshly constructed root has opened nothing yet. If it started anywhere else, the " +
            "scene above it would render a session that does not exist for as long as the open takes.");
    }

    [Fact]
    public async Task StartAsync_reaches_Ready_when_the_host_opens_a_profile()
    {
        var presenter = new AppRootPresenter(StubGameHost.Opening(OpenedProfile));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Phase.ShouldBe(
            AppRootPhase.Ready,
            "an opened profile is the whole success condition of the root. Staying in Composing " +
            "after a successful open is a game that never starts and never says why.");
    }

    [Fact]
    public async Task StartAsync_carries_the_PlayerId_the_host_returned()
    {
        var presenter = new AppRootPresenter(StubGameHost.Opening(OpenedProfile));

        await presenter.StartAsync(CancellationToken.None);

        presenter.PlayerId.ShouldBe(
            OpenedProfile,
            "the id the host minted is the identity everything downstream is keyed on. A root that " +
            "reaches Ready without carrying it has hidden which profile is open, and the next screen " +
            "has to go and ask again.");
    }

    [Fact]
    public async Task StartAsync_records_no_failure_reason_when_the_host_opens_a_profile()
    {
        var presenter = new AppRootPresenter(StubGameHost.Opening(OpenedProfile));

        await presenter.StartAsync(CancellationToken.None);

        presenter.FailureReason.ShouldBeNull(
            "a reason on a Ready root is a failure that did not happen. Anything reading the reason " +
            "to decide whether to show an error screen would show one over a perfectly good session.");
    }

    [Fact]
    public async Task StartAsync_hands_the_host_the_cancellation_token_it_was_given()
    {
        using var cancellation = new CancellationTokenSource();
        var host = StubGameHost.Opening(OpenedProfile);
        var presenter = new AppRootPresenter(host);

        await presenter.StartAsync(cancellation.Token);

        host.ReceivedToken.ShouldBe(
            cancellation.Token,
            "the token is how the engine says 'this node is leaving the tree, stop'. A root that " +
            "drops it and passes None instead leaves the profile open running against a scene that " +
            "is already gone, and nothing can call it off.");
    }

    [Fact]
    public async Task StartAsync_reaches_Failed_rather_than_propagating_when_the_host_throws()
    {
        var presenter = new AppRootPresenter(StubGameHost.ThrowingBeforeReturning(HostFailure()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Phase.ShouldBe(
            AppRootPhase.Failed,
            "the root is the last thing between a broken composition and a player staring at a frozen " +
            "window. A thrown exception here escapes into the engine's node callback, where nothing " +
            "catches it and nothing tells the player anything.");
    }

    [Fact]
    public async Task StartAsync_reaches_Failed_when_the_hosts_task_faults_instead_of_throwing()
    {
        var presenter = new AppRootPresenter(StubGameHost.FaultingItsTask(HostFailure()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Phase.ShouldBe(
            AppRootPhase.Failed,
            "this is the shape the real host actually fails in: OpenProfileAsync is an async method, " +
            "so it returns a faulted task and never throws before returning one. A root that guards " +
            "the call rather than the await handles only the shape production cannot produce.");
    }

    [Fact]
    public async Task StartAsync_records_the_reason_when_the_host_throws()
    {
        var presenter = new AppRootPresenter(StubGameHost.ThrowingBeforeReturning(HostFailure()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.FailureReason.ShouldNotBeNullOrWhiteSpace(
            "Failed without a reason is indistinguishable from Failed for any other cause, which makes " +
            "the one state that exists to be reported unreportable.");
        presenter.FailureReason.ShouldContain(
            HostFailureMessage,
            Case.Sensitive,
            "the reason has to identify the failure that actually happened, not merely be non-blank. " +
            "A fixed string like 'composition failed' satisfies every 'is it set?' check and tells " +
            "whoever is holding the handset exactly as much as a blank one would.");
    }

    [Fact]
    public async Task StartAsync_leaves_the_PlayerId_unset_when_the_host_throws()
    {
        var presenter = new AppRootPresenter(StubGameHost.ThrowingBeforeReturning(HostFailure()));

        await presenter.StartAsync(CancellationToken.None);

        presenter.PlayerId.ShouldBeNull(
            "PlayerId is a struct, so a root that assigns it before the open completes leaves behind " +
            "a default whose Value is null rather than an obviously missing id. Every screen keyed on " +
            "it would then run against a profile that was never opened.");
    }

    [Fact]
    public void Constructor_rejects_a_null_game_host()
    {
        Should.Throw<ArgumentNullException>(() => new AppRootPresenter(gameHost: null!))
              .ParamName.ShouldBe(
                  "gameHost",
                  "the host is the presenter's only collaborator, and a null one turns every later call " +
                  "into a NullReferenceException at whichever line happens to touch it first. Guarding " +
                  "at the constructor is what makes the composition root the thing that failed.");
    }
}
