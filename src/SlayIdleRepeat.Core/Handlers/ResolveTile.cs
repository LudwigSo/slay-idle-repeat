using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-03, `03` §2 — the <c>RESOLVE_TILE</c> handler: one branch per `03` §2 tile kind, over the
/// pending tile <c>Run.ArriveAtTile</c> recorded.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Three shapes of branch, and telling them apart is the whole design of this handler.</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>Resolved here and cleared</b> — <c>Empty</c>, <c>Treasure</c>, <c>Cache</c>, <c>Shrine</c>,
/// <c>Curse</c>. The tile's whole effect happens in this command and the pending state is cleared,
/// because nothing else is coming.
/// </item>
/// <item>
/// <b>Advanced but NOT cleared</b> — <c>Event</c> and <c>Campfire</c>. A second command
/// (<c>EVENT_CHOOSE</c>, <c>CAMPFIRE_CHOOSE</c>) finishes them, and it reads the pending state to
/// know what it is finishing. Clearing here would strand it.
/// </item>
/// <item>
/// <b>Acknowledged and NOT cleared</b> — <c>Enemy</c>, <c>Elite</c>, <c>Boss</c>, <c>Shop</c>,
/// <c>Minigame</c>, <c>DiceForge</c>, <c>Portal</c>. Each resolves through its <em>own</em> `14`
/// §2.3 command, most of which are still <c>Deferred</c> to a later milestone, so this handler
/// accepts the acknowledgement and leaves the tile pending for the command that owns it.
/// </item>
/// </list>
/// <para>
/// 🔒 <b>The Enemy/Elite/Boss branch is the seam M3-05 and M3-06 should build on, and it is left
/// deliberately rather than forgotten.</b> `02` §1-3's post-battle perk draft is M3-06's and battle
/// resolution is M3-05's; both <c>START_BATTLE</c> and <c>CONFIRM_BATTLE_RESULT</c> are still
/// <c>Deferred</c> dispatch rows, so there is no battle code path in existence to hook a draft
/// trigger into today. What this branch leaves behind is the state those handlers need:
/// <c>Run.PendingTileKind</c> says which of the three fights it is,
/// <c>Run.PendingTileLinearIndex</c> and <c>Run.PendingTileStage</c> feed
/// <see cref="EnemyPowerFormula.Compute"/>, and <c>Run.ClearPendingTile</c> is theirs to call once
/// the battle resolves — which is also the natural point to fire M3-06's draft.
/// </para>
/// <para>
/// ⚠️ <b>What this handler does not check.</b> There is no run-phase gate — `02` §1.1's state
/// machine is M3-05's (<c>GapRegister</c>'s <c>RunPhase</c> entry) — and no board validation, so
/// nothing here confirms the pending tile is the tile the run's generated board actually holds at
/// that index. That check needs the board, which is M3-02's. <c>Run.ArriveAtTile</c> is the seam
/// where it will land.
/// </para>
/// </remarks>
internal static class ResolveTile
{
    /// <summary>`03` §2 — applies <c>RESOLVE_TILE</c>.</summary>
    /// <param name="command">Carries no payload: the tile is whatever the run is standing on.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no tile is pending, or when a pending event
    /// tile has already drawn its card; otherwise accepted, with whatever events the tile's resolver
    /// produced.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 🔒 `03` §2's tile vocabulary grew a fifteenth member no branch here handles — a real defect
    /// rather than a rejection, and it throws so that the missing branch is loud instead of silently
    /// accepted as a no-op.
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

        // 🔒 The cast is where the tile vocabulary re-enters. Run stores the kind as an int because
        // 30 §11.4 forbids Model from naming Rules (see Run.PendingTileKindValue), so this handler —
        // which sits above both — is the layer that interprets it, and the `default` arm below is
        // what catches a value outside 03 §2's fourteen.
        switch ((TileKind)run.PendingTileKindValue)
        {
            case TileKind.Empty:
                // 03 §2's breather tile. Nothing to draw, nothing to pay, nothing to follow.
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
                // ⚠️ hasCleansableCurse is false and CANNOT be anything else today: Run holds no
                // curse list (GapRegister's Curses entry, M3-11's), so there is nothing to ask. This
                // is the wire-in point once that list exists — 03 §7a.5's cleanse rule is written and
                // tested in ShrineResolver, it simply has no true input yet.
                ShrineResolver.Resolve(input, hasCleansableCurse: false);
                run.ClearPendingTile();
                return HandlerResult.Accept();

            case TileKind.Curse:
            {
                // ⚠️ Pays the curse's 19 Part E reward and does NOT apply the curse — see
                // CurseTileResolver's remarks. M3-11 owns the other half.
                var events = CurseTileResolver.Resolve(input);
                run.ClearPendingTile();
                return HandlerResult.Accept(events);
            }

            case TileKind.Event:
                return DrawEventCard(input, run);

            case TileKind.Campfire:
                // 03 §2's three options are fixed — there is nothing to draw and nothing to decide
                // here. CAMPFIRE_CHOOSE reads the pending state directly, so it must survive.
                return HandlerResult.Accept();

            case TileKind.Enemy:
            case TileKind.Elite:
            case TileKind.Boss:
                // 🔒 Acknowledgement only. START_BATTLE (M3-05) is the real trigger and this pending
                // state is what it reads — see this type's remarks for the full seam.
                return HandlerResult.Accept();

            case TileKind.Shop:
            case TileKind.Minigame:
            case TileKind.DiceForge:
                // Each resolves through its own 14 §2.3 command — SHOP_BUY/SHOP_REFRESH (landed,
                // M3-08), MINIGAME_SUBMIT (landed, M3-03c), and the Dice Forge's own upgrade
                // command (M3-04's DiceForgeUpgradeResolver is written and waiting). RESOLVE_TILE is
                // an acknowledgement for all three, so the tile stays pending for them.
                return HandlerResult.Accept();

            case TileKind.Portal:
                // 03 §1.1's forward jump. The jump itself is movement, which is M3-02's; this is the
                // seam it will resolve through and a no-op until then.
                return HandlerResult.Accept();

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(input),
                    run.PendingTileKindValue,
                    "03 §2 fixes fourteen tile kinds and RESOLVE_TILE has a branch for each. A kind " +
                    "arriving here that none of them names is one of two things, both defects: the " +
                    "vocabulary grew a fifteenth member and this handler was not extended, or a " +
                    "persisted row carried a value outside it — which Run.Rehydrate deliberately " +
                    "cannot catch, because 30 §11.4 forbids Model from naming this vocabulary. " +
                    "Either way it throws, so the gap is loud rather than silently accepted as a " +
                    "tile that does nothing.");
        }
    }

    /// <summary>
    /// `03` §5 — the first half of an event tile: draw the `19` Part A card and hold it on the run.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The pending tile is deliberately NOT cleared, and a second call is refused.</b>
    /// <c>EVENT_CHOOSE</c> still has to resolve the option, and it finds the card through the pending
    /// state. Refusing the resubmission is what stops a client re-drawing a card it disliked — the
    /// unreproducible run `14` §8.1's counter model exists to prevent — and <c>ILLEGAL_STATE</c> is
    /// the honest answer: the command is legal in general, just not in this state.
    /// </remarks>
    private static HandlerResult DrawEventCard(HandlerInput input, Model.Run run)
    {
        if (run.PendingEventCardId is not null)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // Read per command out of this command's own snapshot, never cached statically — the same
        // shape MinigameSubmit reads MinigameRewardTuning in, and for 30 §3's reason.
        var catalogue = EventCatalogue.Read(input.Context.Content);

        run.SetPendingEventCard(EventTileResolver.DrawCard(input, catalogue));

        return HandlerResult.Accept();
    }
}
