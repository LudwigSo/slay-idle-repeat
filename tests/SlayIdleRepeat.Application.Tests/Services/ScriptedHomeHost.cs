using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;

namespace SlayIdleRepeat.Application.Tests.Services;

/// <summary>
/// An <see cref="IGameHost"/> serving one row and refusing every command with one chosen reason.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Here rather than <see cref="Adapters.InMemory.InMemoryGameHost"/> because that fake cannot
/// hold the two halves of this state at once.</b> It mints its own starting profile — a full Energy
/// bar — and its <c>RefuseEveryCommand</c> then refuses a command against a row that shows plenty.
/// A case about "the run was refused for Energy and the screen says by how much" needs the refusal
/// AND an empty bar, and no shipped fake can be put in that state.
/// </para>
/// <para>
/// 🔴 <b>And no shipped COMMAND can either.</b> <c>Handlers.StartRun</c> reads
/// <c>EnergyTuning.RunCost</c> nowhere: it refuses for state, for an uncleared prerequisite and for
/// Legend Level, and never for Energy. So <see cref="StartRunResult.InsufficientEnergy"/> is a seam
/// whose only caller is deferred, and steering S25 says the deliverable is the fixture that reaches
/// it anyway rather than a note that it cannot be reached.
/// </para>
/// </remarks>
internal sealed class ScriptedHomeHost : IGameHost
{
    private readonly WorldSlice _state;
    private readonly RejectionReason? _refusal;

    private ScriptedHomeHost(WorldSlice state, RejectionReason? refusal)
    {
        _state = state;
        _refusal = refusal;
    }

    /// <summary>How many commands this host has been sent.</summary>
    internal int SubmitCallCount { get; private set; }

    /// <summary>The last command it was sent, or <c>null</c> when it has been sent none.</summary>
    internal GameCommand? LastCommand { get; private set; }

    /// <summary>A host serving <paramref name="row"/> and refusing every command with <paramref name="reason"/>.</summary>
    /// <param name="row">The row every read answers with.</param>
    /// <param name="reason">Why every command is refused.</param>
    /// <param name="content">The content set the row is rehydrated against.</param>
    internal static ScriptedHomeHost Refusing(
        PlayerSnapshot row, RejectionReason reason, ContentSnapshot content) =>
        new(SliceOf(row, content), reason);

    /// <inheritdoc/>
    public Task<PlayerId> OpenProfileAsync(CancellationToken ct) =>
        Task.FromResult(_state.Player.Id);

    /// <inheritdoc/>
    public Task<ApplyCommandOutcome> SubmitAsync(
        PlayerId player, RunId? run, GameCommand command, CancellationToken ct)
    {
        SubmitCallCount++;
        LastCommand = command;

        return Task.FromResult(
            _refusal is { } reason
                ? ApplyCommandOutcome.Reject(reason, _state)
                : ApplyCommandOutcome.Accept(_state, [], []));
    }

    /// <inheritdoc/>
    public Task<OwnStateResult> ReadOwnStateAsync(PlayerId player, RunId? run, CancellationToken ct) =>
        Task.FromResult(
            new OwnStateResult(
                OwnStateLookup.Found,
                new OwnStateView(_state.Player.ToSnapshot(), _state.Run?.ToSnapshot())));

    private static WorldSlice SliceOf(PlayerSnapshot row, ContentSnapshot content)
    {
        var player = PlayerAggregate.Rehydrate(row, content);

        return player.IsFailure
            ? throw new InvalidOperationException(
                "the scripted host's row does not rehydrate: " + player.Error)
            : new WorldSlice(player.Value, null);
    }
}
