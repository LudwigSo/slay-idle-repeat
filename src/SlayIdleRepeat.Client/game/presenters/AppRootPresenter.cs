using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// Where the application root has got to.
/// </summary>
public enum AppRootPhase
{
    /// <summary>The graph is being built and the profile opened. The state a presenter starts in.</summary>
    Composing = 1,

    /// <summary>A profile is open and its id is known.</summary>
    Ready = 2,

    /// <summary>Composition did not complete, and the reason is carried rather than thrown.</summary>
    Failed = 3,
}

/// <summary>
/// Drives the application root through composition into a usable session.
/// </summary>
/// <remarks>
/// <para>
/// A plain C# class taking <see cref="IGameHost"/> as a constructor argument, so it runs
/// under a test runner with no engine anywhere near it. Nothing here may name an engine
/// type or an adapter — the scene above it renders and forwards input, and the
/// composition root below it decides which adapters exist.
/// </para>
/// <para>
/// 🔒 Scoped to the composition and lifecycle seam only. Splash, session handshake, atlas
/// loading and the cold-start budget are the boot screen's, and the boot screen is not
/// this task's.
/// </para>
/// </remarks>
public sealed class AppRootPresenter
{
    private readonly IGameHost _gameHost;

    /// <summary>Takes the host the composition root built.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="gameHost"/> is null.</exception>
    public AppRootPresenter(IGameHost gameHost)
    {
        ArgumentNullException.ThrowIfNull(gameHost);

        _gameHost = gameHost;
    }

    /// <summary>How far composition has got.</summary>
    public AppRootPhase Phase { get; private set; } = AppRootPhase.Composing;

    /// <summary>The open profile's id once <see cref="Phase"/> is <see cref="AppRootPhase.Ready"/>, otherwise null.</summary>
    public PlayerId? PlayerId { get; private set; }

    /// <summary>Why composition failed once <see cref="Phase"/> is <see cref="AppRootPhase.Failed"/>, otherwise null.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Opens the local profile and moves to <see cref="AppRootPhase.Ready"/>, or records the
    /// failure and moves to <see cref="AppRootPhase.Failed"/>.
    /// </summary>
    /// <remarks>
    /// A host that throws is a state, not an escape: the root is the last thing between a
    /// failure and a player looking at a frozen splash, so it always has something to show.
    /// </remarks>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            // Awaited inside the guard rather than merely called inside it: the host's open is an
            // async method, so its failure arrives as a faulted task and a try around the call
            // alone would never see it.
            var player = await _gameHost.OpenProfileAsync(ct).ConfigureAwait(false);

            PlayerId = player;
            Phase = AppRootPhase.Ready;
        }
        catch (Exception failure)
        {
            FailureReason = failure.Message;
            Phase = AppRootPhase.Failed;
        }
    }
}
