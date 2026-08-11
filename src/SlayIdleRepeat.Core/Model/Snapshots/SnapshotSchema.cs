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
    /// </remarks>
    public const int SchemaVersion = 1;
}
