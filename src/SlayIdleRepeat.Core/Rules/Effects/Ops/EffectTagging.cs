using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// 🔒 R12 — the two tag vocabularies of `18`, read apart: an effect's own author labels (`18` §1's
/// <c>tags</c>, in which <c>drawback</c> is reserved) and a status's tag group (`18` §2.3's
/// <c>statusTag</c>).
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this is a type and not two <c>string</c> comparisons.</b> `05` §4.1's ward bypass list
/// (b) exempts <em>"self-inflicted costs (cursed-perk drawbacks such as <c>CP_BLOOD_PRICE</c> /
/// <c>CP_TIMEBOUND</c>)"</em> and names no marker; `18` §7.5 authors <c>CP_BLOOD_PRICE</c> with
/// <c>"tags": ["drawback"]</c>, which defines the marker by example. `18` §2.3 separately gives
/// <c>REMOVE_STATUS</c> <em>"a status or a tag group"</em>. Both are lower-snake strings. Left as
/// strings, one <c>REMOVE_STATUS</c> authored with <c>"drawback"</c> would either clear the marker
/// or — worse — a future ward-bypass check would accept a status label, and the failure would be a
/// perk drawback silently absorbed by a shield, which is the exact outcome `05` §4.1 forbids.
/// <see cref="AuthorTag"/> and <see cref="StatusTag"/> cannot be passed to each other's methods.
/// </para>
/// <para>
/// ⚠️ <b>This class reads; it decides nothing about wards.</b> `05` §4.1 is M2-09's, and the bypass
/// itself happens there — the op passes <see cref="IsSelfInflictedCost"/> along
/// (<see cref="IAttackPipeline.DealMaxHpPctDamage"/>) so that the tag is read in exactly one place.
/// </para>
/// </remarks>
internal static class EffectTagging
{
    /// <summary>The effect's `18` §1 author labels, typed.</summary>
    /// <remarks>
    /// ⚠️ Duplicates are not rejected here — the schema's <c>uniqueItems</c> is what states that
    /// rule, and restating it would be a second mechanism that can disagree with the first.
    /// </remarks>
    internal static IEnumerable<AuthorTag> AuthorTags(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        foreach (var tag in effect.Tags)
        {
            yield return new AuthorTag(tag);
        }
    }

    /// <summary>Whether the effect carries `18` §7.5's reserved <c>drawback</c> label.</summary>
    /// <remarks>
    /// ⚠️ An indexed loop rather than <c>AuthorTags(effect).Any(…)</c>: this runs once per
    /// <c>DAMAGE_MAXHP_PCT</c> resolution, which is per actor per tick at `05` §3's 20 Hz inside a
    /// &lt; 5 ms budget. The iterator plus the LINQ enumerator are two allocations for a scan of a
    /// list that is almost always one element long. <c>foreach</c> over the
    /// <see cref="IReadOnlyList{T}"/> interface would still box an enumerator.
    /// </remarks>
    internal static bool IsDrawback(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var tags = effect.Tags;
        for (var i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], AuthorTag.Drawback.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 🔒 `05` §4.1 bypass class <b>(b)</b> — whether this effect's damage is a self-inflicted cost
    /// and must reach HP through the ward pool untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two conditions, both required, and the second is what stops the tag being a blanket
    /// ward-bypass switch: the damage must be <b>self</b>-inflicted. `05` §4.1's own examples are
    /// <c>CP_BLOOD_PRICE</c> (<c>DAMAGE_MAXHP_PCT</c> on <c>target: SELF</c>) and
    /// <c>CP_TIMEBOUND</c>; a <c>drawback</c>-tagged effect pointed at an enemy is an author label
    /// on an offensive clause, not a cost, and letting it skip the enemy's wards would hand every
    /// cursed perk armour penetration nobody wrote.
    /// </para>
    /// <para>
    /// 🔴 <b>M2-02 has ruled, and the answer here changed.</b> An absent <c>target</c> is
    /// <c>SELF</c> (<see cref="EffectDefaults"/> ruling 2, read from `18` §2.4's <c>CLEAR_SUMMONS</c>
    /// row), so a <c>drawback</c>-tagged effect with no target is now read as a self-inflicted cost
    /// where M2-03 read it as not. That is the correct direction: `18` §7.5's <c>CP_BLOOD_PRICE</c> is
    /// the clause `05` §4.1's bypass list (b) was written for, and under the ruling a sibling drawback
    /// that omitted its target keeps the bypass instead of silently losing it to a ward. The second
    /// condition still does its job — a <c>drawback</c> tag pointed at an <em>enemy</em> is an author
    /// label on an offensive clause and gets no bypass.
    /// </para>
    /// </remarks>
    internal static bool IsSelfInflictedCost(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return IsDrawback(effect) && EffectDefaults.TargetOf(effect) == EffectTarget.SELF;
    }

    /// <summary>
    /// `18` §2.3 — the <b>status</b> tag group a <see cref="EffectOp.REMOVE_STATUS"/> clears, or
    /// <c>null</c> where it names a single <c>statusId</c> instead.
    /// </summary>
    internal static StatusTag? StatusTagOf(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.StatusTag;
    }
}
