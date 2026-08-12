namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>Which cap a <see cref="EffectOp.STAT_CAP_OVERRIDE"/> raises or redirects.</summary>
/// <remarks>
/// <para>
/// ⚠️ <b>M2-01 shipped one member, because `18` authored exactly one token.</b> M2-03 adds two under
/// `18` §10's extension procedure, because with one token the op cannot express either half of its
/// own §2.1 description — <em>"raise or redirect a stat cap (<c>Perfect Strike</c> keystone)"</em>:
/// </para>
/// <list type="number">
/// <item><b>The one authored token is not a stat cap at all.</b> <see cref="HEAL_CEILING"/> comes
/// from §7.6's <c>Avatar of War</c> — <em>"you can no longer be healed above 80% Max HP"</em> (`09`
/// §4) — which is a ceiling on <c>Heal()</c> (`05` §4.3), not one of `05` §1's six stat ceilings.
/// So the op as authored could raise no cap and redirect nothing.</item>
/// <item><b>The only stated redirect appears in no DSL example.</b> `09` §4's <c>Perfect Strike</c>
/// is <em>"Crit Chance above the 75% cap converts to Crit Damage at 1:4"</em>, and `18` writes no
/// JSON for it — so both the token and the destination key were missing.</item>
/// </list>
/// <para>
/// 🔒 <b>Keys, never values (steering S6).</b> Neither new member carries a number. The ceiling
/// <see cref="STAT_MAX"/> installs and the ratio <see cref="REDIRECT_EXCESS"/> multiplies by are
/// both the effect's own authored <c>value</c>, so <c>Perfect Strike</c>'s "1:4" stays in the talent
/// catalogue M3 authors — nothing here decides which way round that ratio reads.
/// </para>
/// <para>
/// 🔒 Wire values, as <see cref="EffectOp"/>: append, never renumber, never reuse; no <c>0</c>
/// member.
/// </para>
/// </remarks>
public enum StatCapKind
{
    /// <summary>
    /// The ceiling on healing, expressed as a fraction of Max HP (`18` §7.6). ⚠️ <b>Not one of `05`
    /// §1's six stat caps</b> — it bounds <c>Heal()</c> (`05` §4.3), so `18` §8 step 9's cap table
    /// is left untouched by it and M2-09 reads it instead.
    /// </summary>
    HEAL_CEILING = 1,

    /// <summary>
    /// §2.1's <em>"raise"</em>: replaces `05` §1's declared ceiling on the named <c>stat</c> with
    /// the effect's <c>value</c>, at `18` §8 step 9.
    /// </summary>
    STAT_MAX = 2,

    /// <summary>
    /// §2.1's <em>"redirect"</em>: the amount by which the named <c>stat</c> overshot its ceiling is
    /// multiplied by the effect's <c>value</c> and added to <c>toStat</c>, at `18` §8 step 9.
    /// <c>Perfect Strike</c>'s <em>"crit above the 75% cap converts to crit damage"</em>.
    /// </summary>
    REDIRECT_EXCESS = 3,
}
