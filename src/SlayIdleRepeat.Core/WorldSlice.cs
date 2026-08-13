using SlayIdleRepeat.Core.Model;

namespace SlayIdleRepeat.Core;

/// <summary>
/// 🔒 `30` §4.1 — the aggregates one command may read or write. <em>"<c>Apply</c> never receives
/// 'the world'. It receives exactly the aggregates a given command may touch. Loading the right
/// slice is the Application layer's job. Deciding what happens to it is the domain's."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this lives in the <c>SlayIdleRepeat.Core</c> root and not under <c>Core/Model/</c>.</b>
/// `30` §4.1 writes it inside §4, the aggregates chapter — but it is not an aggregate, it is
/// <c>Apply</c>'s <em>parameter</em>, and the difference is enforced:
/// <c>AccessibilityBoundaryTests.Apply_is_the_only_public_mutation</c> forbids a public constructor
/// on any public type under <c>Core/Model/</c>, while `30` §4.1 requires the <b>Application</b>
/// layer to construct one on every command. A <c>WorldSlice</c> under <c>Model/</c> would therefore
/// have to choose between failing that rule and being unbuildable by the layer whose job it is to
/// build it. It sits beside <c>GameContext</c> — <c>Apply</c>'s other argument, in the root for the
/// same reason — and beside <see cref="CommandResult"/>, its return.
/// </para>
/// <para>
/// 🔒 <b>Two members in M1, and the other two of `30` §4.1's four are registered gaps</b>
/// (kickoff assumption <b>A4</b>). §4.1 sketches
/// <c>(Player, Run?, GuildView?, GhostSnapshot?)</c>; <c>GuildView</c> is M14's and
/// <c>GhostSnapshot</c> is M12's, and neither type exists. A <c>WorldSlice</c> is <b>not</b> a
/// persisted snapshot — nothing hashes it and no row stores it — so adding a nullable member later
/// costs no <c>SnapshotSchema.SchemaVersion</c> bump, whereas an empty placeholder <c>GuildView</c>
/// authored now would be the plausible-looking hole steering <b>S6</b> forbids, sitting under M12's
/// duel and M14's guild rules. Both are declared in
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>, keyed on a type that must not yet exist, so
/// the build fails on the day each becomes writable.
/// </para>
/// <para>
/// ⚠️ <b>The slice is a pair of references, not a copy.</b> Constructing one does not clone the
/// aggregates; <c>GameRules.Apply</c> is what clones, on the way in, so that `30` §2.1's <b>P4</b>
/// holds for the caller's slice. A caller that builds a slice and then mutates the aggregate it put
/// in it has mutated the slice.
/// </para>
/// </remarks>
/// <param name="Player">
/// The player the command is applied to. Never null: `30` §4 makes <c>Run</c> a <b>child</b> of
/// <c>Player</c>, so there is no command in the game that touches no player.
/// </param>
/// <param name="Run">
/// The run in flight, or <c>null</c> outside a run. ⚠️ A <c>null</c> here for a
/// <c>CommandKind.Run</c> command is a <b>loading defect</b>, not a rejection — `14` §16.2's
/// <c>RUN_NOT_FOUND</c> is a transport-tier value that never reaches <c>Apply</c> (`30` §2).
/// </param>
public sealed record WorldSlice(Player Player, Run? Run)
{
    private readonly Player _player = RequirePlayer(Player);

    private readonly Run? _run = RequireOwnedRun(Player, Run);

    /// <inheritdoc cref="WorldSlice(Player, Run)" path="/param[@name='Player']"/>
    public Player Player
    {
        get => _player;
        init => _player = RequirePlayer(value);
    }

    /// <inheritdoc cref="WorldSlice(Player, Run)" path="/param[@name='Run']"/>
    public Run? Run
    {
        get => _run;

        // 🔒 Reads the FIELD, not the Player property. The synthesized copy constructor copies
        // backing fields and then calls the plain init setters, so on the `with` path the parameter
        // is not yet the slice's player — this is the same ordering hazard RequirePlayer's remarks
        // describe, one member over.
        init => _run = RequireOwnedRun(_player, value);
    }

    /// <summary>
    /// 🔒 The guard behind <see cref="Player"/>, in the <c>init</c> accessor rather than in a
    /// property initialiser so it runs on the <c>with</c> path too.
    /// </summary>
    /// <remarks>
    /// The same construction, and the same reason, as <c>GameContext</c>'s guards: an initialiser
    /// runs only in the primary constructor, while the synthesized copy constructor copies backing
    /// fields and then calls the plain <c>init</c> setters — so <c>slice with { Player = null! }</c>
    /// would otherwise produce a slice with a hole, exactly where nobody was looking.
    /// </remarks>
    private static Player RequirePlayer(Player player) =>
        player ?? throw new ArgumentNullException(
            nameof(Player),
            "A WorldSlice always names a player. 30 §4 models Run as a CHILD of Player — every run " +
            "mutation also touches player state (rewards, XP, pity) — so there is no command in the " +
            "game that touches a run and no player. A null here is the Application layer loading the " +
            "wrong slice (30 §4.1), not a state the domain can be in.");

    /// <summary>
    /// 🔒 <c>30</c> §4's <b>child-not-peer</b> modelling, enforced rather than described: the run in
    /// a slice belongs to the player in that slice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Until the M1 review this was stated in the type's own doc comment and checked <b>nowhere</b>.
    /// <c>Apply(new WorldSlice(playerA, runOfPlayerB), …)</c> was a legal public call: it cloned both,
    /// dispatched, folded RNG positions and stamped <c>MarkApplied</c>, and returned a result pairing
    /// player A with a mutated run belonging to player B — gold moved, HP written, and no rule of any
    /// shape able to see it. <c>Apply_is_the_only_public_mutation</c> quantifies over <c>Core/Model/</c>
    /// and <c>WorldSlice</c> lives in the root, deliberately; nothing keyed on ownership at all.
    /// </para>
    /// <para>
    /// This is the same failure <see cref="RequirePlayer"/> already names — the Application layer
    /// loading the wrong slice (`30` §4.1) — for the member that had the guard. A cross-player run is
    /// not a state the domain can be in, so it throws rather than rejecting: `14` §16.2 has no value
    /// for it, and a caller that built this slice is miswired, not refused.
    /// </para>
    /// </remarks>
    private static Run? RequireOwnedRun(Player player, Run? run) =>
        run is null || run.PlayerId == player.Id
            ? run
            : throw new ArgumentException(
                "The run in this slice belongs to " + run.PlayerId + ", not to " + player.Id + ". " +
                "30 §4 models Run as a CHILD of Player — single writer, owned by exactly one player " +
                "— so a slice pairing one player's run with another player is the Application layer " +
                "loading the wrong slice (30 §4.1). Apply would otherwise move Gold, write HP, fold " +
                "RNG stream positions and stamp the run, and return the result as if it belonged to " +
                "the player named here.",
                nameof(Run));
}
