using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model;

/// <summary>What the hero is wearing: at most one gear instance per <see cref="GearSlot"/>.</summary>
/// <remarks>
/// <para>
/// <b>Immutable, and replaced wholesale rather than mutated</b> — <c>PityCounters</c>' shape, not
/// <c>Inventory</c>'s. It has to be: a preset stores a loadout, and a mutable one would mean saving a
/// preset and then equipping a helmet silently rewrote the preset. Every mutator here answers a new
/// instance and the aggregate decides whether to keep it.
/// </para>
/// <para>
/// 🔒 <b>A slot holds an item's IDENTITY, never a copy of the item.</b> The stock is the one place a
/// gear instance lives; this names one. So equipping cannot duplicate an item, salvaging cannot leave
/// a stale copy on the hero, and an enhancement applied in the forge is worn the moment it is
/// applied, with nothing to keep in step.
/// </para>
/// <para>
/// 🔒 <b>One item is in at most one slot.</b> <see cref="With"/> clears the identity from wherever
/// else it sat before it writes the new slot, rather than refusing: a client dragging a ring from the
/// left hand to the right is asking for exactly that, and refusing would make the obvious gesture an
/// error. The invariant is kept by construction rather than checked afterwards.
/// </para>
/// <para>
/// It does <b>not</b> know what the player owns. The stock is the <c>Player</c> aggregate's, and the
/// rule that an equipped identity is one the player actually holds is enforced where both are
/// visible — on the aggregate, at rehydration, and in the handler that equips.
/// </para>
/// </remarks>
public sealed class Loadout
{
    private readonly IReadOnlyDictionary<GearSlot, GearInstanceId> _gear;

    private Loadout(IReadOnlyDictionary<GearSlot, GearInstanceId> gear) => _gear = gear;

    /// <summary>A hero wearing nothing — where a new player stands, and what an empty preset restores.</summary>
    /// <remarks>Safe to share: it is read-only and empty, so nothing can tell a shared instance from a private one.</remarks>
    public static Loadout Empty { get; } = new(
        new ReadOnlyDictionary<GearSlot, GearInstanceId>(new Dictionary<GearSlot, GearInstanceId>(0)));

    /// <summary>Which instance sits in which slot. Read-only; an empty slot is an absent key.</summary>
    public IReadOnlyDictionary<GearSlot, GearInstanceId> Gear => _gear;

    /// <summary>How many slots are filled.</summary>
    public int EquippedCount => _gear.Count;

    /// <summary>The instance in one slot, if anything is in it.</summary>
    /// <param name="slot">The slot to read.</param>
    /// <param name="item">The instance equipped there.</param>
    /// <returns><see langword="true"/> when the slot is filled.</returns>
    public bool TryGet(GearSlot slot, out GearInstanceId item) => _gear.TryGetValue(slot, out item);

    /// <summary>Whether an identity is equipped in any slot.</summary>
    /// <param name="item">The instance to look for.</param>
    /// <returns><see langword="true"/> when it is worn.</returns>
    public bool Holds(GearInstanceId item)
    {
        foreach (var equipped in _gear.Values)
        {
            if (equipped.Equals(item))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>This loadout with <paramref name="item"/> in <paramref name="slot"/>, and out of every other slot.</summary>
    /// <param name="slot">The slot to fill. One of the six the domain declares.</param>
    /// <param name="item">The instance to wear.</param>
    /// <returns>The new loadout, or this one when nothing would change.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not a declared slot, or <paramref name="item"/> has no id.</exception>
    internal Loadout With(GearSlot slot, GearInstanceId item)
    {
        RequireSlot(slot);
        RequireItem(item);

        if (_gear.TryGetValue(slot, out var current) && current.Equals(item) && CountOf(item) == 1)
        {
            return this;
        }

        var next = new Dictionary<GearSlot, GearInstanceId>(_gear);

        // Cleared BEFORE the write, so moving a ring from one hand to the other cannot delete the
        // slot it was just written into.
        foreach (var occupied in _gear.Keys)
        {
            if (_gear[occupied].Equals(item))
            {
                next.Remove(occupied);
            }
        }

        next[slot] = item;

        return new Loadout(new ReadOnlyDictionary<GearSlot, GearInstanceId>(next));
    }

    /// <summary>This loadout with <paramref name="slot"/> emptied.</summary>
    /// <param name="slot">The slot to empty.</param>
    /// <returns>The new loadout, or this one when the slot was already empty.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not a declared slot.</exception>
    internal Loadout Without(GearSlot slot)
    {
        RequireSlot(slot);

        if (!_gear.ContainsKey(slot))
        {
            return this;
        }

        var next = new Dictionary<GearSlot, GearInstanceId>(_gear);
        next.Remove(slot);

        return new Loadout(new ReadOnlyDictionary<GearSlot, GearInstanceId>(next));
    }

    /// <summary>This loadout with an identity taken off, wherever it was worn.</summary>
    /// <param name="item">The instance to take off.</param>
    /// <returns>The new loadout, or this one when it was not worn.</returns>
    /// <remarks>
    /// The seam a destructive item operation needs: an item that is salvaged, merged away or
    /// otherwise stops existing must not stay named by a slot, and the operation that destroys it
    /// knows only the identity, not where it was worn.
    /// </remarks>
    internal Loadout WithoutItem(GearInstanceId item)
    {
        if (!Holds(item))
        {
            return this;
        }

        var next = new Dictionary<GearSlot, GearInstanceId>(_gear);

        foreach (var occupied in _gear.Keys)
        {
            if (_gear[occupied].Equals(item))
            {
                next.Remove(occupied);
            }
        }

        return new Loadout(new ReadOnlyDictionary<GearSlot, GearInstanceId>(next));
    }

    /// <summary>The persisted shape.</summary>
    /// <returns>The row.</returns>
    /// <remarks>
    /// The map is handed out rather than copied, on the wallet's precedent: this type is replaced
    /// wholesale on every change, so the object a snapshot holds can never change afterwards.
    /// </remarks>
    public LoadoutSnapshot ToSnapshot() => new(_gear);

    /// <summary>Reads a persisted loadout, refusing a row no loadout could be in.</summary>
    /// <param name="snapshot">The persisted row.</param>
    /// <returns>The loadout, or a failure listing every validation the row failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    internal static Result<Loadout> Rehydrate(LoadoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var faults = new List<string>();

        if (snapshot.Gear is null)
        {
            faults.Add(
                nameof(LoadoutSnapshot.Gear) + " is null. An absent loadout is not a naked hero: " +
                "read as empty, it silently unequips everything the player was wearing on the first " +
                "load of a row that merely failed to write it.");

            return Failure(faults);
        }

        var gear = new Dictionary<GearSlot, GearInstanceId>(snapshot.Gear.Count);
        var worn = new HashSet<string>(snapshot.Gear.Count, StringComparer.Ordinal);

        foreach (var (slot, item) in snapshot.Gear)
        {
            if (!Enum.IsDefined(slot))
            {
                faults.Add(
                    nameof(LoadoutSnapshot.Gear) + " names slot " + Text((int)slot) + ", which is not one " +
                    "of the six a hero wears. GearSlot has no zero member on purpose, so this is " +
                    "what an uninitialised column reads as.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Value))
            {
                faults.Add(
                    nameof(LoadoutSnapshot.Gear) + "[" + slot + "] holds a blank instance id, which " +
                    "names no item. An EMPTY slot is an absent key; a slot present with no id is a " +
                    "row that says something is worn and cannot say what.");
                continue;
            }

            if (!worn.Add(item.Value))
            {
                faults.Add(
                    nameof(LoadoutSnapshot.Gear) + " wears '" + item.Value + "' in more than one " +
                    "slot. One rolled item is one object: worn twice, it would contribute its stats " +
                    "twice and be salvaged from under itself.");
                continue;
            }

            gear[slot] = item;
        }

        return faults.Count > 0
            ? Failure(faults)
            : Result<Loadout>.Success(
                gear.Count == 0
                    ? Empty
                    : new Loadout(new ReadOnlyDictionary<GearSlot, GearInstanceId>(gear)));
    }

    private static Result<Loadout> Failure(List<string> faults) =>
        Result<Loadout>.Failure(
            "This LoadoutSnapshot is not a state the game can be in (" + Text(faults.Count) +
            " problem(s)): " + string.Join(" | ", faults));

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>, so messages read the same on every host.</summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private int CountOf(GearInstanceId item)
    {
        var count = 0;

        foreach (var equipped in _gear.Values)
        {
            if (equipped.Equals(item))
            {
                count++;
            }
        }

        return count;
    }

    private static void RequireSlot(GearSlot slot)
    {
        if (Enum.IsDefined(slot))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(slot),
            slot,
            "A hero wears six slots and this is not one of them. GearSlot deliberately has no zero " +
            "member, so an uninitialised value arrives here rather than reading as WEAPON.");
    }

    private static void RequireItem(GearInstanceId item)
    {
        if (!string.IsNullOrWhiteSpace(item.Value))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(item),
            item,
            "A slot holds an item's identity, so the identity is never blank. " +
            "default(GearInstanceId) runs no constructor and so was never validated by one.");
    }
}
