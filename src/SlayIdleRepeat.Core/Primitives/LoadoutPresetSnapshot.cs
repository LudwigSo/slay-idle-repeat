namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The persisted shape of one saved loadout preset.</summary>
/// <param name="Slot">Which slot it occupies, counted from 1. Unique across a player's presets.</param>
/// <param name="Name">The player's own name for it. Never blank; never interpreted.</param>
/// <param name="Loadout">What it restores.</param>
/// <remarks>
/// <para>
/// It carries a <see cref="LoadoutSnapshot"/> rather than its own copy of the gear map, and that is
/// the load-bearing choice: when a later milestone adds pets, a mount, talents or a PvP perk set to a
/// loadout, they land on that one record and every preset gains them in the same edit. A preset with
/// its own parallel gear member would need the same field added twice, and the two would drift.
/// </para>
/// <para>
/// ⚠️ <b>A preset is a wish, not a claim of ownership.</b> Unlike the live loadout, it is deliberately
/// not validated against the player's stock: `12` §66 keeps presets beyond the free allowance
/// readable rather than deleting them, and an item can be salvaged or merged long after a preset
/// named it. Applying a preset therefore restores what the player still owns and silently leaves the
/// rest — refusing the whole preset because one ring is gone would be the worse answer, and deleting
/// the preset would destroy the only record of what the player had built.
/// </para>
/// </remarks>
public sealed record LoadoutPresetSnapshot(int Slot, string Name, LoadoutSnapshot Loadout);
