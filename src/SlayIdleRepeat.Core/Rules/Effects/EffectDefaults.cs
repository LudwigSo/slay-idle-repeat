using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// 🔒 <b>The two rulings M2-01 handed to M2-02: what an effect with no <c>trigger</c> and no
/// <c>target</c> means.</b> Stated once, here, so that the resolver, the ops and the tagging layer
/// cannot each answer differently.
/// </summary>
/// <remarks>
/// <para>
/// `18` §1 names an eight-part shape and `18` writes effects that omit two of the eight. M2-01
/// declared <see cref="EffectDefinition.Trigger"/> and <see cref="EffectDefinition.Target"/>
/// nullable, manufactured no default (steering S6) and recorded the hole as errata. These are the
/// rulings.
/// </para>
///
/// <para>
/// ══════ 🔒 <b>RULING 1 — an absent <c>trigger</c> is <c>ALWAYS</c>.</b> ══════
/// </para>
/// <para>
/// Read from <b>`18` §1.1</b>, which partitions every effect in the language into two kinds and
/// leaves no third: <em>"<c>valueScale</c> is re-evaluated exactly when conditions are (§4): at every
/// resolution pass <b>for <c>ALWAYS</c> effects</b>, at fire time <b>for triggered ones</b>."</em> An
/// effect with no <c>trigger</c> has no kind to fire on, so it cannot be the second; the partition is
/// exhaustive, so it is the first.
/// </para>
/// <para>
/// Corroborated, rather than inferred, by the pair `18` writes both ways. §1's canonical example and
/// §7.1's <c>PK_SHARP_EDGE</c> author a passive <c>STAT_ADD_PCT</c> <b>with</b>
/// <c>"trigger":{"kind":"ALWAYS"}</c>; §9.1's <c>CP_GLASS_HEART</c> authors passive stat ops
/// <b>without</b> any trigger at all. They are the same kind of effect and §3's <c>ALWAYS</c> row —
/// <em>"passive, always active"</em>, no parameters — is the only kind under which both do what their
/// prose says.
/// </para>
/// <para>
/// ⚠️ <b>SCOPE, and it is the half that keeps the ruling honest.</b> This governs <b>build
/// effects</b> — the ones `18` §8 step 1 collects from its ten sources. It does <b>not</b> govern
/// §7.7's <c>PET_STORMFANG</c> <c>active</c> block, whose effects also carry no trigger and are
/// emphatically not passive: their cadence is the wrapper's <c>"cooldown": 12.0</c>. The document
/// separates them itself — step 1 collects <em>"pet auras"</em>, which is §7.7's sibling <c>aura</c>
/// block — so the exception is the DSL's own structure and not a carve-out invented here. That is why
/// <c>TriggerInstance</c> still <b>refuses</b> an effect with no trigger rather than defaulting: an
/// effect reaching the firing layer untriggered has not come through step 1, and the one thing it can
/// be is a pet active whose owner (M4-07) has not wired its cooldown.
/// </para>
///
/// <para>
/// ══════ 🔒 <b>RULING 2 — an absent <c>target</c> is <c>SELF</c>.</b> ══════
/// </para>
/// <para>
/// Read from <b>`18` §2.4's <c>CLEAR_SUMMONS</c> row</b> — <em>"despawn all living summons owned by
/// the target <b>(default <c>SELF</c>)</b>"</em>. That is the only place in the whole document where
/// `18` states a default target at all, and it states <c>SELF</c>. Taking it as the general rule
/// costs nothing and makes that row an instance of it; taking it as a lone exception leaves the other
/// nine omitting effects with no rule and one op with a special case.
/// </para>
/// <para>
/// Corroborated by every authored effect in `18` that omits <c>target</c>, all of which mean the
/// holder and none of which mean an enemy: §7.4's <c>SURVIVE_LETHAL</c> (<em>"survive an
/// otherwise-fatal hit"</em> — the survivor is the holder, and its sibling <c>SHIELD</c> in the same
/// array writes <c>"target":"SELF"</c> out); §7.5's <c>CP_BLOOD_PRICE</c> <c>+45% ATK</c> clause (the
/// holder's ATK, and its sibling drawback again writes <c>SELF</c> out); §7.6's <em>Avatar of War</em>
/// (<c>×1.20 ATK</c> and a heal ceiling, both the holder's per `09` §4); and §9.1's
/// <c>CP_GLASS_HEART</c> (<em>"×2 all stats, Max HP set to 1"</em> — §9.1's whole interaction list is
/// about the holder's own survival).
/// </para>
/// <para>
/// 🔒 <b>The counter-argument, and why it loses.</b> <c>OpTargets</c> declined to default because
/// <em>"<c>SELF</c> turns an offensive clause into self-harm"</em>. That is a real risk and it is
/// hypothetical: no authored effect in `18` omits <c>target</c> while meaning an enemy, and the one
/// row that states a default states this one. Against it stands a concrete cost — under "refuse", `18`
/// §7.4, §7.5, §7.6 and §9.1 are all unresolvable as written, which means four of the document's own
/// worked examples do not run. A rule that makes the spec's examples fail is the weaker reading.
/// </para>
/// <para>
/// 🔴 <b>THE RISK THIS LEAVES OPEN, and it is genuinely open — do not read the paragraph above as
/// though something else caught it.</b> The right home for an authoring guard is
/// <c>game-data/schema/effect.schema.json</c>, which partitions the 43 ops into key shapes and is
/// `18` §10's stated place for one. <b>It does not carry this guard today.</b> No <c>required</c>
/// array in that file names <c>target</c>, so <c>{"op":"DAMAGE","value":0.8}</c> validates, and
/// under this ruling it now resolves against the holder instead of being refused. An earlier draft
/// of this remark claimed the schema was the protection; that claim was false and is corrected here.
/// </para>
/// <para>
/// 🔒 <b>Why M2-02 did not simply add it.</b> The <c>required</c> array that would have to change
/// (<c>"18 §2.2 — DAMAGE / DAMAGE_TRUE / DAMAGE_MAXHP_PCT / HEAL / HEAL_LEECH / REFLECT"</c>) covers
/// <c>HEAL</c> and <c>HEAL_LEECH</c> as well, and those legitimately mean the holder when untargeted
/// — so the guard needs the group split before it can be stated, which is a change to `18` §10's op
/// partition rather than a one-line edit. And the schema is shared content that a concurrently
/// running task is authoring against; changing its validity rules mid-wave is how one branch breaks
/// another's data at merge (steering S12). Recorded as the follow-up, in the file the ruling lives
/// in, rather than done badly here.
/// </para>
/// <para>
/// ⚠️ <b>Consequence, stated so it is a decision rather than a discovery.</b>
/// <c>EffectTagging.IsSelfInflictedCost</c> — `05` §4.1's ward-bypass list (b) — now reads a
/// <c>drawback</c>-tagged effect with no <c>target</c> as self-inflicted, where before it read as
/// not. That is the correct direction: §7.5's <c>CP_BLOOD_PRICE</c> is the clause the bypass list was
/// written for, and under this ruling a sibling drawback that omitted its target keeps the bypass
/// instead of silently losing it to a ward.
/// </para>
/// </remarks>
internal static class EffectDefaults
{
    /// <summary>
    /// 🔒 Ruling 1 — the trigger an effect with none resolves under: `18` §3's <c>ALWAYS</c>.
    /// </summary>
    /// <remarks>
    /// A single shared instance. <see cref="EffectTrigger"/> is a record with no parameters set, and
    /// `18` §3's <c>ALWAYS</c> row admits none — <em>"passive, always active"</em>, parameters
    /// <em>"—"</em>.
    /// </remarks>
    internal static EffectTrigger Always { get; } = new() { Kind = TriggerKind.ALWAYS };

    // ⚠️ TriggerOf and IsAlwaysActive have no PRODUCTION caller yet, and that is stated rather than
    //    left to be discovered. They are ruling 1 in the two shapes its consumers need: M2-08's tick
    //    loop registers each collected effect with M2-04's TriggerRegistry, which needs a non-null
    //    EffectTrigger (TriggerOf), and `18` §1.1's "re-evaluated at every resolution pass for
    //    ALWAYS effects" needs the predicate (IsAlwaysActive). Declaring both here is what stops
    //    each of those tasks answering the ruling for itself; the alternative — waiting — is how one
    //    ruling becomes two implementations.

    /// <summary>🔒 Ruling 2 — the target an effect with none resolves against: `18` §5's <c>SELF</c>.</summary>
    internal const EffectTarget AbsentTarget = EffectTarget.SELF;

    /// <summary>
    /// The effect's trigger, or `18` §3's <c>ALWAYS</c> where it authors none (ruling 1).
    /// </summary>
    internal static EffectTrigger TriggerOf(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.Trigger ?? Always;
    }

    /// <summary>The effect's trigger kind, or <see cref="TriggerKind.ALWAYS"/> (ruling 1).</summary>
    internal static TriggerKind TriggerKindOf(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.Trigger?.Kind ?? TriggerKind.ALWAYS;
    }

    /// <summary>
    /// 🔒 True when the effect is a `18` §3 <c>ALWAYS</c> passive — the effects `18` §1.1 says are
    /// re-evaluated <em>"at every resolution pass"</em>, and therefore the ones a `18` §8 aggregation
    /// pass reads.
    /// </summary>
    internal static bool IsAlwaysActive(EffectDefinition effect) =>
        TriggerKindOf(effect) == TriggerKind.ALWAYS;

    /// <summary>The effect's `18` §5 target, or <see cref="EffectTarget.SELF"/> (ruling 2).</summary>
    internal static EffectTarget TargetOf(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.Target ?? AbsentTarget;
    }
}
