namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The persisted shape of a player's stock: what fits, what is waiting, and what was bought.</summary>
/// <param name="ExpansionsPurchased">
/// How many capacity expansions have been bought. Decides the stock's capacity together with the
/// authored base and step.
/// </param>
/// <param name="Stored">The items in stock, in grant order — the newest last.</param>
/// <param name="Held">
/// The items a full stock could not take, in arrival order. They are owned and unreachable, and they
/// come back the moment space exists.
/// </param>
/// <remarks>
/// <para>
/// 🔒 <b>Two lists, never one plus a count.</b> Where an item is <em>is</em> state: a row that
/// concatenated them would hand a player items they had been waiting on and would hash identically
/// to the row that did not. The two lists are what makes the placement survive a round trip.
/// </para>
/// <para>
/// A component of the player's row rather than a row of its own, so it carries no
/// <c>SchemaVersion</c> — see <see cref="GearInstanceSnapshot"/>'s remarks for why it lives here
/// beside the other snapshot components rather than under <c>Model/Snapshots/</c>.
/// </para>
/// <para>
/// Nothing here bounds <see cref="Held"/>. No document authors a cap, a decay or an expiry on it,
/// and <c>InventoryTuning.OverflowCapacity</c> carries that absence where a reader can see it.
/// </para>
/// </remarks>
public sealed record InventorySnapshot(
    int ExpansionsPurchased,
    IReadOnlyList<GearInstanceSnapshot> Stored,
    IReadOnlyList<GearInstanceSnapshot> Held);
