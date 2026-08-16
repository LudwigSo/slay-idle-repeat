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
/// The <c>ROLL_DICE</c> handler: draws a face from the Fair-Dice bag, resolves its movement over
/// the run's actual board — stepwise, honouring the junction pause, the stage-end clamp, and the
/// boss-exact rule — applies any <see cref="DieFaceKind.Surge"/> heal, and moves the run.
/// </summary>
/// <remarks>
/// <para>
/// A junction pause stops the whole roll, chain and all: the instant one face's movement would leave
/// a junction, this handler stops resolving faces, commits the run at the junction and opens a
/// <see cref="Model.PendingFork"/> for <c>Handlers.ChooseFork</c> to resume. Events for every face
/// already drawn, including the paused one, are still emitted.
/// </para>
/// <para>
/// Reaching the boss, or coming to rest on a stage's last node — clamped or exactly — ends the chain
/// regardless of the rolled face's own <see cref="FaceOutcome.RollAgain"/>. Tile resolution is not
/// this handler's job; it stops the chain, fires the Stage Gate for that landing, and leaves the run
/// positioned correctly for whatever resolves next.
/// </para>
/// <para>
/// This handler always rolls against <see cref="DieComposer.StartingDie"/> — six
/// <see cref="DieFaceKind.Pip"/> faces — rather than a composed one: talents, mounts, drafted perks,
/// Dice Forge upgrades and curses that would compose a different die don't exist yet. It likewise
/// applies no <see cref="DieFaceKind.Fortune"/>/<see cref="DieFaceKind.Void"/> reward multiplier —
/// both are unreachable from the starting die, and the reward itself belongs to
/// <c>ResolveTileCommand</c>.
/// </para>
/// <para>
/// <see cref="DieFaceKind.Star"/> is refused rather than guessed at, since <c>RollDiceCommand</c>
/// carries no payload a player's chosen movement could arrive on. Unreachable from the all-Pip
/// starting die today.
/// </para>
/// <para>
/// The Fair-Dice bag's weight vector is reconstructed by replay, not persisted — see
/// <see cref="FairDiceBag"/>'s remarks — and resets at the run's Stage Gate anchor.
/// </para>
/// </remarks>
internal static class RollDice
{
    /// <summary>Applies <c>ROLL_DICE</c>.</summary>
    internal static HandlerResult Handle(RollDiceCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (run.PendingFork is not null)
        {
            // Movement is paused at a junction; CHOOSE_FORK is the only legal next move.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (run.HasPendingTile)
        {
            // A tile landed on and not yet resolved blocks a further roll.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var board = BoardResolution.Resolve(run, input.Context.Content, input.Rng);

        var events = new List<DomainEvent>();
        var currentHp = run.CurrentHp;
        var chainLinks = 0;

        // The virtual trailhead is one step before node 0 and is not itself a board node.
        var atTrailhead = run.Position == Run.TrailheadPosition;
        var current = atTrailhead ? default : new NodeId(run.Position);

        while (true)
        {
            var committedDraws = run.StreamPosition(RngStreams.Dice) + (ulong)events.Count;

            // resetAtDraw is the run's Stage Gate anchor: weights reset at the start of the current
            // stage, not the start of the run.
            var weights = FairDiceBag.Replay(
                run.RunSeed, resetAtDraw: run.StageGateDiceAnchor, uptoDraw: committedDraws);
            var (faceValue, _) = FairDiceBag.Step(input.Rng.Stream(RngStreams.Dice), weights);

            var face = DieComposer.StartingDie[faceValue - 1];
            var outcome = FaceEffectResolver.Resolve(face, chainLinks);

            if (outcome.RequiresPlayerChoice)
            {
                // Unreachable from an all-Pip die today; refused rather than thrown because the
                // command genuinely carries no payload a Star choice could arrive on.
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

                // The boss node is itself a resolvable tile; reaching it ends the chain but is not a
                // Stage Gate — the boss belongs to no stage.
                var bossNode = board.Node(current);
                run.ArriveAtTile((int)bossNode.Tile, bossNode.LinearIndex, bossNode.Stage);

                return HandlerResult.Accept(events);
            }

            // Positional, not a step count: an exact landing on a stage's last node ends the chain
            // and gates just as a clamped overshoot onto the same node does. The trailhead is not a
            // board node, so it cannot be asked.
            var atStageEnd = !atTrailhead && board.IsStageEndNode(current);

            if (atStageEnd || !outcome.RollAgain)
            {
                if (!atTrailhead)
                {
                    run.MoveTo(current.Value);

                    // The gate fires before ArriveAtTile is recorded, so its heal reads Run.MaxHp
                    // before anything else this command touches; the trailing SetHitPoints below then
                    // becomes a no-op for it.
                    if (atStageEnd)
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
