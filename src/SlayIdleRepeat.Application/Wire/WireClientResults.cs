using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The device credential a client presents to open a session — 14 §16.5's stored pair.</summary>
/// <param name="DeviceId">The device the account was minted for.</param>
/// <param name="DeviceSecret">The secret that proves it. Never logged, never rendered.</param>
public sealed record WireCredentials(string DeviceId, string DeviceSecret);

/// <summary>What <c>POST /auth/device</c> answers: a fresh anonymous account and the credential that reopens it.</summary>
/// <param name="DeviceId">The device the account was minted for.</param>
/// <param name="DeviceSecret">The secret that proves it, issued once and never re-readable.</param>
/// <param name="Player">The account this installation now plays.</param>
/// <param name="DisplayName">The name the server's own filter decided — never the one the caller asked for.</param>
public sealed record WireDeviceRegistration(string DeviceId, string DeviceSecret, PlayerId Player, string DisplayName);

/// <summary>An open token family — what both issuing routes answer with.</summary>
/// <param name="Player">Whose session this is.</param>
/// <param name="AccessToken">The bearer token every command and state call carries.</param>
/// <param name="AccessExpiresInSeconds">How long the access token is accepted for.</param>
/// <param name="RenewAfterSeconds">
/// When to renew, always ahead of expiry: the whole point of 14 §16.5's silent renewal is that a
/// client refreshes before a player can be told anything went wrong.
/// </param>
/// <param name="RefreshToken">The single-use rotation token. Never logged, never rendered.</param>
/// <param name="RefreshExpiresInSeconds">How long the family may be rotated within.</param>
public sealed record WireSession(
    PlayerId Player,
    string AccessToken,
    long AccessExpiresInSeconds,
    long RenewAfterSeconds,
    string RefreshToken,
    long RefreshExpiresInSeconds);

/// <summary>One command's answer as a client reads it — the client-side half of <see cref="CommandResponse"/>.</summary>
/// <param name="Sequence">The request's sequence, echoed.</param>
/// <param name="Accepted">Whether the command changed the state.</param>
/// <param name="Rejection">
/// Why it was refused, or <c>null</c>. <c>null</c> on a refusal too, when the server named a reason
/// this build does not know: <c>RejectionReason</c>'s own contract is that a client seeing an
/// unknown value treats it as a generic rejection and resyncs, so an unreadable reason may not be a
/// parse failure.
/// </param>
/// <param name="RunId">The run the command acted on or created, or <c>null</c>.</param>
/// <param name="Profile">The player projection after the command, or <c>null</c> on a refusal.</param>
/// <param name="Run">The run projection after the command, or <c>null</c>.</param>
/// <param name="RngStreamStates">The run's committed per-stream draw counters, echoed by name.</param>
/// <param name="BattleSeed">The open battle's server-issued seed, or <c>null</c> when no battle is open.</param>
/// <param name="StateHash">The hash the mirror checks itself against, or <c>null</c> when the exchange read no state.</param>
/// <remarks>
/// 🔒 <b>Deliberately carries no domain events, and that is not an omission.</b>
/// <c>WireJson</c>'s event converter refuses to read one — the server is their only producer, and a
/// client-supplied event would be an unvalidated door into the four consumers of the list — so
/// events genuinely cannot be recovered from a response body. A member here would either be
/// permanently empty or would need a parse nothing authorises; neither is worth having.
/// </remarks>
public sealed record WireCommandResult(
    long Sequence,
    bool Accepted,
    RejectionReason? Rejection,
    RunId? RunId,
    PlayerWireProjection? Profile,
    RunWireProjection? Run,
    IReadOnlyDictionary<string, ulong>? RngStreamStates,
    string? BattleSeed,
    string? StateHash);

/// <summary>What <c>GET /run/{runId}/state</c> answers as a client reads it.</summary>
/// <param name="RunId">The run that was read.</param>
/// <param name="Sequence">The run scope's last consumed sequence, or <c>null</c> when the ledger no longer knows the scope.</param>
/// <param name="SinceSequence">The sequence the client asked from, echoed.</param>
/// <param name="Profile">The player projection.</param>
/// <param name="Run">The run projection — the run seed does not cross this line.</param>
/// <param name="StateHash">The hash the mirror checks itself against.</param>
/// <param name="MissedOutcomes">The outcomes from <paramref name="SinceSequence"/> + 1 onward, ascending. Empty when nothing was missed.</param>
/// <param name="ResyncFull">Whether the client must read its state afresh rather than resume incrementally.</param>
public sealed record WireRunState(
    RunId RunId,
    long? Sequence,
    long SinceSequence,
    PlayerWireProjection Profile,
    RunWireProjection Run,
    string StateHash,
    IReadOnlyList<WireCommandResult> MissedOutcomes,
    bool ResyncFull);
