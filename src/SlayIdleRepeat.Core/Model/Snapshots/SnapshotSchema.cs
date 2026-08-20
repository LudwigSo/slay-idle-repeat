namespace SlayIdleRepeat.Core.Model.Snapshots;

/// <summary>The version of the persistence-snapshot serialisation format. Independent of the assembly SemVer and the wire protocol version.</summary>
public static class SnapshotSchema
{
    /// <summary>
    /// The current snapshot schema version. Every <c>*Snapshot</c> record carries it as its first
    /// field and <c>Rehydrate</c> validates it. Adding, removing or reordering a field is a
    /// serialisation change: it bumps this number and is a versioned migration, never silent — and
    /// changes every existing <c>stateHash</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 18 adds two <c>RunSnapshot</c> fields — <c>FixedDice</c> and <c>PendingFixedDieChoices</c> —
    /// for the run-scoped fixed dice that replaced the die's special faces: a die showing a chosen
    /// number, spent instead of a roll to move exactly that far.
    /// </para>
    /// <para>
    /// 17 drops five <c>RunSnapshot</c> fields at once — <c>RerollChargesSpentThisStage</c>,
    /// <c>StageGateDiceAnchor</c>, <c>DieFaceUpgrades</c>, <c>RerollChargesGrantedThisStage</c> and
    /// <c>ChainLinksTaken</c> — with the reroll charge, the weighted dice bag and the die's special
    /// faces. <c>FreeDraftRerolls</c> stays: the perk draft's reroll is a different mechanic.
    /// </para>
    /// </remarks>
    public const int SchemaVersion = 18;
}
