namespace SlayIdleRepeat.Core.Model.Snapshots;

/// <summary>
/// The version of the persistence-snapshot serialisation format (<c>14</c> §16.6).
/// </summary>
/// <remarks>
/// One of the three independent version numbers in this codebase, and the one that is
/// easiest to break silently:
/// <list type="bullet">
///   <item>the assembly SemVer lives in <c>Directory.Build.props</c> and moves on releases;</item>
///   <item><c>SlayIdleRepeat.Contracts.WireProtocol.PROTOCOL_VERSION</c> versions the wire
///         envelope and lifecycle semantics;</item>
///   <item><see cref="SchemaVersion"/> versions how aggregates are written down.</item>
/// </list>
/// They are never bumped together for tidiness.
/// </remarks>
public static class SnapshotSchema
{
    /// <summary>
    /// The current snapshot schema version. Every <c>*Snapshot</c> record carries it as its
    /// first field and <c>Rehydrate</c> validates it.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Bump rule (<c>14</c> §16.6):</b> adding, removing or reordering a field of any
    /// snapshot record is a serialisation change. It bumps this number and is handled as a
    /// versioned migration — <i>never silently</i>. Field order is the declaration order of the
    /// snapshot record, depth-first, and a CI test pins the field list per version. Changing a
    /// snapshot without bumping this number also changes every <c>stateHash</c> in existence.
    /// <para>Bumping it is not tied to the assembly version or to
    /// <c>PROTOCOL_VERSION</c>: the wire can stay put while the snapshot moves, and vice versa.</para>
    /// <para>
    /// 🔒 <b>History, so a reader can tell a bump that happened from one that was skipped.</b>
    /// <b>1</b> — M0-07's format, populated by M1-04 (<c>PlayerSnapshot</c>) and M1-05
    /// (<c>RunSnapshot</c>). <b>2</b> — M1-09 added `19` Part G's two login-calendar fields to
    /// <c>PlayerSnapshot</c>. No migration is written for either: the M1 kickoff ruled that no
    /// migration code exists before soft launch and that written migrations become mandatory at M18,
    /// so a row stamped 1 is refused loudly by <c>Player.Rehydrate</c> rather than read against the
    /// wrong layout. <b>3</b> — M3-03c added <c>RunSnapshot.ResolvedMinigames</c>, `03` §6.2's
    /// per-tile minigame legality gate (position → the <c>MG_*</c> id resolved there). <b>4</b> —
    /// M3-02 added <c>RunSnapshot.PendingForkJunctionPosition</c> and
    /// <c>PendingForkRemainingSteps</c>, `03` §1.1's junction pause (<c>Run.PendingFork</c>). Same
    /// ruling, no migration. <b>5</b> — M3-03 added <c>RunSnapshot</c>'s four pending-tile fields
    /// (<c>PendingTileKind</c>, <c>PendingTileLinearIndex</c>, <c>PendingTileStage</c>,
    /// <c>PendingEventCardId</c>), the seam between arriving at a tile and resolving it — cut from a
    /// parallel lane based on SchemaVersion 3, reconciled after both landed. Same ruling, no
    /// migration. <b>6</b> — M3-05 added four fields: <c>Phase</c> (`02` §1.1's run state machine,
    /// <see cref="SlayIdleRepeat.Core.Primitives.RunPhase"/>), <c>DraftPending</c> (M3-06's documented hook),
    /// <c>RerollChargesSpentThisStage</c> and <c>StageGateDiceAnchor</c> (`03` §1.1's Stage Gate:
    /// reroll-charge refresh and the Fair-Dice bag's reset anchor). Same ruling, no migration; all
    /// four are defaulted on the record so every pre-existing positional construction still compiles
    /// against the value a run implicitly held before this task.
    /// </para>
    /// <para>
    /// <b>7</b> — M3-13 added <c>RunSnapshot.BankedLegendXp</c>, <c>BankedSoulShards</c> and
    /// <c>BossDefeated</c> (`02` §5's reward-banking and run-end payout — see <c>Run.BankRewards</c>
    /// and <c>Run.EndRun</c>), and <c>PlayerSnapshot.ClearedChapterTiers</c> (`02` §5.3's first-clear
    /// gate). Same ruling, no migration; all four fields are defaulted so every pre-existing
    /// positional construction still compiles.
    /// </para>
    /// </remarks>
    public const int SchemaVersion = 7;
}
