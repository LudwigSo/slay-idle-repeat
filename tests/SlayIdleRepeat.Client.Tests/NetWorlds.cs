using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The wire answers the connection machinery reads, built out of rows the domain would accept.
/// </summary>
/// <remarks>
/// Projected through <c>WireProjections</c> rather than constructed field by field: that is the one
/// construction path the wire has, and a projection assembled by hand would let a case pin a shape
/// the server could never send — including, most usefully here, one that still carried the run seed.
/// </remarks>
internal static class NetWorlds
{
    /// <summary>The profile every case here belongs to.</summary>
    internal static readonly PlayerId Player = new("PLAYER_net_4c1a");

    /// <summary>The run every case here is about.</summary>
    internal static readonly RunId Run = new("RUN_net_9d2f");

    /// <summary>A hash no other fixture value equals, for a case that only needs "some state".</summary>
    internal const string SomeHash = "hash-a";

    /// <summary>A second hash, distinct from <see cref="SomeHash"/>, for "the state moved".</summary>
    internal const string AnotherHash = "hash-b";

    /// <summary>The player projection, at whatever the shared fixture row says.</summary>
    internal static PlayerWireProjection Profile() =>
        WireProjections.Of(PlayerState.Rehydratable(Player));

    /// <summary>
    /// A run projection standing on a pending tile in a named stage — the state the resume card can
    /// actually name.
    /// </summary>
    /// <param name="chapterId">The chapter the card reads.</param>
    /// <param name="stage">The stage the card reads. 1-3 are the stages a sentence can name.</param>
    internal static RunWireProjection RunOnAPendingTile(int chapterId = 2, int stage = 3) =>
        WireProjections.Of(
            PlayerState.Run(
                Run,
                Player,
                RunPhase.InProgress,
                chapterId: chapterId,
                pendingTileKind: 0,
                pendingTileLinearIndex: 4,
                pendingTileStage: stage));

    /// <summary>
    /// A run projection between tiles: nothing pending, so the stage field carries no stage.
    /// </summary>
    internal static RunWireProjection RunBetweenTiles() =>
        WireProjections.Of(PlayerState.Run(Run, Player, RunPhase.InProgress));

    /// <summary>A state read answering with the given hash.</summary>
    /// <param name="stateHash">What the mirror compares itself against.</param>
    /// <param name="sequence">The scope's last consumed sequence, or null when the ledger has forgotten it.</param>
    /// <param name="sinceSequence">What the client asked from, echoed.</param>
    /// <param name="run">The run projection, defaulted to one on a pending tile.</param>
    internal static WireRunState State(
        string stateHash,
        long? sequence = 1,
        long sinceSequence = 0,
        RunWireProjection? run = null) =>
        new(
            Run,
            sequence,
            sinceSequence,
            Profile(),
            run ?? RunOnAPendingTile(),
            stateHash,
            MissedOutcomes: [],
            ResyncFull: false);

    /// <summary>An accepted command outcome at the given sequence, carrying the given hash.</summary>
    internal static WireCommandResult Accepted(long sequence, string stateHash) =>
        new(
            sequence,
            Accepted: true,
            Rejection: null,
            RunId: Run,
            Profile(),
            RunOnAPendingTile(),
            RngStreamStates: null,
            BattleSeed: null,
            stateHash);

    /// <summary>A refusal the server answered on HTTP 200 — a value, not a failure.</summary>
    /// <remarks>
    /// It carries no hash, because an exchange that refused the command read no state: that is the
    /// case the mirror has to leave its projections alone for.
    /// </remarks>
    internal static WireCommandResult Rejected(long sequence, RejectionReason reason) =>
        new(
            sequence,
            Accepted: false,
            reason,
            RunId: null,
            Profile: null,
            Run: null,
            RngStreamStates: null,
            BattleSeed: null,
            StateHash: null);
}
