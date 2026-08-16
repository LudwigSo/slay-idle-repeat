using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model.Gear;

/// <summary>
/// The stock a player carries: the items in it, the items a full stock could not take, and the
/// expansions that have been bought. A component of the <c>Player</c> aggregate, never a root.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Hold, never lose.</b> No random source in this game may be able to starve a player, and a
/// grant that arrives while the stock is full and is silently dropped is the same wound from the
/// other side. <see cref="Place"/> therefore never refuses and never destroys: at capacity it
/// answers <see cref="InventoryPlacement.HELD"/> and appends to the holding list, and
/// <see cref="Remove"/> and <see cref="PurchaseExpansion"/> pull held items back in arrival order as
/// soon as there is room. Nothing here caps, decays or expires the holding list, because no
/// document authorises any of the three — <c>InventoryTuning.OverflowCapacity</c> carries that
/// absence where a reader can find it.
/// </para>
/// <para>
/// 🔒 <b>The lock transition lives here, not on the item.</b> <c>GearInstance</c> deliberately has
/// no <c>WithLock</c>: a hand-written member on a grant outcome that answers another grant outcome
/// is the shape the luck-routing rule was narrowed to catch. <see cref="SetLock"/> names no
/// <c>GearInstance</c> in its signature at all, so it never presents that shape; it rebuilds the
/// instance through the internal constructor inside its own body.
/// </para>
/// <para>
/// <b>Public type, internal constructor, internal mutators.</b> The type is public because the
/// aggregate exposes it; every operation that changes state is <c>internal</c>, so the only public
/// way to move an inventory is <c>GameRules.Apply</c>. <see cref="ToSnapshot"/> and
/// <see cref="Rehydrate"/> are the validating pair the persistence adapter needs, exactly as they
/// are on <c>Player</c>.
/// </para>
/// <para>
/// ⚠️ <b><see cref="Rehydrate"/> does not reclaim.</b> It reads the row as written: a stored list
/// with room in it and a held item beside it stays that way until something actually opens a slot.
/// A rehydration that tidied up would edit a player's state on the way in, and the edit would be
/// invisible — the row it produced would look exactly like one the game had written itself.
/// </para>
/// </remarks>
public sealed class Inventory
{
    /// <summary>The items in stock, in grant order. Mutated in place, so the view stays valid.</summary>
    private readonly List<GearInstance> _stored;

    private readonly ReadOnlyCollection<GearInstance> _storedView;

    /// <summary>The items a full stock could not take, in arrival order.</summary>
    private readonly List<GearInstance> _held;

    private readonly ReadOnlyCollection<GearInstance> _heldView;

    private int _expansionsPurchased;

    /// <summary>The one constructor. Every value has already been checked by <see cref="Rehydrate"/>, its only caller.</summary>
    internal Inventory(List<GearInstance> stored, List<GearInstance> held, int expansionsPurchased)
    {
        _stored = stored;
        _storedView = new ReadOnlyCollection<GearInstance>(stored);
        _held = held;
        _heldView = new ReadOnlyCollection<GearInstance>(held);
        _expansionsPurchased = expansionsPurchased;
    }

    /// <summary>The items in stock, in grant order — the newest last.</summary>
    /// <remarks>
    /// Grant order is load-bearing rather than incidental: the "newest" ordering reads it, and a
    /// container that normalised its order would leave that ordering with nothing to sort by. A live
    /// view, like <c>Player.DailyCounters</c>: read it, do not hold it.
    /// </remarks>
    public IReadOnlyList<GearInstance> Stored => _storedView;

    /// <summary>The items waiting for space, in arrival order — the oldest first.</summary>
    /// <remarks>Arrival order is what the reclaim rule pulls them back in, so it has to be an order at all.</remarks>
    public IReadOnlyList<GearInstance> Held => _heldView;

    /// <summary>How many capacity expansions this player has bought.</summary>
    public int ExpansionsPurchased => _expansionsPurchased;

    /// <summary>How many items the stock can hold at the expansions bought so far.</summary>
    /// <param name="tuning">The inventory numbers.</param>
    /// <returns>The capacity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    internal int CapacityWith(InventoryTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        return tuning.CapacityAt(_expansionsPurchased);
    }

    /// <summary>Which of the four states an item of this identity is in.</summary>
    /// <param name="instanceId">The identity a caller named.</param>
    /// <returns>The availability, told apart rather than collapsed to "not usable".</returns>
    internal ItemAvailability Availability(GearInstanceId instanceId)
    {
        var stored = IndexOf(_stored, instanceId);
        if (stored >= 0)
        {
            return _stored[stored].Locked ? ItemAvailability.LOCKED : ItemAvailability.AVAILABLE;
        }

        return IndexOf(_held, instanceId) >= 0
            ? ItemAvailability.HELD_IN_OVERFLOW
            : ItemAvailability.UNKNOWN_ITEM;
    }

    /// <summary>Takes an item the player has been granted, storing it or holding it.</summary>
    /// <param name="item">The item. Produced by whatever granted it; this only files it.</param>
    /// <param name="tuning">The inventory numbers, for the capacity.</param>
    /// <returns>Where it landed. Never a refusal — see the type's remarks.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The identity is already owned. A repeat makes every operation that names an id ambiguous, and
    /// it is the one invariant <see cref="Rehydrate"/> refuses a persisted row for.
    /// </exception>
    internal InventoryPlacement Place(GearInstance item, InventoryTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(tuning);

        if (Availability(item.InstanceId) != ItemAvailability.UNKNOWN_ITEM)
        {
            throw new InvalidOperationException(
                "'" + item.InstanceId.Value + "' is already owned. A gear instance id names one " +
                "rolled item, so a second copy of the identity would make every command that names " +
                "it ambiguous — including the salvage that would then destroy the wrong one.");
        }

        if (_stored.Count < tuning.CapacityAt(_expansionsPurchased))
        {
            _stored.Add(item);
            return InventoryPlacement.STORED;
        }

        _held.Add(item);
        return InventoryPlacement.HELD;
    }

    /// <summary>Locks or unlocks a stored item.</summary>
    /// <param name="instanceId">The item to move.</param>
    /// <param name="locked">The flag to leave it at.</param>
    /// <returns>
    /// <see langword="true"/> when the flag actually moved. <see langword="false"/> when it was
    /// already there, and when the identity is not in stock at all.
    /// </returns>
    /// <remarks>
    /// A <see langword="false"/> rather than a throw for an id the stock does not hold: a client
    /// naming an item the player does not have is a rejection, not a defect, and a handler turns it
    /// into one by asking <see cref="Availability"/> — which tells an unknown id from a held one,
    /// where this return value cannot.
    /// </remarks>
    internal bool SetLock(GearInstanceId instanceId, bool locked)
    {
        var index = IndexOf(_stored, instanceId);
        if (index < 0)
        {
            return false;
        }

        var current = _stored[index];
        if (current.Locked == locked)
        {
            return false;
        }

        _stored[index] = new GearInstance(
            current.InstanceId,
            current.DefId,
            current.Slot,
            current.Family,
            current.Rarity,
            current.ChapterOrigin,
            current.Quality,
            current.EnhanceLevel,
            current.EnhanceFailures,
            current.Affixes,
            locked);

        return true;
    }

    /// <summary>Takes an item out of the inventory, wherever it is.</summary>
    /// <param name="instanceId">The item to remove.</param>
    /// <param name="tuning">The inventory numbers, for the reclaim the removal may open room for.</param>
    /// <returns><see langword="true"/> when something was removed.</returns>
    /// <remarks>
    /// Removing a <em>held</em> item reclaims nothing: the stock did not change, so no slot opened,
    /// and a reclaim here would pull an item into a place that never became free.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    internal bool Remove(GearInstanceId instanceId, InventoryTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        var stored = IndexOf(_stored, instanceId);
        if (stored >= 0)
        {
            _stored.RemoveAt(stored);
            Reclaim(tuning);
            return true;
        }

        var held = IndexOf(_held, instanceId);
        if (held < 0)
        {
            return false;
        }

        _held.RemoveAt(held);
        return true;
    }

    /// <summary>Buys one capacity expansion and reclaims whatever the new slots can take.</summary>
    /// <param name="tuning">The inventory numbers, for the cap and the new capacity.</param>
    /// <remarks>
    /// It does not charge for anything — the price is the handler's, and the handler checks the cap
    /// before it debits anybody. Reaching this past the cap is therefore a miswired caller rather
    /// than a player asking for something they cannot have, which is why it throws where
    /// <see cref="SetLock"/> answers <see langword="false"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Every expansion the ladder prices has been bought.</exception>
    internal void PurchaseExpansion(InventoryTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        if (_expansionsPurchased >= tuning.MaxPurchases)
        {
            throw new InvalidOperationException(
                "All " + Text(tuning.MaxPurchases) + " expansions have been bought, so there is no " +
                "capacity and no price for another. The handler refuses the purchase before it " +
                "charges anybody; reaching here means a caller debited without checking the cap.");
        }

        _expansionsPurchased++;
        Reclaim(tuning);
    }

    /// <summary>The persisted shape of this component.</summary>
    /// <remarks>
    /// Both lists are copied into fresh read-only arrays rather than handed out: a shared reference
    /// would let a later grant rewrite a snapshot that has already been taken, and a record's
    /// synthesized equality compares an <c>IReadOnlyList&lt;T&gt;</c> component by <em>reference</em>
    /// — so the sharing would be invisible to every comparison that looked for it.
    /// </remarks>
    /// <returns>The row.</returns>
    public InventorySnapshot ToSnapshot() =>
        new(_expansionsPurchased, Persist(_stored), Persist(_held));

    /// <summary>
    /// The structural half of a persisted inventory: the row names items, each of them is one the
    /// domain can build, and no identity appears twice.
    /// </summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <returns>
    /// The rehydrated component, or a failure listing every validation the row failed, not just the
    /// first.
    /// </returns>
    /// <remarks>
    /// ⚠️ <b>It deliberately does not check the row against a capacity</b>, and this is the door the
    /// <c>Player</c> aggregate loads through. The reason is the one <c>Player</c> already records for
    /// its Energy ceiling: capacity is a <em>tunable</em>, a balance patch that lowered it would
    /// leave real players above the new one, and refusing to load such a row would turn a tuning
    /// change into an account outage. The ceiling belongs on the operations that grow the stock —
    /// <see cref="Place"/> holds rather than overfills — and on the overload that is handed the
    /// numbers.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    internal static Result<Inventory> Rehydrate(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var faults = new List<string>();

        if (snapshot.ExpansionsPurchased < 0)
        {
            faults.Add(
                nameof(InventorySnapshot.ExpansionsPurchased) + " is " +
                Text(snapshot.ExpansionsPurchased) + ". Expansions are bought, never sold, so the " +
                "count starts at zero and only grows.");
        }

        var owned = new HashSet<string>(
            (snapshot.Stored?.Count ?? 0) + (snapshot.Held?.Count ?? 0), StringComparer.Ordinal);
        var stored = ReadItems(snapshot.Stored, nameof(InventorySnapshot.Stored), owned, faults);
        var held = ReadItems(snapshot.Held, nameof(InventorySnapshot.Held), owned, faults);

        if (faults.Count > 0 || stored is null || held is null)
        {
            return Failure(faults);
        }

        return Result<Inventory>.Success(new Inventory(stored, held, snapshot.ExpansionsPurchased));
    }

    /// <summary>
    /// The whole validated entry point: everything <see cref="Rehydrate(InventorySnapshot)"/> checks,
    /// plus the two claims that need the authored numbers — the purchase count is one the ladder
    /// prices, and the stock is no larger than those purchases paid for.
    /// </summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <param name="tuning">The inventory numbers.</param>
    /// <returns>The rehydrated component, or a failure listing every validation the row failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="tuning"/> is null.</exception>
    internal static Result<Inventory> Rehydrate(InventorySnapshot snapshot, InventoryTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(tuning);

        var structural = Rehydrate(snapshot);
        if (structural.IsFailure)
        {
            return structural;
        }

        var faults = new List<string>();

        if (snapshot.ExpansionsPurchased > tuning.MaxPurchases)
        {
            faults.Add(
                nameof(InventorySnapshot.ExpansionsPurchased) + " is " +
                Text(snapshot.ExpansionsPurchased) + " and the ladder prices " +
                Text(tuning.MaxPurchases) + ". A row past the cap claims purchases nobody could " +
                "have made.");
        }
        else if (structural.Value.Stored.Count > tuning.CapacityAt(snapshot.ExpansionsPurchased))
        {
            faults.Add(
                nameof(InventorySnapshot.Stored) + " holds " + Text(structural.Value.Stored.Count) +
                " items and " + Text(snapshot.ExpansionsPurchased) + " expansion(s) pay for " +
                Text(tuning.CapacityAt(snapshot.ExpansionsPurchased)) + ". A stock larger than the " +
                "purchases bought is a row written against different tuning, and reading it would " +
                "hand this player slots nobody paid for.");
        }

        return faults.Count > 0 ? Failure(faults) : structural;
    }

    /// <summary>The refusal every rehydration path shares, so a corrupt row reads the same either way.</summary>
    private static Result<Inventory> Failure(List<string> faults) =>
        Result<Inventory>.Failure(
            "This InventorySnapshot is not a state the game can be in (" + Text(faults.Count) +
            " problem(s)): " + string.Join(" | ", faults));

    /// <summary>Reads one persisted list, refusing a null list, a null row, a corrupt item or a repeated identity.</summary>
    private static List<GearInstance>? ReadItems(
        IReadOnlyList<GearInstanceSnapshot>? rows,
        string field,
        HashSet<string> owned,
        List<string> faults)
    {
        if (rows is null)
        {
            faults.Add(field + " is null. An absent list is not an empty one.");
            return null;
        }

        var items = new List<GearInstance>(rows.Count);
        var faulted = false;

        foreach (var row in rows)
        {
            if (row is null)
            {
                faults.Add(field + " carries a null row, which names no item at all.");
                faulted = true;
                continue;
            }

            // The item's own constructor is the validation — quality range, chapter floor, the
            // affix exclusion — and a row that trips it is a corrupt save rather than a caller
            // defect, so it becomes a fault here instead of escaping as an exception.
            GearInstance item;
            try
            {
                item = new GearInstance(
                    row.InstanceId,
                    row.DefId,
                    row.Slot,
                    row.Family,
                    row.Rarity,
                    row.ChapterOrigin,
                    row.Quality,
                    row.EnhanceLevel,
                    row.EnhanceFailures,
                    row.Affixes,
                    row.Locked);
            }
            catch (ArgumentException problem)
            {
                faults.Add(field + " carries an item the domain refuses: " + problem.Message);
                faulted = true;
                continue;
            }

            if (!owned.Add(item.InstanceId.Value))
            {
                faults.Add(
                    field + " names '" + item.InstanceId.Value + "', which this inventory already " +
                    "holds. One identity is one rolled item, and an inventory carrying it twice " +
                    "makes every command that names it ambiguous.");
                faulted = true;
                continue;
            }

            items.Add(item);
        }

        return faulted ? null : items;
    }

    /// <summary>Pulls held items back into stock, oldest first, for as long as there is room.</summary>
    private void Reclaim(InventoryTuning tuning)
    {
        var capacity = tuning.CapacityAt(_expansionsPurchased);

        while (_held.Count > 0 && _stored.Count < capacity)
        {
            _stored.Add(_held[0]);
            _held.RemoveAt(0);
        }
    }

    /// <summary>The persisted form of one list, as a fresh read-only array.</summary>
    private static IReadOnlyList<GearInstanceSnapshot> Persist(List<GearInstance> items)
    {
        var rows = new GearInstanceSnapshot[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            rows[i] = new GearInstanceSnapshot(
                item.InstanceId,
                item.DefId,
                item.Slot,
                item.Family,
                item.Rarity,
                item.ChapterOrigin,
                item.Quality,
                item.EnhanceLevel,
                item.EnhanceFailures,
                item.Affixes,
                item.Locked);
        }

        return Array.AsReadOnly(rows);
    }

    /// <summary>Where an identity sits in a list, or <c>-1</c>.</summary>
    private static int IndexOf(List<GearInstance> items, GearInstanceId instanceId)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].InstanceId.Equals(instanceId))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>, so messages read the same on every host.</summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
