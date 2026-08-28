using System.Text.Json;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Contracts;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>The names the ordering log uses, so a case reads as a sequence rather than as two strings.</summary>
internal static class CommitSteps
{
    internal const string Commit = "commit";

    internal const string Dispatch = "dispatch";
}

/// <summary>
/// A unit of work that records every commit handed to it and then applies it to the volatile world
/// the gateway suite runs on.
/// </summary>
/// <remarks>
/// It writes through rather than only recording so that a case can send a second command against
/// what the first one committed; <see cref="Commits"/> is the assertion surface.
/// </remarks>
internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    private readonly WorldSliceStore _store;
    private readonly VolatileCommandLedger _ledger;
    private readonly ContentSnapshot _content;
    private readonly List<string>? _order;
    private readonly List<CommandCommit> _commits = [];

    internal RecordingUnitOfWork(
        WorldSliceStore store, VolatileCommandLedger ledger, ContentSnapshot content, List<string>? order = null)
    {
        _store = store;
        _ledger = ledger;
        _content = content;
        _order = order;
    }

    /// <summary>Every commit this unit of work was handed, in order.</summary>
    internal IReadOnlyList<CommandCommit> Commits => _commits;

    /// <inheritdoc/>
    public async Task CommitAsync(CommandCommit commit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commit);

        _commits.Add(commit);
        _order?.Add(CommitSteps.Commit);

        if (commit.State is { } profile)
        {
            await _store.SaveAsync(SliceOf(profile), ct);
        }

        await _ledger.AppendAsync(KeyOf(commit.Scope), LedgerRecordOf(commit.Outcome), ct);

        if (commit.OpensScope is { } opened)
        {
            await _ledger.OpenScopeAsync(KeyOf(opened), ct);
        }
    }

    private WorldSlice SliceOf(PlayerProfile profile)
    {
        var player = PlayerAggregate.Rehydrate(profile.Player, _content);
        if (player.IsFailure)
        {
            throw new InvalidOperationException("A committed player snapshot does not rehydrate: " + player.Error);
        }

        if (profile.ActiveRun is not { } runRow)
        {
            return new WorldSlice(player.Value, null);
        }

        var run = RunAggregate.Rehydrate(runRow);

        return run.IsSuccess
            ? new WorldSlice(player.Value, run.Value)
            : throw new InvalidOperationException("A committed run snapshot does not rehydrate: " + run.Error);
    }

    private static string KeyOf(IdempotencyScope scope) =>
        scope.Kind == IdempotencyScopeKind.Run
            ? CommandScopes.ForRun(scope.Player, scope.Run!.Value)
            : CommandScopes.ForPlayer(scope.Player);

    private static LedgerRecord LedgerRecordOf(RecordedCommandOutcome outcome)
    {
        using var payload = JsonDocument.Parse(outcome.PayloadJson);

        var decode = WireCommandCodec.Decode(new CommandEnvelope(
            WireProtocol.PROTOCOL_VERSION, outcome.CommandId, outcome.Sequence,
            outcome.CommandType, payload.RootElement.Clone()));

        return decode.Command is { } command
            ? new LedgerRecord(outcome.CommandId, outcome.Sequence, command, outcome.ResponseBody, outcome.OpensScope)
            : throw new InvalidOperationException(
                "A committed record does not decode (" + decode.Rejection + ").");
    }
}

/// <summary>A ledger that answers a record a case placed there by hand, and counts what the gateway does to it.</summary>
/// <remarks>
/// The seam the replay path is asserted on: the durable stores make scope opening a fallible call,
/// and this stands in for one without any of them being started.
/// </remarks>
internal sealed class ScriptedLedger : ICommandLedgerStore
{
    private readonly VolatileCommandLedger _inner;

    internal ScriptedLedger(VolatileCommandLedger inner) => _inner = inner;

    /// <summary>How many times the gateway asked for a scope to be opened.</summary>
    internal int ScopeOpens { get; private set; }

    /// <summary>How many records the gateway appended.</summary>
    internal int Appends { get; private set; }

    /// <summary>When set, opening a scope raises — a durable store answering for a run row that is gone.</summary>
    internal bool RefusingScopeOpens { get; set; }

    /// <inheritdoc/>
    public Task<long?> ReadLastSequenceAsync(string scope, CancellationToken ct) =>
        _inner.ReadLastSequenceAsync(scope, ct);

    /// <inheritdoc/>
    public Task<LedgerRecord?> ReadRecordAsync(string scope, CommandId commandId, CancellationToken ct) =>
        _inner.ReadRecordAsync(scope, commandId, ct);

    /// <summary>The write half the ledger seam no longer carries, kept here as the fixture's own.</summary>
    /// <remarks>
    /// The gateway holds this fixture as an <c>ICommandLedgerStore</c>, which is a read seam — it
    /// cannot reach either of the two members below at all. The counters are what a case seeds a
    /// record through, and what would report a caller that found its way back to them.
    /// </remarks>
    public Task OpenScopeAsync(string scope, CancellationToken ct)
    {
        ScopeOpens++;

        return RefusingScopeOpens
            ? throw new InvalidOperationException(
                "scripted: no run row stands behind '" + scope + "' any more, so its scope cannot be opened.")
            : _inner.OpenScopeAsync(scope, ct);
    }

    /// <inheritdoc cref="OpenScopeAsync"/>
    public Task AppendAsync(string scope, LedgerRecord record, CancellationToken ct)
    {
        Appends++;

        return _inner.AppendAsync(scope, record, ct);
    }
}

/// <summary>A sink that writes into the shared ordering log, and can be told to throw.</summary>
internal sealed class OrderedSink : IDomainEventSink
{
    private readonly List<string> _order;

    internal OrderedSink(List<string> order) => _order = order;

    /// <summary>When set, delivery raises — the analytics transport being down.</summary>
    internal bool Failing { get; set; }

    /// <inheritdoc/>
    public Task ReceiveAsync(DispatchedEvents batch, CancellationToken ct)
    {
        _order.Add(CommitSteps.Dispatch);

        return Failing
            ? throw new InvalidOperationException("scripted: the analytics transport is not answering.")
            : Task.CompletedTask;
    }
}

/// <summary>Envelope bodies for the commands the commit suite sends.</summary>
internal static class CommitEnvelopes
{
    /// <summary>A meta command the domain refuses on a starting player: an item nobody owns cannot be locked.</summary>
    internal static string LockUnownedItem(long sequence, string commandId) =>
        Envelopes.Body(
            "LOCK_ITEM", sequence, commandId,
            "{\"itemId\": \"g-none\", \"locked\": true}");
}
