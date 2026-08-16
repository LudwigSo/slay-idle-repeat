namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The subset of the run's client-presentation state machine that is genuine server-side aggregate state.</summary>
/// <remarks>
/// <para>
/// The client draws nine UI states (setup, awaiting roll, rolling, reroll prompt, moving, tile
/// resolution, the branch screens, check state, death prompt, results), but that is a presentation
/// machine — a die animation, banners — not a description of what the <c>Run</c> aggregate must
/// persist. <c>ROLL_DICE</c> answers face, movement and landing in one command, so nothing server-side
/// ever stands "mid-roll" between two commands. The question this enum answers for each UI state is
/// only: does <c>GameRules.Apply</c> need to tell it apart from another to validate a command or
/// produce a result — never whether the UI animates it.
/// </para>
/// <para>
/// Most of the nine UI states need no phase of their own: a junction pause is already carried by
/// <c>Run.PendingFork</c>, a pending tile by <c>Run.HasPendingTile</c>, and the branch screens (shop,
/// event, minigame, perk draft) by <c>Run.PendingTileKind</c> — a phase member for any of them would
/// duplicate a fact already stored elsewhere. <see cref="BattlePending"/> is the one exception: a
/// fight splits across two commands (<c>START_BATTLE</c> opens it, <c>CONFIRM_BATTLE_RESULT</c>
/// closes it), and <c>PendingTileKind</c> alone can't distinguish "about to fight" from "fighting",
/// so something has to persist between the two calls to block a stray second command.
/// </para>
/// <para>
/// <see cref="Ended"/> is produced by <c>END_RUN</c> and <c>ABANDON_RUN</c>, and
/// <c>GameRules.Execute</c>'s phase gate answers <c>RUN_ALREADY_ENDED</c> to a run command that
/// arrives afterwards.
/// </para>
/// </remarks>
public enum RunPhase
{
    /// <summary>
    /// The default phase, and the one a freshly-started run begins in: nothing is open. Every
    /// <c>CommandKind.Run</c> command but <c>CONFIRM_BATTLE_RESULT</c> is legal here (subject to its
    /// own finer-grained checks — <c>Run.PendingFork</c>, <c>Run.HasPendingTile</c>, and so on).
    /// </summary>
    InProgress = 0,

    /// <summary>
    /// A battle is open: <c>START_BATTLE</c> has run and <c>CONFIRM_BATTLE_RESULT</c> has not yet
    /// closed it. Every other <c>CommandKind.Run</c> command is illegal while a run stands here.
    /// </summary>
    BattlePending = 1,

    /// <summary>
    /// The run is over. Every <c>CommandKind.Run</c> command is answered
    /// <see cref="Primitives.RejectionReason.RUN_ALREADY_ENDED"/> by <c>GameRules.Execute</c>, with
    /// one exemption: the row that opens its own run (<c>START_RUN</c>) is let through, since a
    /// finished run is what the next one starts over and nothing else in the game clears it.
    /// </summary>
    Ended = 2,
}
