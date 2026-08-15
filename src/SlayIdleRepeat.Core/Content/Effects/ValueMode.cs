namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The eight <c>valueMode</c> values — what an effect's <c>value</c> is a multiple of.</summary>
/// <remarks>
/// <para>
/// <see cref="ATK_MULT"/> is the documented default. Nothing says what an absent <c>valueMode</c>
/// means for an op outside damage/healing, so <see cref="EffectDefinition.ValueMode"/> is left
/// <c>null</c> rather than coerced.
/// </para>
/// <para>
/// <see cref="HEAL_AMOUNT"/> and <see cref="OVERHEAL_AMOUNT"/> exist only inside <c>ON_HEAL</c>
/// contexts: <c>HEAL_AMOUNT</c> is the full amount actually healed, <c>OVERHEAL_AMOUNT</c> the
/// clipped excess. This cannot be a schema rule, because the <c>ON_HEAL</c> context can also be
/// supplied by the wrapper an effect list sits in.
/// </para>
/// <para>
/// Wire values, as <see cref="EffectOp"/>: append, never renumber, never reuse; no <c>0</c> member.
/// </para>
/// </remarks>
public enum ValueMode
{
    /// <summary>A multiple of the source's ATK. The default.</summary>
    ATK_MULT = 1,

    /// <summary>An absolute amount.</summary>
    FLAT = 2,

    /// <summary>A fraction of the source's Max HP.</summary>
    SELF_MAXHP_PCT = 3,

    /// <summary>A fraction of the target's Max HP.</summary>
    TARGET_MAXHP_PCT = 4,

    /// <summary>A fraction of the target's missing HP.</summary>
    TARGET_MISSING_HP_PCT = 5,

    /// <summary>A fraction of the damage just dealt.</summary>
    DAMAGE_DEALT_PCT = 6,

    /// <summary>The full amount actually healed. <c>ON_HEAL</c> contexts only.</summary>
    HEAL_AMOUNT = 7,

    /// <summary>The clipped excess of a heal. <c>ON_HEAL</c> contexts only.</summary>
    OVERHEAL_AMOUNT = 8,
}
