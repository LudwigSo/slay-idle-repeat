using System.Text.Json;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The answer to <c>GET /run/{runId}/state</c>: where the run stands, and what the client missed while it was away.</summary>
/// <param name="ProtocolVersion">The version this server speaks — its own, never an echo of the client's.</param>
/// <param name="RunId">The run that was read.</param>
/// <param name="Sequence">The run scope's last consumed sequence, or <c>null</c> — omitted on the wire — when the ledger no longer knows the scope. A zero here would read as "this run has answered nothing yet" and invite the client to send sequence 1.</param>
/// <param name="SinceSequence">The sequence the client asked from, echoed exactly as accepted.</param>
/// <param name="Profile">The client-visible player projection.</param>
/// <param name="Run">The client-visible run projection — the run seed does not cross this line.</param>
/// <param name="StateHash">The command path's own hash of these two rows, so the client can verify this answer against the one its last command returned.</param>
/// <param name="MissedOutcomes">The stored response envelopes from <see cref="SinceSequence"/> + 1 onward, ascending, embedded verbatim. Empty when nothing was missed.</param>
/// <param name="ResyncFull">
/// <c>true</c> when the client cannot resume incrementally and must read its state afresh; <c>null</c>
/// — omitted on the wire — otherwise. Never <c>false</c>: the marker's presence is the whole signal.
/// </param>
/// <remarks>
/// The embedded envelopes are the bytes the first processing stored, carried as parsed JSON and
/// written out untouched. A re-rendering would re-decide outcomes the client has already been told
/// about, and the two answers would then differ over the same exchange.
/// </remarks>
public sealed record RunStateResponse(
    int ProtocolVersion,
    RunId RunId,
    long? Sequence,
    long SinceSequence,
    PlayerWireProjection Profile,
    RunWireProjection Run,
    string StateHash,
    IReadOnlyList<JsonElement> MissedOutcomes,
    bool? ResyncFull);
