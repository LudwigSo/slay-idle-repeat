using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// A hand-written <see cref="IGameHost"/> for the screens that read state and submit commands,
/// recording what it was asked rather than only what it answered.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StubGameHost"/> answers the one call the application root makes and refuses the other
/// two by name, which is what its own remarks ask a presenter needing them to do: bring its own
/// fake rather than widen that one.
/// </para>
/// <para>
/// 🔒 It records the <em>arguments</em> because two of the facts these screens have to get right are
/// invisible in the answer. A read addressed to a named run can answer <c>NoSuchRun</c> where a read
/// addressed to the player alone returns whatever run they are in — same return type, different
/// question — and a <c>START_RUN</c> submitted against a run id is submitted against a run that does
/// not exist yet. Both look identical from the outside unless the call itself is observed.
/// </para>
/// </remarks>
internal sealed class RecordingGameHost : IGameHost
{
    private readonly OwnStateResult? _read;
    private readonly Exception? _readFailure;

    private RecordingGameHost(OwnStateResult? read, Exception? readFailure)
    {
        _read = read;
        _readFailure = readFailure;
    }

    /// <summary>How many times the state read was called.</summary>
    internal int ReadCallCount { get; private set; }

    /// <summary>The player the last read was addressed to.</summary>
    internal PlayerId? ReadPlayer { get; private set; }

    /// <summary>The run the last read named, or null when it named none.</summary>
    internal RunId? ReadRun { get; private set; }

    /// <summary>The token the last read was handed.</summary>
    internal CancellationToken? ReadToken { get; private set; }

    /// <summary>How many commands were submitted.</summary>
    internal int SubmitCallCount { get; private set; }

    /// <summary>The player the last submission was addressed to.</summary>
    internal PlayerId? SubmitPlayer { get; private set; }

    /// <summary>The run the last submission named, or null when it named none.</summary>
    internal RunId? SubmitRun { get; private set; }

    /// <summary>The last command submitted, or null while none has been.</summary>
    internal GameCommand? SubmitCommand { get; private set; }

    /// <summary>A host whose state read answers with the given result.</summary>
    internal static RecordingGameHost Reading(OwnStateResult result) => new(result, readFailure: null);

    /// <summary>A host whose state read answers <c>Found</c> with the given rows.</summary>
    internal static RecordingGameHost Finding(PlayerSnapshot player, RunSnapshot? run = null) =>
        Reading(new OwnStateResult(OwnStateLookup.Found, new OwnStateView(player, run)));

    /// <summary>A host whose state read finds nothing stored for the player.</summary>
    internal static RecordingGameHost FindingNoSuchPlayer() =>
        Reading(new OwnStateResult(OwnStateLookup.NoSuchPlayer, View: null));

    /// <summary>A host whose state read returns a faulted task — how a real async host fails.</summary>
    internal static RecordingGameHost FaultingItsRead(Exception failure) => new(read: null, failure);

    /// <inheritdoc/>
    public Task<PlayerId> OpenProfileAsync(CancellationToken ct) =>
        throw new NotSupportedException(
            "These screens run after boot has already opened the profile and are handed its id. A " +
            "presenter reopening it has made the cold start pay for the same read twice.");

    /// <inheritdoc/>
    public Task<OwnStateResult> ReadOwnStateAsync(PlayerId player, RunId? run, CancellationToken ct)
    {
        ReadCallCount++;
        ReadPlayer = player;
        ReadRun = run;
        ReadToken = ct;

        return _readFailure is null
            ? Task.FromResult(_read!)
            : Task.FromException<OwnStateResult>(_readFailure);
    }

    /// <inheritdoc/>
    public Task<ApplyCommandOutcome> SubmitAsync(
        PlayerId player,
        RunId? run,
        GameCommand command,
        CancellationToken ct)
    {
        SubmitCallCount++;
        SubmitPlayer = player;
        SubmitRun = run;
        SubmitCommand = command;
        _ = ct;

        return Task.FromResult(
            ApplyCommandOutcome.Accept(PlayerState.EmptySlice(player), [], []));
    }
}
