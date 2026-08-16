namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The persisted shape of a loadout: which gear instance sits in which slot.</summary>
/// <param name="Gear">
/// Slot → the instance equipped in it. A slot with nothing in it is an absent key, never a
/// <c>default(GearInstanceId)</c> row: an empty slot and a slot holding an item with no id are
/// different states, and only one of them is legal.
/// </param>
/// <remarks>
/// <para>
/// 🔒 <b>One member today, and the shape is the point.</b> `07` §4 calls a loadout "gear + pets +
/// mount + PvP perk set" and `09` §2.1 calls it "talents + gear + pets + mount"; pets (M4-07), mounts
/// (M4-08), talents (M4-06) and PvP (M12) are all outside this milestone, and none of them has a
/// type yet. This record is what lets each of those tasks <b>append a field</b> rather than change a
/// structure: the loadout is already a named record reached through one member on the player and one
/// on every preset, so a pets member lands here and nothing above it moves. Empty placeholder types
/// for the four absent domains are deliberately <em>not</em> declared — a plausible-looking hole is
/// worse than a visible one, and four of them would be four milestones' decisions taken by a task
/// that owns none of them.
/// </para>
/// <para>
/// A component of the player's row and of each preset's rather than a row of its own, so it carries
/// no <c>SchemaVersion</c> — the same argument <see cref="InventorySnapshot"/> records.
/// </para>
/// <para>
/// The map is keyed by <see cref="GearSlot"/> rather than being a six-element list, because "one item
/// per slot" is then the shape of the data rather than a rule stated over it, and because the
/// canonical writer orders a map by its key: two loadouts differing only in the order they were
/// assembled encode identically, which is what they are.
/// </para>
/// </remarks>
public sealed record LoadoutSnapshot(IReadOnlyDictionary<GearSlot, GearInstanceId> Gear);
