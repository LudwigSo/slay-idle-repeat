using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>START_RUN</c> handler: commits a run seed and creates the <c>Run</c> every other run
/// command assumes already exists.
/// </summary>
/// <remarks>
/// <para>
/// In order: refuse a request that already has an active run; refuse a chapter/tier the seed
/// derivation could not hash; spend the player's lifetime run counter and derive the seed from it;
/// build the new <c>Run</c> via <c>Run.Rehydrate</c> — the only way to construct one — and attach it
/// through <see cref="HandlerInput.OpenRun"/>.
/// </para>
/// <para>
/// <c>Position</c>, HP and <c>Gold</c> are not equally authored. <c>Position</c> is the design's own
/// starting value (the virtual trailhead, one step before node 0); <c>Gold</c> is 0 because nothing
/// can have paid a fresh run any. <c>CurrentHp</c>/<c>MaxHp</c> have no authored starting value at
/// all — Max HP is meant to be scored off the hero's build, but no build (gear/perks/pets/talents)
/// exists yet to score — so <see cref="StartingHitPoints"/> uses <c>Run.SetHitPoints</c>'s own
/// structural floor (1/1) rather than a fabricated balance number, and should be revisited once a
/// real build exists to compute Max HP from.
/// </para>
/// <para>
/// <see cref="MintRunId"/> derives a deterministic id from the player id and run counter rather than
/// calling an ambient id generator, which nothing in <c>Core</c>/<c>Application</c> is allowed to
/// call. It is a stand-in for the wire-issued, collision-checked id the server will allocate later.
/// </para>
/// </remarks>
internal static class StartRun
{
    /// <summary>The virtual trailhead, one step before node 0 — the run's authored starting position.</summary>
    private const int TrailheadPosition = -1;

    /// <summary>
    /// Not a design value: <c>Run.SetHitPoints</c>'s own floor, used because no document authors a
    /// starting Max HP before a hero build exists to score. See <see cref="Handle"/>'s remarks.
    /// </summary>
    private const int StartingHitPoints = 1;

    /// <summary>Gold is scoped to the run; a run that has picked up nothing holds none.</summary>
    private const long StartingGold = 0L;

    private static readonly ReadOnlyDictionary<string, ulong> NoStreamPositions =
        new(new Dictionary<string, ulong>(0, StringComparer.Ordinal));

    private static readonly ReadOnlyDictionary<string, long> NoAdUses =
        new(new Dictionary<string, long>(0, StringComparer.Ordinal));

    /// <summary>A just-started run has resolved no minigames.</summary>
    private static readonly ReadOnlyDictionary<int, string> NoResolvedMinigames = new(new Dictionary<int, string>(0));

    /// <summary>
    /// <c>RunSnapshot.PendingTileKind</c>'s "no tile pending" sentinel — a run at the trailhead is
    /// standing on no node, so it is on no tile either.
    /// </summary>
    /// <remarks>
    /// Restated here rather than read off <c>Run</c>, whose own constant is private: this is part of
    /// the snapshot's own contract, and this handler is writing a snapshot.
    /// </remarks>
    private const int NoPendingTile = -1;

    /// <inheritdoc cref="NoPendingTile"/>
    private const int NoPendingTileLinearIndex = 0;

    /// <inheritdoc cref="NoPendingTile"/>
    private const int NoPendingTileStage = 0;

    /// <summary><c>RunSnapshot.PendingEventCardId</c>'s "no card drawn" value: empty, never null.</summary>
    private const string NoPendingEventCard = "";

    /// <summary>Applies <c>START_RUN</c>.</summary>
    /// <param name="command">The chapter and tier to start on.</param>
    /// <param name="input">The cloned, already-caught-up, run-less slice.</param>
    /// <returns>
    /// A rejection if the player already has an active run or the command names a chapter/tier the
    /// seed derivation could not hash; otherwise accepted, with the new <c>Run</c> attached and no
    /// events.
    /// </returns>
    internal static HandlerResult Handle(StartRunCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        // Checked first, before anything is spent: a player who already has a run legitimately
        // double-taps "start run" or retries a call whose response they never saw.
        if (input.State.Run is not null)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // Checked before Player.BeginRun() spends the lifetime counter, so a rejected START_RUN
        // costs the player nothing.
        if (command.ChapterId < 1 || !Enum.IsDefined(command.Tier))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var player = input.Player;

        var runCounter = player.BeginRun();

        var runSeed = SeedDerivation.RunSeed(
            player.Id, command.ChapterId, command.Tier, input.Context.NowUtc, runCounter);

        var snapshot = new RunSnapshot(
            SnapshotSchema.SchemaVersion,
            MintRunId(player.Id, runCounter),
            player.Id,
            runSeed,
            command.ChapterId,
            command.Tier,
            input.Context.NowUtc,
            TrailheadPosition,
            StartingHitPoints,
            StartingHitPoints,
            StartingGold,
            NoStreamPositions,
            NoAdUses,
            NoResolvedMinigames,
            PendingForkJunctionPosition: null,
            PendingForkRemainingSteps: null,
            NoPendingTile,
            NoPendingTileLinearIndex,
            NoPendingTileStage,
            NoPendingEventCard,

            // 07 §4's snapshot-at-run-start, taken here because here is the only moment it can be:
            // the run does not exist before this line and the loadout may not change after it.
            // Named rather than positional, since every parameter past this point is optional and a
            // positional argument would bind to whichever one a later append happened to displace.
            StartingLoadout: player.Loadout.ToSnapshot());

        var run = Run.Rehydrate(snapshot);

        if (run.IsFailure)
        {
            // A defect, not a rejection: every field above is this handler's own construction, never
            // the player's.
            throw new InvalidOperationException(
                "StartRun built a RunSnapshot that does not rehydrate: " + run.Error + " Every field " +
                "on that snapshot is this handler's own construction, never the player's, so this is " +
                "a defect in StartRun.Handle, not an illegal move.");
        }

        input.OpenRun(run.Value);

        return HandlerResult.Accept();
    }

    /// <summary>A deterministic <see cref="RunId"/> stand-in — see <see cref="Handle"/>'s remarks.</summary>
    private static RunId MintRunId(PlayerId playerId, long runCounter) =>
        new("RUN_" + playerId.Value + "_" + runCounter.ToString(CultureInfo.InvariantCulture));
}
