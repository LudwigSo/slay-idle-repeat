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
    public const int SchemaVersion = 11;
}
