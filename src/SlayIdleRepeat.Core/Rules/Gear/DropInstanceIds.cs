using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Gear;

/// <summary>
/// The identities an in-run grant mints for the items it hands the player.
/// </summary>
/// <remarks>
/// <para>
/// A deterministic stand-in for the wire-issued, collision-checked id the server will allocate
/// later, on <c>StartRun</c>'s precedent — <c>Guid.NewGuid()</c> is not something <c>Core</c> may
/// call, and a replay that minted different identities would store different items.
/// </para>
/// <para>
/// 🔒 <b>Why the two families cannot collide.</b> A <c>RunId</c> is unique to one player forever, so
/// a run names its own key space. Within it, the drops stream's position strictly increases across
/// every draw the run takes, and <c>END_RUN</c> runs once, so a floor grant's index is unique on its
/// own. The <c>D</c>/<c>F</c> letters keep the two spaces disjoint from each other.
/// </para>
/// </remarks>
internal static class DropInstanceIds
{
    /// <summary>The identity of one item dropped by a kill.</summary>
    /// <param name="run">The run the kill happened in.</param>
    /// <param name="drawOrdinal">The drops-stream position the item's roll started at.</param>
    /// <returns>The identity.</returns>
    internal static GearInstanceId ForKillDrop(RunId run, ulong drawOrdinal) =>
        new("GI_" + run.Value + "_D" + drawOrdinal.ToString(CultureInfo.InvariantCulture));

    /// <summary>The identity of one item the session floor grants.</summary>
    /// <param name="run">The run whose end owed the grant.</param>
    /// <param name="index">The item's position within the grant, from zero.</param>
    /// <returns>The identity.</returns>
    internal static GearInstanceId ForSessionFloor(RunId run, int index) =>
        new("GI_" + run.Value + "_F" + index.ToString(CultureInfo.InvariantCulture));
}
