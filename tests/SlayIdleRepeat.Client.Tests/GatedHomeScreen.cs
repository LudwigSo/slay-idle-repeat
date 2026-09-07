using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// An <see cref="IHomeScreen"/> whose <c>StartRun</c> stays genuinely in flight until it is
/// released, and whose view model can be moved between reads.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The only fixture a double-submit latch can be proved against.</b>
/// <see cref="ScriptedHomeScreen"/> answers synchronously, so a first press runs to completion
/// before it returns and the second finds a settled screen — turned away by whatever guard happens
/// to be there, including one taken AFTER the await, which is no guard at all. A paused submission
/// is what makes the second press arrive while the first is really outstanding.
/// </para>
/// <para>
/// 🔒 A case that pauses MUST release before it awaits anything, or a presenter that failed to
/// latch hangs the suite instead of failing it: start both calls, release, then await. This is the
/// same arrangement <c>RecordingGameHost.PausingItsCommands</c> already states for the host seam.
/// </para>
/// <para>
/// The moving view model is the other half: a rise in power can only be told from a first reading
/// if the fixture can answer two different things, and a seam that answered alike twice would
/// satisfy a presenter that compared against the previous reading and one that never did.
/// </para>
/// </remarks>
internal sealed class GatedHomeScreen : IHomeScreen
{
    private readonly TaskCompletionSource _gate =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private HomeViewModel _view;

    private GatedHomeScreen(HomeViewModel view) => _view = view;

    /// <summary>How many times the presenter asked to start a run.</summary>
    internal int StartCallCount { get; private set; }

    /// <summary>The stage the last start named, or <c>null</c> when none was asked for.</summary>
    internal int? LastStageStarted { get; private set; }

    /// <summary>A seam answering one view model, with its start paused until released.</summary>
    /// <param name="view">What the screen draws.</param>
    internal static GatedHomeScreen Answering(HomeViewModel view) => new(view);

    /// <summary>Moves what the next read answers with.</summary>
    /// <param name="view">What the screen draws from now on.</param>
    internal void Answer(HomeViewModel view) => _view = view;

    /// <summary>Lets every paused submission answer.</summary>
    internal void Release() => _gate.TrySetResult();

    /// <inheritdoc/>
    public Task<HomeViewModel> GetViewModelAsync(CancellationToken ct) => Task.FromResult(_view);

    /// <inheritdoc/>
    public async Task<StartRunOutcome> StartRunAsync(int stageId, CancellationToken ct)
    {
        // Recorded synchronously, before the gate: what a screen submitted is a fact about the
        // call, and a case that pauses one still has to be able to assert on it while it is paused.
        StartCallCount++;
        LastStageStarted = stageId;

        await _gate.Task.ConfigureAwait(false);

        return StartRunOutcome.Started(new RunId("RUN_home_gated_fixture"));
    }
}
