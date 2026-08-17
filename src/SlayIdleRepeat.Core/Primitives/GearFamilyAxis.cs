namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// The four family axes — the stat bias one family of every slot shares, and the identity of the
/// four SS sets.
/// </summary>
/// <remarks>
/// <para>
/// There are exactly four sets, one per axis, so an SS item's set is <em>determined by its family</em>
/// and needs no separate mapping table. That is why this enum exists and why no <c>setId</c> is
/// stored on a gear instance: the set is derived, and a value that is derived and also stored is two
/// answers to one question.
/// </para>
/// <para>
/// The member names are the authored tokens — <c>drops.json#/sets/*/familyAxis</c> spells all four —
/// so this is a transcription rather than an invention. The <em>set names</em> (Bloodmoon, Ironvow,
/// Fateweave, Stormcall) are display strings and live in the locale files; no <c>setId</c> vocabulary
/// is authored anywhere in the design set, which is why <c>drops.json</c> still carries
/// <c>id: null</c> on all four rows and why the bonus engine keys on the axis instead.
/// </para>
/// <para>
/// Wire values, appended never renumbered, and no <c>0</c> member.
/// </para>
/// </remarks>
public enum GearFamilyAxis
{
    /// <summary>Balanced, crit-leaning. The Bloodmoon set.</summary>
    BALANCED = 1,

    /// <summary>Heavy: ATK and Max HP, lower attack speed. The Ironvow set.</summary>
    HEAVY = 2,

    /// <summary>Caster: damage-over-time power and attack speed, low DEF. The Fateweave set.</summary>
    CASTER = 3,

    /// <summary>Agile: attack speed, dodge, penetration. The Stormcall set.</summary>
    AGILE = 4,
}
