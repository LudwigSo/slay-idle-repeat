using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>RESOLVE_TILE</c> handler: one branch per tile kind, over the pending tile
/// <c>Run.ArriveAtTile</c> recorded.
/// </summary>
/// <remarks>
/// <para>
/// Three shapes of branch:
/// </para>
/// <list type="number">
/// <item>
/// <b>Resolved here and cleared</b> — <c>Empty</c>, <c>Treasure</c>, <c>Cache</c>, <c>Shrine</c>,
/// <c>Curse</c>, <c>Shop</c>, <c>DiceForge</c>. The tile's whole effect happens in this command —
/// which for a Shop and a Dice Forge is deliberately nothing at all; see their branch.
/// </item>
/// <item>
/// <b>Advanced but not cleared</b> — <c>Event</c> and <c>Campfire</c>. A second command
/// (<c>EVENT_CHOOSE</c>, <c>CAMPFIRE_CHOOSE</c>) finishes them and reads the pending state to know
/// what it is finishing.
/// </item>
/// <item>
/// <b>Acknowledged and not cleared</b> — <c>Enemy</c>, <c>Elite</c>, <c>Boss</c>, <c>Minigame</c>.
/// Each resolves through its own command, so this handler accepts the acknowledgement and leaves the
/// tile pending for the command that owns it.
/// </item>
/// </list>
/// <para>
/// There is no run-phase gate and no board validation here: nothing confirms the pending tile is the
/// tile the run's generated board actually holds at that index.
/// </para>
/// </remarks>
internal static class ResolveTile
{
    /// <summary>Applies <c>RESOLVE_TILE</c>.</summary>
    /// <param name="command">Carries no payload: the tile is whatever the run is standing on.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no tile is pending, or when a pending event
    /// tile has already drawn its card; otherwise accepted, with whatever events the tile's resolver
    /// produced.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A real defect rather than a rejection: the tile vocabulary grew a member no branch here
    /// handles.
    /// </exception>
    internal static HandlerResult Handle(ResolveTileCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // Run stores the tile kind as an int because Model may not name Rules types; this handler,
        // which sits above both, is where it gets interpreted back into TileKind.
        switch ((TileKind)run.PendingTileKindValue)
        {
            case TileKind.Empty:
                run.ClearPendingTile();
                return HandlerResult.Accept();

            case TileKind.Treasure:
            {
                var events = TreasureResolver.Resolve(input);
                run.ClearPendingTile();
                return HandlerResult.Accept(events);
            }

            case TileKind.Cache:
            {
                var events = CacheResolver.Resolve(input);
                run.ClearPendingTile();
                return HandlerResult.Accept(events);
            }

            case TileKind.Shrine:
                // hasCleansableCurse is always false today: Run holds no curse list yet, so there is
                // nothing to ask. ShrineResolver's cleanse rule is written and tested; it just has no
                // true input yet.
                ShrineResolver.Resolve(input, hasCleansableCurse: false);
                run.ClearPendingTile();
                return HandlerResult.Accept();

            case TileKind.Curse:
            {
                // Pays the curse tile's reward and does NOT apply the curse itself — see
                // CurseTileResolver's remarks.
                var events = CurseTileResolver.Resolve(input);
                run.ClearPendingTile();
                return HandlerResult.Accept(events);
            }

            case TileKind.Event:
                return DrawEventCard(input, run);

            case TileKind.Campfire:
                // Nothing to draw or decide here; CAMPFIRE_CHOOSE reads the pending state directly.
                return HandlerResult.Accept();

            case TileKind.Enemy:
            case TileKind.Elite:
            case TileKind.Boss:
                // Acknowledgement only. START_BATTLE is the real trigger and reads this pending state.
                return HandlerResult.Accept();

            case TileKind.Shop:
            case TileKind.DiceForge:
                // Deliberately nothing: neither a stocked offer nor a die-face choice exists on Run,
                // and no command in the frozen vocabulary can carry either. Clearing is what lets the
                // run walk away — whichever commit lands either concept owes it its own clearing
                // step and revisits this branch.
                run.ClearPendingTile();
                return HandlerResult.Accept();

            case TileKind.Minigame:
                // Resolves through MINIGAME_SUBMIT, which clears the tile as its last step.
                return HandlerResult.Accept();

            case TileKind.Portal:
                return ResolvePortal(input, run);

            default:
                // InvalidOperationException, not ArgumentOutOfRangeException: the bad value is a
                // state value read off run.PendingTileKindValue, not an argument to this method.
                throw new InvalidOperationException(
                    "03 §2 fixes fourteen tile kinds and RESOLVE_TILE has a branch for each. A kind " +
                    "of " + run.PendingTileKindValue.ToString(CultureInfo.InvariantCulture) + " arriving " +
                    "here that none of them names is one of two things, both defects: the vocabulary " +
                    "grew a fifteenth member and this handler was not extended, or a persisted row " +
                    "carried a value outside it — which Run.Rehydrate deliberately cannot catch, " +
                    "because 30 §11.4 forbids Model from naming this vocabulary. Either way it throws, " +
                    "so the gap is loud rather than silently accepted as a tile that does nothing.");
        }
    }

    /// <summary>The first half of an event tile: draw the card and hold it on the run.</summary>
    /// <remarks>
    /// The pending tile is deliberately not cleared, and a second call is refused: <c>EVENT_CHOOSE</c>
    /// still has to resolve the option and finds the card through the pending state. Refusing the
    /// resubmission stops a client re-drawing a card it disliked.
    /// </remarks>
    private static HandlerResult DrawEventCard(HandlerInput input, Model.Run run)
    {
        if (run.PendingEventCardId is not null)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var catalogue = EventCatalogue.Read(input.Context.Content);

        run.SetPendingEventCard(EventTileResolver.DrawCard(input, catalogue));

        return HandlerResult.Accept();
    }

    /// <summary>
    /// The Portal jump: draws its distance from the run's own board stream, applies
    /// <see cref="MovementEngine.AdvancePortal"/>'s stage-3 pre-boss campfire clamp, then either
    /// pauses at a junction inside the jump or lands — firing the Stage Gate when it comes to rest
    /// on a stage's last node.
    /// </summary>
    /// <remarks>
    /// Known, narrow gap: if the jump pauses at a junction, <c>Handlers.ChooseFork</c> resumes the
    /// remaining steps with an ordinary <see cref="MovementEngine.Advance"/> call, which has no way
    /// to know the resumed movement still owes the stage-3 clamp. In practice this is unreachable
    /// because fork placement keeps every junction strictly before the campfire, so the unclamped
    /// remainder is very unlikely to overshoot it. Left open because closing it properly needs
    /// <c>PendingFork</c> to carry an extra flag — a schema change out of scope here.
    /// </remarks>
    private static HandlerResult ResolvePortal(HandlerInput input, Model.Run run)
    {
        var board = BoardResolution.Resolve(run, input.Context.Content, input.Rng);
        var distance = MovementEngine.DrawPortalDistance(input.Rng.Stream(RngStreams.Board));
        var result = MovementEngine.AdvancePortal(board, new NodeId(run.Position), distance);

        run.ClearPendingTile();

        if (result.PausedAtJunction)
        {
            run.MoveTo(result.Node.Value);
            run.BeginPendingFork(new PendingFork(result.Node.Value, result.RemainingSteps));
            return HandlerResult.Accept();
        }

        run.MoveTo(result.Node.Value);

        // A jump that comes to rest on a stage's last node crosses the boundary exactly as a walked
        // landing does; fired before ArriveAtTile, the same ordering Handlers.RollDice uses.
        if (board.IsStageEndNode(result.Node))
        {
            StageGateResolver.Apply(input, run.CurrentHp);
        }

        var landed = board.Node(result.Node);
        run.ArriveAtTile((int)landed.Tile, landed.LinearIndex, landed.Stage);

        return HandlerResult.Accept();
    }
}
