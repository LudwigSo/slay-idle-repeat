using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Contracts;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The command-specific half of an accepted response — 14 §2.3's <c>outcome</c> object.</summary>
/// <param name="RunId">The run this command acted on or created, or <c>null</c> when the resulting state carries none. A <c>START_RUN</c> reads its allocated id here.</param>
/// <param name="Run">The client-visible run projection after the command, or <c>null</c> when the resulting state carries none.</param>
/// <param name="RngStreamStates">14 §2.3's literal draw counters — the run's committed per-stream positions, echoed by name. The same values as <paramref name="Run"/>'s, from the same snapshot; the field exists because the spec names it, not as a second computation.</param>
/// <param name="BattleSeed">The open battle's server-issued seed as <c>0x</c> + 16 lowercase hex, or <c>null</c> when no battle is open after this command.</param>
/// <param name="Events">The command's domain events, in order — the animation script the client replays.</param>
public sealed record AcceptedCommandOutcome(
    RunId? RunId,
    RunWireProjection? Run,
    IReadOnlyDictionary<string, ulong>? RngStreamStates,
    string? BattleSeed,
    IReadOnlyList<DomainEvent> Events);

/// <summary>One command's answer — 14 §2.3's response envelope, accepted or rejection, always HTTP 200.</summary>
/// <remarks>
/// <para>
/// One record for both shapes because the wire has one: <c>{protocolVersion, sequence, …}</c> with
/// either the accepted half (<see cref="Outcome"/>, <see cref="Profile"/>) or the rejection half
/// (<see cref="Rejected"/>, <see cref="Reason"/>, <see cref="Detail"/>) populated — the two
/// factories are the only builders, so a half-and-half instance cannot exist. Null members are
/// omitted from the JSON.
/// </para>
/// <para>
/// <see cref="Detail"/> is <c>null</c> on every rejection this build produces, and that is a
/// recorded absence, not an oversight: the three reason-specific payloads the wire contract
/// authors (a currency id for the funds shortfall, an availability instant for the cooldown, a
/// required/available pair for the energy shortfall) have no channel out of the domain today —
/// <c>HandlerResult.Reject</c> carries the reason value alone — and every other reason ships no
/// detail by ruling. On the wire a null detail is OMITTED, not spelled <c>null</c> (the renderer
/// skips null members; the spec marks the field "reason-specific, optional"). The member is
/// declared so the shape is stable when the channel arrives; nothing may fill it with a value the
/// domain did not decide.
/// </para>
/// </remarks>
public sealed record CommandResponse
{
    private CommandResponse(
        int protocolVersion,
        long sequence,
        AcceptedCommandOutcome? outcome,
        PlayerWireProjection? profile,
        bool? rejected,
        RejectionReason? reason,
        string? stateHash)
    {
        ProtocolVersion = protocolVersion;
        Sequence = sequence;
        Outcome = outcome;
        Profile = profile;
        Rejected = rejected;
        Reason = reason;
        StateHash = stateHash;
    }

    /// <summary>The protocol version this server speaks — always its own, never an echo of the client's.</summary>
    public int ProtocolVersion { get; }

    /// <summary>The request's sequence, echoed.</summary>
    public long Sequence { get; }

    /// <summary>The command-specific outcome. Non-null exactly on an accepted response.</summary>
    public AcceptedCommandOutcome? Outcome { get; }

    /// <summary>The full client-visible player projection. Non-null exactly on an accepted response — the kickoff shipped this in place of the unspecified <c>profileDelta</c>.</summary>
    public PlayerWireProjection? Profile { get; }

    /// <summary><c>true</c> on a rejection, omitted otherwise.</summary>
    public bool? Rejected { get; }

    /// <summary>Why, as the enum value — its name on the wire. Non-null exactly on a rejection.</summary>
    public RejectionReason? Reason { get; }

    /// <summary>The reason-specific payload. Always <c>null</c> today — see the type remarks.</summary>
    public object? Detail => null;

    /// <summary>
    /// The state's hash — the changed state on an acceptance, the untouched state on a rejection.
    /// <c>null</c> only on a rejection decided before any state was read (a protocol-version,
    /// decode, routing, sequencing, throttle or kill-switch refusal): hashing state the exchange
    /// never touched would mean loading it for the hash's sake, and the mirror on the other end
    /// resyncs on those reasons regardless.
    /// </summary>
    public string? StateHash { get; }

    /// <summary>An accepted response.</summary>
    /// <param name="sequence">The request's sequence.</param>
    /// <param name="outcome">The command-specific outcome.</param>
    /// <param name="profile">The client-visible player projection.</param>
    /// <param name="stateHash">The hash of the resulting state.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    public static CommandResponse Accepted(
        long sequence, AcceptedCommandOutcome outcome, PlayerWireProjection profile, string stateHash)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(stateHash);

        return new CommandResponse(
            WireProtocol.PROTOCOL_VERSION, sequence, outcome, profile, rejected: null, reason: null, stateHash);
    }

    /// <summary>A rejection — a successful protocol exchange whose answer is no (14 §16.2).</summary>
    /// <param name="sequence">The request's sequence, echoed.</param>
    /// <param name="reason">Why.</param>
    /// <param name="stateHash">The untouched state's hash, or <c>null</c> when no state was read.</param>
    public static CommandResponse RejectedWith(long sequence, RejectionReason reason, string? stateHash) =>
        new(WireProtocol.PROTOCOL_VERSION, sequence, outcome: null, profile: null, rejected: true, reason, stateHash);
}
