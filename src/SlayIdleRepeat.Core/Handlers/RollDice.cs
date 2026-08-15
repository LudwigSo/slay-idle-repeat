using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;
using SlayIdleRepeat.Core.Rules.Dice;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 `04` §§1,3,4 / `03` §1.1 — the <c>ROLL_DICE</c> handler (M3-04's dice draw, M3-02's real
/// movement): draws a face from the Fair-Dice bag, resolves its movement over the run's actual
/// board — stepwise, honouring the junction pause, the stage-end clamp, the boss-exact rule and
/// `04` §1's chain — applies any <see cref="DieFaceKind.Surge"/> heal, and moves the run.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A junction pause stops the whole roll, chain and all.</b> `03` §1.1's junction pause applies
/// "whether the move started there or reached it mid-move" — so the instant one face's movement
/// would leave a junction, this handler stops resolving faces entirely, commits the run at the
/// junction and opens a <see cref="Model.PendingFork"/> for <c>Handlers.ChooseFork</c> to resume.
/// The <see cref="DiceRolled"/> events for every face already drawn (including the paused one) are
/// still emitted — the draws happened and their stream positions are committed regardless of
/// whether the movement they describe finished.
/// </para>
/// <para>
/// 🔒 <b>Reaching the boss, or a stage-end clamp, ends the chain</b> (`03` §1.1: "landing on a
/// stage's last node resolves that tile, fires Stage Gate, chain stops" / "Landing on the boss node
/// likewise ends the chain") — regardless of the rolled face's own <see cref="FaceOutcome.RollAgain"/>.
/// Stage Gate and tile resolution are not this handler's (M3-05 / M3-03); this handler's job ends at
/// stopping the chain and leaving the run positioned correctly for whichever of those runs next.
/// </para>
/// <para>
/// 🔒 <b>What this handler does NOT do, and why.</b> `04` §2's die-composition sources — talents,
/// mounts, drafted perks, Dice Forge upgrades, curses — are all still <c>GapRegister</c> entries on
/// <c>Run</c> (M3-06/M3-11/M4's talents+mounts). This handler therefore always rolls against
/// <see cref="DieComposer.StartingDie"/> — six <see cref="DieFaceKind.Pip"/> faces — rather than a
/// composed one; <see cref="DieComposer"/> exists and is tested, ready for the milestone with real
/// sources to hand it. It also does not double- or halve- any tile reward for a
/// <see cref="DieFaceKind.Fortune"/>/<see cref="DieFaceKind.Void"/> face (unreachable from the
/// starting die today, and the reward itself is <c>ResolveTileCommand</c>'s, M3-03) — a future
/// <c>RESOLVE_TILE</c> handler reads the rolled <see cref="DieFace"/> off the <see cref="DiceRolled"/>
/// event to know whether to apply either multiplier.
/// </para>
/// <para>
/// ⚠️ <b><see cref="DieFaceKind.Star"/> is refused rather than guessed at</b> — see
/// <see cref="FaceEffectResolver"/>'s remarks for why <c>RollDiceCommand</c> carries no payload a
/// player's chosen movement could arrive on. Unreachable from the all-Pip starting die today.
/// </para>
/// <para>
/// 🔒 <b>The Fair-Dice bag's weight vector is reconstructed by replay, not persisted</b> — see
/// <see cref="FairDiceBag"/>'s remarks. The reset point is 0 (the whole run) because no Stage Gate
/// concept exists on <c>Run</c> yet (M3-05); the day it does, this handler's one <c>resetAtDraw</c>
/// argument is the only line that changes.
/// </para>
/// </remarks>
internal static class RollDice
{
    /// <summary>🔒 `04` §1 / `03` §1.1 — applies <c>ROLL_DICE</c>.</summary>
    internal static HandlerResult Handle(RollDiceCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (run.PendingFork is not null)
        {
            // 03 §1.1: movement is paused at a junction. CHOOSE_FORK, not another roll, is the only
            // legal next move — the same "an illegal move is data" shape as ShopBuy's own gates.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (run.HasPendingTile)
        {
            // 🔒 M3-05 — a tile landed on and not yet resolved blocks a further roll; RESOLVE_TILE
            // (and whichever command finishes it) is the only legal next move.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var board = BoardResolution.Resolve(run, input.Context.Content, input.Rng);

        var events = new List<DomainEvent>();
        var currentHp = run.CurrentHp;
        var chainLinks = 0;

        // 03 §1.1: the virtual trailhead is one step before node 0 and is not itself a board node —
        // the first face's own movement spends its first step arriving there.
        var atTrailhead = run.Position == Run.TrailheadPosition;
        var current = atTrailhead ? default : new NodeId(run.Position);

        while (true)
        {
            var committedDraws = run.StreamPosition(RngStreams.Dice) + (ulong)events.Count;

            // 🔒 M3-05 — resetAtDraw is the run's Stage Gate anchor, not a hard 0: the bag's weight
            // vector resets at the START OF THE CURRENT STAGE, not at the start of the whole run.
            var weights = FairDiceBag.Replay(
                run.RunSeed, resetAtDraw: run.StageGateDiceAnchor, uptoDraw: committedDraws);
            var (faceValue, _) = FairDiceBag.Step(input.Rng.Stream(RngStreams.Dice), weights);

            var face = DieComposer.StartingDie[faceValue - 1];
            var outcome = FaceEffectResolver.Resolve(face, chainLinks);

            if (outcome.RequiresPlayerChoice)
            {
                // 🔒 Unreachable from an all-Pip die today (see this type's remarks) — a defensive
                // domain-tier refusal rather than a thrown exception, because RollDiceCommand
                // genuinely carries no payload a Star choice could have arrived on: this is a gap in
                // the command's vocabulary, not a defect in this handler's own logic.
                return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
            }

            events.Add(new DiceRolled(DomainEvent.UnstampedSequence, face));

            if (outcome.HealPct is { } healPct)
            {
                var healed = currentHp + (int)Math.Round(run.MaxHp * healPct, MidpointRounding.AwayFromZero);
                currentHp = Math.Min(healed, run.MaxHp);
            }

            var steps = outcome.Movement;
            MovementEngine.AdvanceResult result;

            if (atTrailhead)
            {
                if (steps <= 0)
                {
                    // 03 §1.1's Void face (move 0) is unreachable from the starting die (curse-only),
                    // and no other reachable face moves 0 — nothing to do from the trailhead either way.
                    result = new MovementEngine.AdvanceResult(default, false, false, 0);
                }
                else
                {
                    result = MovementEngine.Advance(board, board.FirstNodeId, steps - 1);
                }
            }
            else
            {
                result = MovementEngine.Advance(board, current, steps);
            }

            if (steps > 0)
            {
                atTrailhead = false;
                current = result.Node;
            }

            if (result.PausedAtJunction)
            {
                run.MoveTo(current.Value);
                run.BeginPendingFork(new PendingFork(current.Value, result.RemainingSteps));

                if (currentHp != run.CurrentHp)
                {
                    run.SetHitPoints(currentHp, run.MaxHp);
                }

                return HandlerResult.Accept(events);
            }

            if (result.ReachedBoss)
            {
                run.MoveTo(current.Value);

                if (currentHp != run.CurrentHp)
                {
                    run.SetHitPoints(currentHp, run.MaxHp);
                }

                // 🔒 M3-05 — the boss node is itself a resolvable tile (03 §2's TileKind.Boss);
                // reaching it ends the chain but is not a Stage Gate (03 §1.1 fires that only on
                // landing exactly on stage 1/2/3's last node, never on the boss, which belongs to no
                // stage).
                var bossNode = board.Node(current);
                run.ArriveAtTile((int)bossNode.Tile, bossNode.LinearIndex, bossNode.Stage);

                return HandlerResult.Accept(events);
            }

            var stageClamped = result.RemainingSteps > 0;

            if (stageClamped || !outcome.RollAgain)
            {
                if (!atTrailhead)
                {
                    run.MoveTo(current.Value);

                    // 🔒 M3-05, 03 §1.1 — "landing on a stage's last node resolves that tile, fires
                    // Stage Gate, chain stops": the gate fires BEFORE ArriveAtTile is recorded so the
                    // heal reads Run.MaxHp before anything else this command touches, and its own
                    // Run.ApplyStageGate call is what writes the healed HP — the trailing
                    // SetHitPoints below becomes a no-op for it (currentHp already matches).
                    if (stageClamped)
                    {
                        currentHp = StageGateResolver.Apply(input, currentHp);
                    }

                    var node = board.Node(current);
                    run.ArriveAtTile((int)node.Tile, node.LinearIndex, node.Stage);
                }

                if (currentHp != run.CurrentHp)
                {
                    run.SetHitPoints(currentHp, run.MaxHp);
                }

                return HandlerResult.Accept(events);
            }

            chainLinks++;
        }
    }
}
