namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 M3-05, `02` §1.1 — the subset of the run's client-presentation state machine that is genuine
/// <b>server-side</b> aggregate state.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The ruling, and the test every member was held to.</b> `02` §1.1's diagram draws nine UI
/// states — <c>RUN_SETUP</c>, <c>AWAIT_ROLL</c>, <c>ROLLING</c>, <c>REROLL_PROMPT</c>, <c>MOVING</c>,
/// <c>RESOLVE_TILE</c>, the branch screens (<c>BATTLE</c>/<c>PERK_DRAFT</c>/<c>SHOP</c>/<c>EVENT</c>/
/// <c>MINIGAME</c>), <c>CHECK_STATE</c>, <c>DEATH_PROMPT</c> and <c>RUN_RESULTS</c> — and it is a
/// <em>client presentation</em> machine (a 0.8s die animation, banners), not a description of what
/// <c>Run</c> must persist. `14` §2.3's own <c>ROLL_DICE</c> answers face, movement and landing in
/// <b>one</b> command, so nothing server-side ever stands "mid-roll" between two commands. The only
/// question this enum answers for each of the nine is: <em>does <c>GameRules.Apply</c> need to tell
/// this state apart from another to validate a command or produce a result?</em> — never "does the UI
/// animate it".
/// </para>
/// <list type="table">
///   <listheader><term>UI state</term><description>Server-side? Why.</description></listheader>
///   <item><term><c>RUN_SETUP</c></term><description>No server phase — there is no <c>Run</c> yet to
///   hold one; this is <c>START_RUN</c>'s own precondition on <c>Player</c>.</description></item>
///   <item><term><c>AWAIT_ROLL</c></term><description>This <em>is</em> <see cref="InProgress"/> — the
///   default, "nothing else is open" state a run spends almost all its life in.</description></item>
///   <item><term><c>ROLLING</c></term><description>No server phase. Purely the 0.8s client die
///   animation over an answer <c>ROLL_DICE</c> already returned in full — there is no "roll in
///   flight" a second command could observe.</description></item>
///   <item><term><c>REROLL_PROMPT</c></term><description>No server phase. <c>USE_REROLL</c> is a
///   self-contained command with its own legality (see <c>Handlers.UseReroll</c>'s charge-cap
///   check); nothing pauses waiting for it that a later command needs to see.</description></item>
///   <item><term><c>MOVING</c></term><description>No server phase. Movement resolves synchronously
///   inside <c>ROLL_DICE</c>/<c>CHOOSE_FORK</c>; a junction pause is already server state, but it is
///   <c>Run.PendingFork</c>, not a phase — <c>CHOOSE_FORK</c> is the only legal next move regardless
///   of what phase would say, so a phase value would duplicate a fact <c>PendingFork</c> already
///   carries.</description></item>
///   <item><term><c>RESOLVE_TILE</c></term><description>No server phase, for the same reason —
///   <c>Run.HasPendingTile</c> (`03` §2) already gates every tile-resolution command, and a phase
///   member here would be a second flag for one fact.</description></item>
///   <item><term>Battle/Perk-draft/Shop/Event/Minigame branch screens</term><description>Four of the
///   five have no server phase: <c>Run.PendingTileKind</c> already says which branch a pending tile
///   opens (`03` §2, `03` §3's tile resolvers), and the shop, event, minigame and perk-draft branches
///   each resolve through their own command with no further server-visible pause. The
///   <b>battle</b> branch is the one exception — see <see cref="BattlePending"/>.</description></item>
///   <item><term><c>CHECK_STATE</c></term><description>No server phase. A pure UI beat between one
///   tile's resolution and the next roll; nothing server-side is "checking" anything a stored value
///   would need to survive between two commands.</description></item>
///   <item><term><c>DEATH_PROMPT</c></term><description>No server phase <em>in this milestone</em>.
///   `02` §6's once-per-run ad-revive is <c>ReviveCommand</c>, still <c>Deferred</c> to M3-13 — no
///   handler in M3-05 ever reduces a run's HP to 0, so there is no death state for a phase to name
///   yet. M3-13 is where this is revisited, once a rule can actually produce it.</description></item>
///   <item><term><c>RUN_RESULTS</c></term><description>No server phase distinguishing
///   Victory/Death/Abandoned — see <see cref="Ended"/>: one terminal value is enough for every
///   <em>command</em> to be refused once a run is over, and which way it ended is a fact for the
///   event stream / <c>EndRunCommand</c>'s own payload (M3-13) to carry, not a fact a future command
///   needs to branch on.</description></item>
/// </list>
/// <para>
/// 🔒 <b>The one addition beyond "does nothing UI-only survive": <see cref="BattlePending"/>.</b>
/// `14` §2.3 splits a fight across two commands — <c>START_BATTLE</c> opens it,
/// <c>CONFIRM_BATTLE_RESULT</c> closes it — so, unlike every other branch, something genuinely has to
/// persist <em>between</em> them: which battle is open, so a stray <c>ROLL_DICE</c> or a second
/// <c>START_BATTLE</c> cannot be dispatched while one is. <c>Run.PendingTileKind</c> alone cannot
/// carry that: it says an <c>Enemy</c>/<c>Elite</c>/<c>Boss</c> tile is pending <b>before</b>
/// <c>START_BATTLE</c> as well as during the fight, and does not change when the fight opens — so a
/// command dispatched the instant after <c>START_BATTLE</c> and one dispatched the instant before it
/// look identical without this member.
/// </para>
/// <para>
/// 🔒 <b><see cref="Ended"/> is authored with no producer in M3-05, and that is a stated, not a
/// hidden, gap.</b> <c>GapRegister</c>'s own <c>RunPhase</c> entry names the consequence this pays
/// for: <em>"without a phase, <c>Apply</c> cannot produce `14` §16.2's <c>RUN_ALREADY_ENDED</c>"</em>.
/// <c>EndRunCommand</c> and <c>AbandonRunCommand</c> are still <c>Deferred</c> to M3-13, so nothing in
/// this milestone ever transitions a run into this value — but the value exists now, so M3-13 adds a
/// mutator rather than a <see cref="SnapshotSchema.SchemaVersion"/> bump of its own, and
/// <c>GameRules.Execute</c>'s phase gate (this milestone's) already answers <c>RUN_ALREADY_ENDED</c>
/// for any run a future caller constructs at this phase — see <c>RunPhaseGuardTests</c>.
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
    /// The run is over. No <c>CommandKind.Run</c> command is legal against a run at this phase;
    /// <c>GameRules.Execute</c> answers <see cref="Primitives.RejectionReason.RUN_ALREADY_ENDED"/>.
    /// Nothing in M3-05 transitions a run here — see this type's remarks.
    /// </summary>
    Ended = 2,
}
