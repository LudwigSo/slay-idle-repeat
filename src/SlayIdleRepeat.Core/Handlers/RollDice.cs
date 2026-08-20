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
/// The <c>ROLL_DICE</c> handler: draws one face from the Fair-Dice bag, resolves its movement over
/// the run's actual board — stepwise, honouring the junction pause, the stage-end clamp, and the
/// boss-exact rule — applies any <see cref="DieFaceKind.Surge"/> heal, and moves the run.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One face per command, and the chain's link count is persisted between them.</b> `03` §1.1
/// makes a <see cref="DieFaceKind.Chain"/> hop <em>"move 2, resolve the landing tile in full —
/// battle, draft, shop, everything — then the next chained roll fires automatically"</em>. Resolving
/// a tile takes its own command, so the sequence genuinely spans several <c>ROLL_DICE</c> calls and
/// the link count lives on the run. This handler used to loop over chained faces inside one command,
/// which resolved only the LAST landing and silently deleted the content of every hop before it —
/// dead code while the starting die was all Pip, and a live content-eating bug the moment a Dice
/// Forge could install a Chain face. "Fires automatically" is the client's behaviour, not a loop
/// here.
/// </para>
/// <para>
/// A junction pause stops the roll: the run is committed at the junction and a
/// <see cref="Model.PendingFork"/> opened for <c>Handlers.ChooseFork</c> to resume. Reaching the
/// boss, or coming to rest on a stage's last node — clamped or exactly — ends the chain regardless
/// of the rolled face's own <see cref="FaceOutcome.RollAgain"/>.
/// </para>
/// <para>
/// 🔒 <b>The die is the RUN's</b> (<see cref="RunDie.Of"/>), not <c>DieComposer.StartingDie</c>: a
/// Dice Forge tile installs run-scoped face replacements, and rolling the starting die anyway is
/// what made that tile do nothing. Talents, mounts and perks still contribute no face — see
/// <see cref="RunDie"/> for why each is absent.
/// </para>
/// <para>
/// <see cref="DieFaceKind.Star"/> is refused rather than guessed at, since <c>RollDiceCommand</c>
/// carries no payload a player's chosen movement could arrive on. It stays unreachable:
/// <c>Handlers.DiceForgeChoose</c> withholds Star from the forge's menu for exactly this reason, so
/// no legal command can put one on the run's die.
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

        if (run.CurrentHp == 0)
        {
            // 🔒 A dead hero does not roll. REVIVE, END_RUN and ABANDON_RUN are the legal moves, and
            // all three are reachable from here — a loss leaves the tile pending, so this arm only
            // fires for a death whose tile has already been cleared.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var board = BoardResolution.Resolve(run, input.Context.Content, input.Rng);
        var events = new List<DomainEvent>(1);
        var currentHp = run.CurrentHp;

        var face = Draw(input, run);
        var outcome = FaceEffectResolver.Resolve(face, run.ChainLinksTaken);

        if (outcome.RequiresPlayerChoice)
        {
            // Unreachable: no command installs a Star face — see this type's remarks. Refused rather
            // than thrown because the command genuinely carries no payload the choice could arrive on.
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        events.Add(new DiceRolled(DomainEvent.UnstampedSequence, face));

        if (outcome.HealPct is { } healPct)
        {
            var healed = currentHp + (int)Math.Round(run.MaxHp * healPct, MidpointRounding.AwayFromZero);
            currentHp = Math.Min(healed, run.MaxHp);
        }

        // 19 Part E's CUR_SLIPPERY, honoured here rather than as a build effect: 18 §2.1 has no
        // "movement" stat, and the penalty's floor of 1 is what stops a cursed run standing still.
        var (penalised, _) = RunDie.PenalisedMovement(face, run);
        var steps = face.Kind == DieFaceKind.Pip ? penalised : outcome.Movement;

        return Move(input, board, run, events, currentHp, steps, outcome);
    }

    /// <summary>Draws one face off the run's own die, against the Fair-Dice bag's replayed weights.</summary>
    private static DieFace Draw(HandlerInput input, Model.Run run)
    {
        // resetAtDraw is the run's Stage Gate anchor: weights reset at the start of the current
        // stage, not the start of the run.
        var weights = FairDiceBag.Replay(
            run.RunSeed,
            resetAtDraw: run.StageGateDiceAnchor,
            uptoDraw: run.StreamPosition(RngStreams.Dice));

        var (faceValue, _) = FairDiceBag.Step(input.Rng.Stream(RngStreams.Dice), weights);

        return RunDie.Of(run)[faceValue - 1];
    }

    /// <summary>
    /// Walks the movement this roll produced and commits wherever it comes to rest: a junction, the
    /// boss, or an ordinary landing.
    /// </summary>
    private static HandlerResult Move(
        HandlerInput input,
        BoardGraph board,
        Model.Run run,
        List<DomainEvent> events,
        int currentHp,
        int steps,
        FaceOutcome outcome)
    {
        // The virtual trailhead is one step before node 0 and is not itself a board node.
        var atTrailhead = run.Position == Model.Run.TrailheadPosition;
        var current = atTrailhead ? default : new NodeId(run.Position);

        MovementEngine.AdvanceResult result;

        if (atTrailhead)
        {
            result = steps <= 0
                ? new MovementEngine.AdvanceResult(default, false, false, 0)
                : MovementEngine.Advance(board, board.FirstNodeId, steps - 1);
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

            // The chain is not over — the fork's resumed landing is still this hop's — so the link
            // count is carried, not reset. Handlers.ChooseFork finishes the landing; the next
            // ROLL_DICE reads the count this line wrote.
            run.SetChainLinksTaken(ChainLinksAfter(run, outcome));
            CommitHitPoints(run, currentHp);

            return HandlerResult.Accept(events);
        }

        if (result.ReachedBoss)
        {
            run.MoveTo(current.Value);
            CommitHitPoints(run, currentHp);

            // Landing on the boss node ends the chain (03 §1.1). The boss node is itself a
            // resolvable tile, but it is not a Stage Gate — the boss belongs to no stage.
            run.SetChainLinksTaken(0);
            TileArrival.Land(run, board.Node(current));

            return HandlerResult.Accept(events);
        }

        // Positional, not a step count: an exact landing on a stage's last node ends the chain and
        // gates just as a clamped overshoot onto the same node does. The trailhead is not a board
        // node, so it cannot be asked.
        var atStageEnd = !atTrailhead && board.IsStageEndNode(current);

        if (atTrailhead)
        {
            // A movement of zero steps from the trailhead: nothing to land on, nothing to gate.
            run.SetChainLinksTaken(ChainLinksAfter(run, outcome));
            CommitHitPoints(run, currentHp);

            return HandlerResult.Accept(events);
        }

        run.MoveTo(current.Value);

        // The gate fires before the landing is recorded, so its heal reads Run.MaxHp before anything
        // else this command touches; the CommitHitPoints below then becomes a no-op for it. It fires
        // whether or not an Escape Rope skips the tile: 03 §7.1 makes the gate POSITIONAL, not tile
        // content.
        if (atStageEnd)
        {
            currentHp = StageGateResolver.Apply(input, currentHp);
        }

        // The stage-gate clamp ends the chain (03 §1.1); anything else defers to the face.
        run.SetChainLinksTaken(atStageEnd ? 0 : ChainLinksAfter(run, outcome));

        TileArrival.Land(run, board.Node(current));
        CommitHitPoints(run, currentHp);

        return HandlerResult.Accept(events);
    }

    /// <summary>Where the roll sequence stands after this face: one more link, or over.</summary>
    private static int ChainLinksAfter(Model.Run run, FaceOutcome outcome) =>
        outcome.RollAgain ? run.ChainLinksTaken + 1 : 0;

    /// <summary>Writes the hit points this roll's heals produced, if any moved.</summary>
    private static void CommitHitPoints(Model.Run run, int currentHp)
    {
        if (currentHp != run.CurrentHp)
        {
            run.SetHitPoints(currentHp, run.MaxHp);
        }
    }
}
