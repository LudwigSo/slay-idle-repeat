namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// The twenty-four item families — four per <see cref="GearSlot"/>, and the whole base-item roster.
/// </summary>
/// <remarks>
/// <para>
/// Twenty-four families across six slots, each existing at five rarities, is the design set's own
/// arithmetic for its hundred and twenty distinct gear entries. The family is what fixes an item's
/// stat bias and — at <see cref="Rarity.SS"/> — its set, so it is a closed vocabulary rather than a
/// content id: adding a twenty-fifth is a code edit <em>and</em> a content edit, deliberately.
/// </para>
/// <para>
/// Declared in <c>Primitives/</c> for <see cref="GearSlot"/>'s reason: the Focus command carries a
/// family, and a command may name <c>Primitives</c> and may not name <c>Model</c>.
/// </para>
/// <para>
/// Ordered by slot and, within a slot, in the order the slot table lists them — so the four members
/// of one slot are contiguous and the four members of one family <em>axis</em> are four apart. The
/// axis is not derived from that arithmetic anywhere: it is authored per row in
/// <c>content/gear/gear.json</c>, because a layout coincidence is not a specification.
/// </para>
/// <para>
/// The numeric values are wire values. Append, never renumber, never reuse; there is no <c>0</c>
/// member, so <c>default(GearFamily)</c> cannot read as <see cref="BLADE"/>.
/// </para>
/// </remarks>
public enum GearFamily
{
    /// <summary>Weapon, balanced axis.</summary>
    BLADE = 1,

    /// <summary>Weapon, heavy axis.</summary>
    AXE = 2,

    /// <summary>Weapon, caster axis.</summary>
    STAFF = 3,

    /// <summary>Weapon, agile axis.</summary>
    BOW = 4,

    /// <summary>Helmet, balanced axis.</summary>
    HOOD = 5,

    /// <summary>Helmet, heavy axis.</summary>
    HELM = 6,

    /// <summary>Helmet, caster axis.</summary>
    CIRCLET = 7,

    /// <summary>Helmet, agile axis.</summary>
    MASK = 8,

    /// <summary>Armor, balanced axis.</summary>
    LEATHERS = 9,

    /// <summary>Armor, heavy axis.</summary>
    PLATE = 10,

    /// <summary>Armor, caster axis.</summary>
    ROBE = 11,

    /// <summary>Armor, agile axis.</summary>
    SCALEMAIL = 12,

    /// <summary>Boots, balanced axis.</summary>
    TREADS = 13,

    /// <summary>Boots, heavy axis.</summary>
    GREAVES = 14,

    /// <summary>Boots, caster axis.</summary>
    SLIPPERS = 15,

    /// <summary>Boots, agile axis.</summary>
    SANDALS = 16,

    /// <summary>Ring, balanced axis.</summary>
    BAND = 17,

    /// <summary>Ring, heavy axis.</summary>
    SIGNET = 18,

    /// <summary>Ring, caster axis.</summary>
    LOOP = 19,

    /// <summary>Ring, agile axis.</summary>
    SEAL = 20,

    /// <summary>Amulet, balanced axis.</summary>
    PENDANT = 21,

    /// <summary>Amulet, heavy axis.</summary>
    TALISMAN = 22,

    /// <summary>Amulet, caster axis.</summary>
    CHARM = 23,

    /// <summary>Amulet, agile axis.</summary>
    IDOL = 24,
}
