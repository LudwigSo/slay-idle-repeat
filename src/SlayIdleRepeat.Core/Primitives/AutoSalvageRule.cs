namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// One row of a player's auto-salvage filter: a band, and the enhancement level below which an item
/// of that band is broken down at run end.
/// </summary>
/// <remarks>
/// <para>
/// The shape is the design set's own example — <em>"salvage all C and B below +3"</em> — read
/// literally: a band paired with a level ceiling, one row per band the player wants swept. A band
/// with no row is never swept, which is what makes an empty filter mean "keep everything" rather
/// than needing a switch beside it.
/// </para>
/// <para>
/// <b>Exclusive, and it has to be.</b> The example says <em>below</em> +3, so an item sitting
/// exactly at the ceiling survives. A rule that swept its own ceiling would delete the item a player
/// had just enhanced to the level they chose as the keep-line.
/// </para>
/// <para>
/// It lives in <c>Primitives/</c> because it is persisted on the player and read by a rule, and
/// <c>Primitives</c> is the layer beneath both — the same placement <c>GearAffixRoll</c> has for the
/// same reason.
/// </para>
/// </remarks>
/// <param name="Rarity">The band this row sweeps.</param>
/// <param name="BelowEnhanceLevel">
/// The enhancement level an item of that band must be strictly below to be swept. Zero sweeps
/// nothing, which is a legal row and not a defect: it is how a player turns one band off without
/// losing the row's place in the filter.
/// </param>
public readonly record struct AutoSalvageRule(Rarity Rarity, int BelowEnhanceLevel);
