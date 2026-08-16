namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The six equipment slots a hero wears, one item each.</summary>
/// <remarks>
/// <para>
/// Declared here rather than under <c>Model/Gear/</c> because the wire needs it: a command carries a
/// slot, and <c>Commands/</c> may name <c>Primitives</c> and may not name <c>Model</c>. A public enum
/// under <c>Core/Model/</c> would additionally fail the accessibility-boundary rule on its
/// compiler-generated <c>value__</c> field.
/// </para>
/// <para>
/// <b>Only <see cref="WEAPON"/> is authored verbatim</b> — it is the slot the example gear instance
/// spells. The other five are the slot-table row names uppercased, which is an identifier derivation
/// from an authored convention rather than an authored vocabulary, and it is recorded here rather
/// than left silent. Note <see cref="ARMOR"/>: the American spelling is the design set's own.
/// </para>
/// <para>
/// The numeric values are wire values — an equip command, a gear row and an analytics record all
/// carry a slot. Append, never renumber, never reuse. There is deliberately no <c>0</c> member, so
/// <c>default(GearSlot)</c> cannot read as <see cref="WEAPON"/> and turn an uninitialised column into
/// a weapon.
/// </para>
/// </remarks>
public enum GearSlot
{
    /// <summary>Blade, Axe, Staff, Bow. Primary stat ATK. Visible on the hero.</summary>
    WEAPON = 1,

    /// <summary>Hood, Helm, Circlet, Mask. Primary stat DEF. Visible on the hero.</summary>
    HELMET = 2,

    /// <summary>Leathers, Plate, Robe, Scalemail. Primary stat Max HP. Visible on the hero.</summary>
    ARMOR = 3,

    /// <summary>Treads, Greaves, Slippers, Sandals. Primary stat Attack Speed.</summary>
    BOOTS = 4,

    /// <summary>Band, Signet, Loop, Seal. Primary stat Crit Chance.</summary>
    RING = 5,

    /// <summary>Pendant, Talisman, Charm, Idol. Primary stat Max HP.</summary>
    AMULET = 6,
}
