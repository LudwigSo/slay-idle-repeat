using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The one tile kind a run can land on that this build has no screen for, and the command that gets
/// a run off it anyway.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>A PLACEHOLDER, AND THE WHOLE OF IT IS IN THIS ONE FILE SO IT CAN BE DELETED.</b> A Minigame
/// tile is resolved by a command a screen this build has not written would submit —
/// <c>MINIGAME_SUBMIT</c> — and <c>RESOLVE_TILE</c> leaves it pending on purpose
/// (<c>Handlers.ResolveTile</c>: *"acknowledged and not cleared"*). So a run that landed on one was
/// stuck there for good: the roll is refused while a tile is pending, the board's Continue press
/// accepted and cleared nothing, and abandoning the run was the only way off the tile. The same shape
/// of dead end <c>START_BATTLE</c> was the fix for, found the same way — by playing an exported
/// build.
/// </para>
/// <para>
/// 🔒 <b>The Event tile is no longer here, and that is the change.</b> It has a screen now:
/// <see cref="EventPresenter"/> draws the card itself, offers its options with their prices, and
/// submits <c>EVENT_CHOOSE</c> for the one the player takes. Nothing in this file stands behind that
/// tile any more — no card is read, no option is chosen on the player's behalf, and the board routes
/// an Event tile to its own screen exactly as it routes a shop or a campfire.
/// </para>
/// <para>
/// 🔒 <b>Through the real command, not through a rules change.</b> Nothing here relaxes what the
/// rules layer accepts and no tile is cleared behind its back: this picks a legal payload for the
/// command the missing screen would have submitted, so the tile resolves through the handler that
/// owns it and pays what that handler pays. That is what keeps the rest of a run testable — the
/// board, the fights, the drafts, the shop, the campfire, the event cards and the run's ending all
/// sit BEHIND this tile on any board that generates one.
/// </para>
/// <para>
/// ⚠️ <b>It plays the game for the player, and that is the cost of being a placeholder.</b> A
/// minigame is scored here rather than played, biased to the least the tile can pay — the lowest
/// outcome tier — so a placeholder can never be the profitable way to play a tile. When the real
/// screen lands, this file goes and the call site in <see cref="BoardPresenter"/> goes with it.
/// </para>
/// </remarks>
public static class UnbuiltTileScreens
{
    /// <summary>The Minigame tile's kind number — <c>MINIGAME_SUBMIT</c>'s tile.</summary>
    public const int MinigameTileKind = (int)TileKind.Minigame;

    /// <summary>
    /// The minigame a skipped Minigame tile is submitted as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>A transcription of a format another assembly owns</b>, for
    /// <see cref="BoardTileKinds"/>'s reason: <c>Content.MinigameCatalogue</c> is internal to the
    /// rules assembly, so nothing a client can reference names the four <c>MG_*</c> ids, and
    /// <c>MINIGAME_SUBMIT</c> refuses an id the catalogue does not know.
    /// </para>
    /// <para>
    /// 🔒 <b>Which of the four, and why this one.</b> The pending tile does not say — a Minigame
    /// tile is the bare kind number 8 and nothing on the run names a minigame — so one has to be
    /// picked, and only a CLIENT-ASSERTED minigame lets the outcome be pinned. The two server-rolled
    /// ones (the chest pick, the dice duel) draw their own tier from the run's stream and ignore the
    /// claim, so a skip through either would pay a random reward and, for the chest pick, advance a
    /// lifetime pity counter for a minigame nobody played. Strike the Anvil is client-asserted, so
    /// the tier below is what the tile actually pays.
    /// </para>
    /// </remarks>
    public const string PlaceholderMinigameId = "MG_TIMING_BAR";

    /// <summary>
    /// The outcome tier a skipped minigame claims — the lowest, which is the worst.
    /// </summary>
    /// <remarks>
    /// 🔒 Zero rather than a middling tier or a best one. A minigame's reward table is an ordered
    /// zero-based tier read positionally out of content, and every one of the four authors its rows
    /// worst-first, so tier 0 is the smallest payout the tile has. It is also the only index legal by
    /// construction: a table has to have at least one row to be a table at all, so 0 is inside every
    /// authored one and no other number is.
    /// </remarks>
    public const int LowestOutcomeTier = 0;

    /// <summary>Whether this tile kind is one whose screen this build has not written.</summary>
    /// <param name="tileKind">The number the run carries in its pending-tile field.</param>
    public static bool HasNoScreen(int tileKind) => tileKind == MinigameTileKind;

    /// <summary>
    /// The command that gets a run off this tile, or null when the run is not on one.
    /// </summary>
    /// <remarks>
    /// 🔒 One command and no acknowledgement first: <c>MINIGAME_SUBMIT</c> reads the pending tile
    /// and clears it in one step, and <c>RESOLVE_TILE</c> on the same tile is accepted and clears
    /// nothing — which is the dead end this file exists to remove.
    /// </remarks>
    /// <param name="tileKind">The pending tile's kind.</param>
    public static GameCommand? CommandThatLeaves(int tileKind) =>
        tileKind == MinigameTileKind
            ? new MinigameSubmitCommand(PlaceholderMinigameId, LowestOutcomeTier)
            : null;
}
