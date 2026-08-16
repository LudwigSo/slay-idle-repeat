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
    public async Task StartAsync_reaches_Failed_rather_than_propagating_when_the_host_throws()
    {
        var presenter = new AppRootPresenter(
            StubGameHost.Failing(new InvalidOperationException("the profile store is unreadable")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Phase.ShouldBe(
            AppRootPhase.Failed,
            "the root is the last thing between a broken composition and a player staring at a frozen " +
            "window. A thrown exception here escapes into the engine's node callback, where nothing " +
            "catches it and nothing tells the player anything.");
    }

    [Fact]
    public async Task StartAsync_records_the_reason_when_the_host_throws()
    {
        var presenter = new AppRootPresenter(
            StubGameHost.Failing(new InvalidOperationException("the profile store is unreadable")));

        await presenter.StartAsync(CancellationToken.None);

        presenter.FailureReason.ShouldNotBeNullOrWhiteSpace(
            "Failed without a reason is indistinguishable from Failed for any other cause, which makes " +
            "the one state that exists to be reported unreportable.");
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
