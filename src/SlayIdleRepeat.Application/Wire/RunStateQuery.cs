using System.Text.Json;
using SlayIdleRepeat.Application.Queries;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Contracts;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The write-model read behind <c>GET /run/{runId}/state</c>, assembled for the wire.</summary>
/// <remarks>
/// <para>
/// A read and nothing else: it loads the player's own stored rows, projects them through the one
/// projection the command path uses, hashes them with the one hash the command path uses, and hands
/// back the stored outcome envelopes the client missed. It writes no row and no ledger record, and
/// it decides no game rule — every number in the answer was decided by the command that wrote it.
/// </para>
/// <para>
/// Not a read model, and it wears none of that convention. The player's own state tolerates no
/// staleness — the client has already animated the command it is reading back — so this read states
/// the primary and would be wrong anywhere else.
/// </para>
/// </remarks>
public sealed class RunStateQuery
{
    /// <summary>A refusal, byte-identical for a player with no state and for a run that is not theirs.</summary>
    /// <remarks>
    /// Any difference between the two confirms that a run id exists, one request at a time.
    /// </remarks>
    private static readonly GatewayReply NotFound = new(404, string.Empty);

    private readonly ReadOwnStateUseCase _read;
    private readonly ICommandLedgerStore _ledger;

    /// <summary>Builds the read over the state it reads and the ledger it replays from.</summary>
    /// <param name="read">The own-state read.</param>
    /// <param name="ledger">The sequencing and outcome ledger.</param>
    /// <exception cref="ArgumentNullException">Either seam is null.</exception>
    public RunStateQuery(ReadOwnStateUseCase read, ICommandLedgerStore ledger)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(ledger);

        _read = read;
        _ledger = ledger;
    }

    /// <summary>Where this read is served from: the primary, always.</summary>
    /// <remarks>
    /// Served from a replica it would answer a reconnecting client with state older than the command
    /// it is reconnecting after.
    /// </remarks>
    public static ReadRouting Routing => ReadRouting.Primary;

    /// <summary>Reads one run of one player, with whatever outcomes were decided after a sequence.</summary>
    /// <param name="player">The authenticated player.</param>
    /// <param name="run">The run they asked about.</param>
    /// <param name="sinceSequence">The last sequence the client was answered at.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The state envelope on 200, or an empty-bodied 404.</returns>
    public async Task<GatewayReply> ReadAsync(PlayerId player, RunId run, long sinceSequence, CancellationToken ct)
    {
        var found = await _read.ReadAsync(new ReadOwnStateRequest(player, run), ct).ConfigureAwait(false);

        if (found.View is not { Run: { } runRow } state)
        {
            return NotFound;
        }

        var scope = CommandScopes.ForRun(player, run);
        var last = await _ledger.ReadLastSequenceAsync(scope, ct).ConfigureAwait(false);
        var replay = await ReplayAsync(scope, sinceSequence, last, ct).ConfigureAwait(false);

        return new GatewayReply(
            200,
            WireJson.Render(new RunStateResponse(
                WireProtocol.PROTOCOL_VERSION,
                run,
                last,
                sinceSequence,
                WireProjections.Of(state.Player),
                WireProjections.Of(runRow),
                WireProjections.HashPlayerAndRun(state.Player, runRow),
                replay.Envelopes,
                replay.ResyncFull)));
    }

    /// <summary>What the client missed, and whether an incremental resume is possible at all.</summary>
    /// <param name="Envelopes">The stored bodies to replay, ascending. Empty whenever a full resync is ordered.</param>
    /// <param name="ResyncFull"><c>true</c> to order a full resync, <c>null</c> to say nothing.</param>
    private sealed record Replay(IReadOnlyList<JsonElement> Envelopes, bool? ResyncFull);

    private static readonly Replay NothingMissed = new([], null);

    private static readonly Replay StartOver = new([], true);

    private async Task<Replay> ReplayAsync(string scope, long sinceSequence, long? last, CancellationToken ct)
    {
        // An unknown scope means the run can no longer be advanced, and a client ahead of the run
        // holds a model of the conversation that nothing incremental can repair.
        if (last is not { } lastSequence || sinceSequence > lastSequence)
        {
            return StartOver;
        }

        // The commonest reconnect there is, and it must never degrade: the client missed nothing, so
        // no backing is asked to enumerate anything.
        if (sinceSequence == lastSequence)
        {
            return NothingMissed;
        }

        var missed = await _ledger.ReadOutcomesAfterAsync(scope, sinceSequence, ct).ConfigureAwait(false);

        return missed.IsAvailable && Covers(missed.Records, sinceSequence + 1, lastSequence)
            ? new Replay(missed.Records.Select(Embed).ToArray(), null)
            : StartOver;
    }

    /// <summary>Whether the records are exactly the sequences <paramref name="from"/> through <paramref name="through"/>.</summary>
    /// <remarks>
    /// A record that fell out under its lifetime leaves a hole, and handing the survivors over alone
    /// would apply them on top of a state the missing one never reached.
    /// </remarks>
    private static bool Covers(IReadOnlyList<ReplayedOutcome> records, long from, long through)
    {
        if (records.Count != through - from + 1)
        {
            return false;
        }

        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].Sequence != from + index)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>One stored body as JSON to embed — parsed, cloned off its document, and never re-rendered.</summary>
    private static JsonElement Embed(ReplayedOutcome outcome)
    {
        using var stored = JsonDocument.Parse(outcome.ResponseBody);

        return stored.RootElement.Clone();
    }
}
