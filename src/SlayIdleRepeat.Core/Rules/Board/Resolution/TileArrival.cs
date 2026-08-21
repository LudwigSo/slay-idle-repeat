using SlayIdleRepeat.Core.Model;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// The one place a run comes to rest on a board node — and therefore the one place `03` §7.1's
/// armed Escape Rope can stop the node's content from happening.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One seam, three callers, and that is the point.</b> A run lands from an ordinary roll
/// (<c>Handlers.RollDice</c>), from the resumed half of a movement a junction interrupted
/// (<c>Handlers.ChooseFork</c>) and from a Portal jump (<c>Handlers.ResolveTile</c>). A rope honoured
/// at only some of them is a consumable that works depending on how the player got there, which is
/// indistinguishable from a bug in the rope.
/// </para>
/// <para>
/// 🔒 <b>The rope never skips a fight the board makes mandatory</b> — see
/// <see cref="IsMandatoryFight"/>. If the landing is one of those, its fight resolves normally and
/// the rope simply does not fire: it stays armed for a landing it is allowed to skip, which for a
/// run standing on the boss means it never fires at all. Written as "does not fire" rather than "is
/// consumed and does nothing", because a rope is consumed WHEN IT FIRES.
/// </para>
/// <para>
/// ⚠️ <b>The Stage Gate is positional, not tile content</b>, so it is deliberately NOT this type's
/// business: landing on a stage's last node with a rope armed skips that tile's content, and the
/// gate (heal, reroll refresh, checkpoint) still fires. Every caller fires the gate before calling
/// here, which is the order that keeps that true.
/// </para>
/// </remarks>
internal static class TileArrival
{
    /// <summary>
    /// Comes to rest on <paramref name="node"/>: either records it as the run's pending tile, or
    /// consumes an armed Escape Rope and skips its content entirely.
    /// </summary>
    /// <param name="run">The run, already moved onto the node.</param>
    /// <param name="node">The node landed on.</param>
    /// <returns>
    /// <c>true</c> when the rope fired and the tile was skipped — the caller's signal that no tile is
    /// pending and the run may roll again immediately.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    internal static bool Land(Run run, BoardNode node)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.EscapeRopeArmed && !IsMandatoryFight(node.Tile))
        {
            // Consumed here, on the fire, and nothing is paid: 03 §7.1 is explicit that a skipped
            // tile "pays nothing", so there is deliberately no reward branch to skip past.
            run.DisarmEscapeRope();

            return true;
        }

        run.ArriveAtTile((int)node.Tile, node.LinearIndex, node.Stage);

        return false;
    }

    /// <summary>
    /// Whether the board refuses to let a run past this node's fight, so an armed rope may not skip
    /// it either.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The rule is "movement cannot carry a run past this fight", not a list of tile kinds
    /// that happen to be boss-tier.</b> Two nodes are reached under a movement rule that refuses to
    /// carry a roll beyond them — the boss terminus, and the gate on the last node of stages 1 and 2
    /// — and a rope that skipped the fight standing on one would undo that rule one step later, for
    /// the price of a consumable. Any tile kind added under the same movement rule belongs in here;
    /// one a roll can be carried past does not, however boss-like it looks.
    /// <para>
    /// ⚠️ <b>Add to this the day such a kind is added, not after.</b> Reading a rope's exemption off
    /// a single kind is what let a gate be skippable in the first place — the kind was appended and
    /// this condition was not re-read.
    /// </para>
    /// </remarks>
    private static bool IsMandatoryFight(TileKind tile) =>
        tile is TileKind.Boss or TileKind.MiniBoss;
}
