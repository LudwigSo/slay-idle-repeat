namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>🔒 The eight <c>valueMode</c> values of `18` §2.2 — what an effect's <c>value</c> is a multiple of.</summary>
/// <remarks>
/// <para>
/// <see cref="ATK_MULT"/> is the documented default. `18` does not say what an <em>absent</em>
/// <c>valueMode</c> means for an op outside §2.2, so <see cref="EffectDefinition.ValueMode"/> is
/// left <c>null</c> rather than coerced — <c>game-data/README.md</c>: <em>"null means the design
/// docs do not authorise a value here"</em>.
/// </para>
/// <para>
/// ⚠️ <see cref="HEAL_AMOUNT"/> and <see cref="OVERHEAL_AMOUNT"/> <em>"exist only inside
/// <c>ON_HEAL</c> contexts (`05` §4.3)"</em>: <c>HEAL_AMOUNT</c> is the full amount actually healed,
/// <c>OVERHEAL_AMOUNT</c> the clipped excess. The schema states that restriction as a comment on
/// those two members and M2-06 enforces it at evaluation — it cannot be a schema rule, because the
/// <c>ON_HEAL</c> context can also be supplied by the wrapper an effect list sits in (a pet
/// <c>active</c> block carries no trigger of its own, `18` §7.7).
/// </para>
/// <para>
/// 🔒 Wire values, as <see cref="EffectOp"/>: append, never renumber, never reuse; no <c>0</c>
/// member.
/// </para>
/// </remarks>
public enum ValueMode
{
    /// <summary>A multiple of the source's ATK. The `18` §2.2 default.</summary>
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

    /// <summary>The full amount actually healed. <c>ON_HEAL</c> contexts only (`05` §4.3).</summary>
    HEAL_AMOUNT = 7,

    /// <summary>The clipped excess of a heal. <c>ON_HEAL</c> contexts only (`05` §4.3).</summary>
    OVERHEAL_AMOUNT = 8,
}
