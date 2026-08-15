using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-05, `14` §2.3 — the <c>START_BATTLE</c> handler: opens the fight the run is standing on.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What <c>Core/Rules/Combat/</c> exposes, and why this handler does not call any of it.</b>
/// <c>CombatSimulator.SimulateEncounter</c> (`30` §11.2's public entry point) can run a real,
/// content-driven fight — but only given the hero's <see cref="SlayIdleRepeat.Core.Rules.Combat.ActorStats"/>, and
/// nothing in <c>Core</c> can build that yet: `18` §8's stat pipeline reads gear (M4-03), pets
/// (M4-07), mounts (M4-08) and talents (M4-06), none of which exist. `14` §2.4 also gives the
/// client the authoritative simulation for this exact reason — <em>"the server answers with
/// <c>battleSeed</c> and the build snapshot, and the client simulates locally"</em> — so this
/// handler's job is narrower than "resolve the fight": commit the seed the fight will be simulated
/// from, and record that a battle is open. <see cref="Rng.RunRngScope.BeginBattle"/> is what derives
/// <c>battleSeed</c> (`14` §8.1's <c>Hash64(runSeed, "combat", battleIndex)</c>) and advances the
/// run's <c>combat</c> stream by exactly one — <c>GameRules.Execute</c> folds it back, so this
/// handler writes no counter itself. Delivering the derived seed to the client over the wire is
/// M5-03's envelope, not this milestone's: it is recoverable from the accepted
/// <c>RunSnapshot.RngStreamPositions["combat"]</c> (the position <em>before</em> this command, which
/// the wire layer already has) without a domain event, and `30` §7's <c>Events</c> surface names no
/// "battle started" event for M3-05 to invent one of (steering <b>S6</b>).
/// </para>
/// <para>
/// 🔒 <b>Legality.</b> A battle can only open against a pending <c>Enemy</c>/<c>Elite</c>/<c>Boss</c>
/// tile — <c>Handlers.ResolveTile</c>'s own remarks name this branch as the exact seam
/// <c>START_BATTLE</c> reads. <c>GameRules.Execute</c>'s phase gate (M3-05) already refuses a second
/// <c>START_BATTLE</c> while one battle is open, so this handler does not re-check the phase itself.
/// </para>
/// </remarks>
internal static class StartBattle
{
    /// <summary>🔒 `14` §2.3 — applies <c>START_BATTLE</c>.</summary>
    internal static HandlerResult Handle(StartBattleCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // 🔒 The cast is where the tile vocabulary re-enters — see Handlers.ResolveTile's own
        // remarks on Run.PendingTileKindValue for why this layer, not Model, does the interpreting.
        var kind = (TileKind)run.PendingTileKindValue;

        if (kind is not (TileKind.Enemy or TileKind.Elite or TileKind.Boss))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // 🔒 14 §8.1 — derives this battle's seed and advances the combat stream by one battleIndex.
        // The seed itself is not read here; deriving it is what commits the draw, and delivering it
        // to the client is the wire layer's job (see this type's remarks).
        _ = input.Rng.BeginBattle();

        run.EnterBattle();

        return HandlerResult.Accept();
    }
}
