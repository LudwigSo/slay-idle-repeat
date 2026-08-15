namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>Which cap a <see cref="EffectOp.STAT_CAP_OVERRIDE"/> raises or redirects.</summary>
/// <remarks>
/// <para>
/// Only one token was authored originally, and it is not a stat cap at all: <see cref="HEAL_CEILING"/>
/// is a ceiling on <c>Heal()</c>, not one of the six stat ceilings. Two more members were added
/// later so the op could actually raise a cap or redirect one, as its own description promised.
/// </para>
/// <para>
/// Keys, never values: neither new member carries a number. The ceiling <see cref="STAT_MAX"/>
/// installs and the ratio <see cref="REDIRECT_EXCESS"/> multiplies by are both the effect's own
/// authored <c>value</c>.
/// </para>
/// <para>
/// Wire values, as <see cref="EffectOp"/>: append, never renumber, never reuse; no <c>0</c> member.
/// </para>
/// </remarks>
public enum StatCapKind
{
    /// <summary>
    /// The ceiling on healing, expressed as a fraction of Max HP. Not one of the six stat caps — it
    /// bounds <c>Heal()</c>, so the ordinary cap table is left untouched by it.
    /// </summary>
    HEAL_CEILING = 1,

    /// <summary>"Raise": replaces the declared ceiling on the named <c>stat</c> with the effect's <c>value</c>.</summary>
    STAT_MAX = 2,

    /// <summary>
    /// "Redirect": the amount by which the named <c>stat</c> overshot its ceiling is multiplied by
    /// the effect's <c>value</c> and added to <c>toStat</c> — e.g. "crit above the cap converts to
    /// crit damage".
    /// </summary>
    REDIRECT_EXCESS = 3,
}
