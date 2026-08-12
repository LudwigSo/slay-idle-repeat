using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// One stat's change, produced by a `18` §8 step 6 <c>STAT_CONVERT</c>.
/// </summary>
/// <param name="Stat">The stat the conversion writes to.</param>
/// <param name="Amount">
/// The amount added to it. Signed: <c>PK_TURTLE</c>'s "convert X% of DEF into ATK" is two deltas,
/// one negative and one positive, and expressing it as a pair rather than as a mutation is what lets
/// `18` §8's <em>"reads post-step-5 values"</em> be enforced by the pipeline rather than trusted.
/// </param>
internal readonly record struct StatDelta(StatId Stat, double Amount);

// 🔒 `IEffectConditionGate` (`18` §8 step 2) and `IEffectValueReader` (`18` §1.1 / §2.2) WERE
//    declared here and were MOVED to Rules/Effects/EffectResolutionSeams.cs by M2-02.
//
//    Both are `18` concerns, which `30` §11.4 assigns to Rules/Effects/, and R17 makes the move
//    mandatory rather than tidy: the intra-Rules layering is Rules.Combat -> Rules.Stats ->
//    Rules.Effects, so Rules.Effects is the BOTTOM and cannot be the layer borrowing an abstraction
//    from Rules.Stats. M2-07 declared them here only because reaching into a sibling task's
//    directory mid-wave was the larger risk, and recorded that they did not belong.
//
//    ⚠️ IStatOpBehaviour below did NOT move, and could not. See its remarks.

/// <summary>
/// 🔒 `18` §8 <b>steps 6 and 9</b> — <c>STAT_CONVERT</c>'s arithmetic and
/// <c>STAT_CAP_OVERRIDE</c>'s cap table. The seam <b>M2-03</b> implements.
/// </summary>
/// <remarks>
/// <para>
/// Both are op <em>behaviour</em>, which `18` §10 and the milestone plan put with the other 43 of
/// `18` §2's 44 ops (43 when this was written; `18` §10.1's E6 added the forty-fourth),
/// not with the aggregation order. What the pipeline keeps is the part that is order and rounding:
/// it hands the conversions the frozen post-step-5 block (so <em>"reads post-step-5 values"</em>
/// cannot be got wrong by an implementation that mutates as it goes), applies the returned deltas
/// itself, and rounds once at the end of the step.
/// </para>
/// <para>
/// ⚠️ <b>Neither op is fully authored today, and that is why the default throws rather than
/// no-ops.</b> `18` §2.1 gives <c>STAT_CONVERT</c> as <em>"convert a percentage of stat A into stat
/// B (<c>PK_TURTLE</c>, <c>PK_JUGGERNAUT</c>)"</em> but the effect shape carries one <c>stat</c>
/// key, so which stat is the source and which the destination is unwritten; `18` §2.1 gives
/// <c>STAT_CAP_OVERRIDE</c> as <em>"raise or redirect a stat cap"</em> with exactly one authored
/// <c>capKind</c> (<c>HEAL_CEILING</c>, `18` §7.6) that is not one of `05` §1's six caps, and the
/// only stated redirect — `09`'s <em>Perfect Strike</em>, "crit chance above the 75% cap converts to
/// crit damage at 1:4" — appears in no DSL example at all. Filling either in would be inventing a
/// rule, so the pipeline refuses effects it cannot resolve instead.
/// </para>
/// <para>
/// 🔴 <b>M2-02 was assigned this interface's relocation to <c>Rules/Effects/</c> and found it
/// impossible. Recorded here because the finding, not the move, is the durable result.</b> Its two
/// siblings moved: their whole signature is <see cref="EffectDefinition"/>, which is
/// <c>Content</c> and below both layers. This one's is not. <see cref="Convert"/> takes an
/// <see cref="ActorStats"/>, <see cref="OverrideCaps"/> takes and returns a <see cref="StatCaps"/>,
/// and both convert-shaped members return <see cref="StatDelta"/> — three <c>Rules.Stats</c> types.
/// Moving the interface down to <c>Rules.Effects</c> would therefore make the <b>bottom</b> layer
/// name the one above it, which is the precise edge
/// <c>IntraRulesLayeringRuleTests.Rules_Effects_is_the_bottom_of_the_intra_Rules_layering</c> forbids.
/// R17 is what was cited to require the move and is what blocks it: the two are the same rule read on
/// the two different signatures.
/// </para>
/// <para>
/// 🔒 <b>Two options WOULD close it, and neither is free. Recorded so the next attempt starts from
/// here rather than rediscovering the wall.</b>
/// </para>
/// <list type="number">
///   <item>
///   <b>A read-only view seam, which is the pattern this codebase already uses one directory over.</b>
///   <c>Rules/Effects/Ops/EffectOpSeams.cs</c> declares <c>IResolvedStatReader</c> for precisely this
///   problem, with precisely this note: <em>"R17 forbids <c>Rules.Effects</c> naming
///   <c>Rules.Stats</c>, so the op reads through this seam rather than through M2-07's
///   <c>ActorStats</c> directly."</em> The same move works here — <see cref="Convert"/> could take a
///   stat-block <em>view</em> and the cap members could take and return cap <em>views</em>, all named
///   in <c>Content.Effects.StatId</c> — and then the whole interface and
///   <see cref="StatOpBehaviour"/> move to <c>Rules/Effects/Ops/</c>. M2-02 chose against it on cost:
///   it means three new view interfaces plus their implementations, in a task that owns `18` §8 steps
///   1-2, to relocate an interface whose only consumer is <see cref="StatAggregation"/> — which sits
///   on the legal side of R17 already. That is a judgement about scope, not an impossibility, and a
///   later task with reason to touch this seam should reconsider it.
///   </item>
///   <item>
///   <b>Moving the vocabulary.</b> `05` §1's stat block and cap table would sit at or below
///   <c>Rules.Effects</c>. That is a decision about where `05` §1 lives rather than where `18` §8
///   does, and `30` §11.4 puts it in <c>Rules/Stats/</c> by name (<em>"Stats/ — 05 §1.1, 29"</em>) —
///   a locked-section change, not a mechanical edit.
///   </item>
/// </list>
/// <para>
/// ⚠️ <b>And the wall is smaller than it first looks.</b> Of the three types cited above,
/// <see cref="StatDelta"/> is <em>not</em> a blocker — it is <c>(StatId, double)</c>, and
/// <see cref="StatId"/> is <c>Content</c>, below both layers — so it could move today at zero cost.
/// <see cref="HealCeilingFraction"/> is not a blocker either: its whole signature is
/// <see cref="EffectDefinition"/> plus <see cref="IEffectValueReader"/>, and by this interface's own
/// remarks it is <em>"not a `18` §8 step"</em> at all. Only <see cref="Convert"/>,
/// <see cref="OverrideCaps"/> and <see cref="RedirectCappedExcess"/> genuinely name
/// <see cref="ActorStats"/> or <see cref="StatCaps"/>.
/// </para>
/// <para>
/// So the consequence M2-03 recorded stands for now and is not a defect:
/// <see cref="StatOpBehaviour"/>'s plumbing is here while its arithmetic is with the other 41 ops in
/// <c>Rules/Effects/Ops/StatOps.cs</c>. The <em>other</em> consequence M2-03 recorded — a second
/// statement of `05` §1.1's 4-dp rule in <c>OpRounding</c>, forced by the same layering — <b>is
/// closed</b>, by <c>Primitives.DeterminismRounding</c>: <c>Primitives</c> is beneath every layer that
/// rounds, so it is reachable from both sides of R17 where <c>Rules.Stats</c> was not.
/// </para>
/// </remarks>
internal interface IStatOpBehaviour
{
    /// <summary>
    /// `18` §8 step 6 — the deltas the <c>STAT_CONVERT</c> effects produce, applied by the caller in
    /// the order returned.
    /// </summary>
    /// <param name="conversions">The <c>STAT_CONVERT</c> effects, already in effect-id order.</param>
    /// <param name="postAdditive">
    /// The stat block as it stood after step 5. 🔒 Frozen: `18` §8 step 6 reads post-step-5 values,
    /// so a conversion never sees an earlier conversion's output.
    /// </param>
    /// <param name="values">
    /// 🔒 The <em>same</em> value reader steps 4, 5, 7 and 8 use. Handed in rather than left for the
    /// implementation to obtain: a conversion's percentage is an effect <c>value</c> like any other,
    /// and `18` §1.1's <c>valueScale</c> applies to it, so an implementation that built its own
    /// reader would give the aggregation two ways to value an effect with nothing making them agree.
    /// </param>
    IReadOnlyList<StatDelta> Convert(
        IReadOnlyList<EffectDefinition> conversions, ActorStats postAdditive, IEffectValueReader values);

    /// <summary>
    /// `18` §8 step 9 — the cap table after the <c>STAT_CAP_OVERRIDE</c> effects have raised or
    /// redirected it.
    /// </summary>
    /// <param name="overrides">The <c>STAT_CAP_OVERRIDE</c> effects, already in effect-id order.</param>
    /// <param name="declared">The caps as `05` §1 declares them.</param>
    /// <param name="values">The same value reader, for the same reason as <see cref="Convert"/>.</param>
    StatCaps OverrideCaps(
        IReadOnlyList<EffectDefinition> overrides, StatCaps declared, IEffectValueReader values);

    /// <summary>
    /// 🔒 `18` §8 step 9's <b>other half</b> — the deltas a <c>REDIRECT_EXCESS</c>
    /// <c>STAT_CAP_OVERRIDE</c> produces once the caps have landed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added by M2-03; M2-07 shipped this interface with two members.</b> `18` §2.1 gives
    /// <c>STAT_CAP_OVERRIDE</c> two jobs — <em>"raise <b>or redirect</b> a stat cap"</em> — and only
    /// the raise is a change to a cap table. `09` §4's <em>Perfect Strike</em>, <em>"crit chance
    /// above the 75% cap converts to crit damage at 1:4"</em>, moves value from one stat to another,
    /// which <see cref="OverrideCaps"/>'s <see cref="StatCaps"/> return cannot express at all. The
    /// alternative to this member was leaving the only authored redirect in the game permanently
    /// unimplementable.
    /// </para>
    /// <para>
    /// 🔒 <b>Frozen, on step 6's discipline.</b> <paramref name="preCap"/> is the block as it stood
    /// <em>before</em> the caps were applied — which is the only place the overshoot still exists —
    /// and it is handed in frozen so a redirect reads how far <em>its own</em> stat overshot and
    /// never another redirect's output.
    /// </para>
    /// </remarks>
    /// <param name="overrides">The <c>STAT_CAP_OVERRIDE</c> effects, already in effect-id order.</param>
    /// <param name="preCap">The stat block as it stood after step 8, before any cap was applied.</param>
    /// <param name="effective">The cap table <see cref="OverrideCaps"/> returned.</param>
    /// <param name="values">The same value reader, for the same reason as <see cref="Convert"/>.</param>
    IReadOnlyList<StatDelta> RedirectCappedExcess(
        IReadOnlyList<EffectDefinition> overrides,
        ActorStats preCap,
        StatCaps effective,
        IEffectValueReader values);

    /// <summary>
    /// 🔒 `18` §7.6's <c>HEAL_CEILING</c> — the fraction of Max HP above which the actor cannot be
    /// healed (<em>Avatar of War</em>) — or <c>null</c> where the build authors none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Not a `18` §8 step, and on this interface anyway.</b> It bounds <c>Heal()</c> (`05`
    /// §4.3), which is M2-09's, so it belongs to no step of the aggregation — but it is the
    /// <b>third</b> of <c>STAT_CAP_OVERRIDE</c>'s three <c>capKind</c>s, and this interface is what
    /// owns that op's behaviour. Left off, one kind in three would sit outside the swap point that
    /// governs the other two, and M2-09 would have to name a concrete <c>Rules.Stats</c> class.
    /// </para>
    /// <para>
    /// 🔒 <b>The lowest ceiling wins.</b> `18` §6's stacking modes govern repeat applications of one
    /// effect and say nothing about two different effects both bounding healing; the minimum is the
    /// only reading under which a second restriction cannot loosen the first, which is what "you can
    /// no longer be healed above" means. Recorded as a ruling — no document states it, because no
    /// second <c>HEAL_CEILING</c> is authored.
    /// </para>
    /// </remarks>
    /// <param name="overrides">The <c>STAT_CAP_OVERRIDE</c> effects, already in effect-id order.</param>
    /// <param name="values">The same value reader, for the same reason as <see cref="Convert"/>.</param>
    double? HealCeilingFraction(IReadOnlyList<EffectDefinition> overrides, IEffectValueReader values);
}

/// <summary>
/// The three seams of <see cref="StatAggregation"/>, together.
/// </summary>
/// <param name="Conditions">`18` §8 step 2 — M2-05.</param>
/// <param name="Values">`18` §1.1 / §2.2 — M2-06 and M2-03.</param>
/// <param name="Ops">`18` §8 steps 6 and 9 — M2-03.</param>
internal sealed record StatAggregationSeams(
    IEffectConditionGate Conditions,
    IEffectValueReader Values,
    IStatOpBehaviour Ops)
{
    /// <summary>
    /// 🔒 The seam set M2-07 shipped, with M2-03's <see cref="IStatOpBehaviour"/> in place: every
    /// part of `18` §8 that is <em>still</em> not authored refuses the input rather than guessing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a complete, usable implementation for the whole of `18` §8 that authored content can
    /// reach <em>today</em> — unconditional <c>STAT_ADD_FLAT</c>, <c>STAT_ADD_PCT</c>,
    /// <c>STAT_MULT</c> and <c>STAT_SET</c>, which is `18` §9.1's <c>CP_GLASS_HEART</c>, `18` §7.1's
    /// <c>PK_SHARP_EDGE</c>, `18` §7.6's <c>Avatar of War</c> multiplier and `05` §3.1's
    /// <c>SYS_ENRAGE</c> — plus, since M2-03, steps 6 and 9: <c>STAT_CONVERT</c> and all three
    /// <c>STAT_CAP_OVERRIDE</c> kinds. It still throws, with the owning task named, on a condition
    /// (M2-05) and on a <c>valueScale</c> (M2-06). That is the shape S6 asks for: a hole that fails
    /// loudly rather than a default that looks like an answer.
    /// </para>
    /// </remarks>
    internal static StatAggregationSeams Strict { get; } = new(
        UnconditionalEffectsOnly.Instance,
        AuthoredEffectValue.Instance,
        StatOpBehaviour.Instance);
}

/// <summary>
/// The `18` §8 step 2 filter M2-07 ships: an effect with no condition is active, and an effect with
/// one is refused.
/// </summary>
/// <remarks>
/// 🔒 <b>Refused, not skipped and not admitted.</b> Both of the quiet answers are wrong in a way
/// that shows up as a balance bug rather than an error: admitting every conditional effect gives
/// <c>PK_EXECUTIONER</c>'s +25% DMG% against a full-health target, and skipping every one deletes
/// <c>PK_BERSERK</c> from the build. M2-05 replaces this with the real evaluator.
/// </remarks>
internal sealed class UnconditionalEffectsOnly : IEffectConditionGate
{
    /// <summary>The single instance.</summary>
    internal static UnconditionalEffectsOnly Instance { get; } = new();

    private UnconditionalEffectsOnly()
    {
    }

    /// <inheritdoc />
    public bool IsActive(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.Condition is null
            ? true
            : throw new NotSupportedException(
                $"18 §8 step 2 filters by condition, evaluated against current state, and '{effect.Id}' " +
                "carries one. Evaluating 18 §4's condition functions needs the live fight and is M2-05's; " +
                "M2-07 ships the aggregation order with this seam open. Admitting the effect anyway would " +
                "apply PK_EXECUTIONER against a full-health target, and skipping it would delete " +
                "PK_BERSERK — both are silent balance bugs, so the aggregation refuses instead. Pass a " +
                "StatAggregationSeams with a real IEffectConditionGate.");
    }
}

/// <summary>
/// The `18` §1.1 value reader M2-07 ships: the authored <c>value</c>, unscaled.
/// </summary>
/// <remarks>
/// <para>
/// <c>valueScale</c> is refused because `18` §1.1 evaluates it against live state (<em>"at every
/// resolution pass for <c>ALWAYS</c> effects, at fire time for triggered ones"</em>), which this
/// task does not have. <see cref="ValueScale.EffectiveValue"/> already owns the arithmetic; only the
/// reading is missing.
/// </para>
/// <para>
/// A <c>valueMode</c> other than <c>FLAT</c> is refused for the same reason. `18` §9.1's
/// <c>STAT_SET MAX_HP</c> is the one stat op in the whole document that carries one, and it is
/// <c>FLAT</c>; the other seven modes of `18` §2.2 are damage- and heal-relative (<c>ATK_MULT</c>,
/// <c>TARGET_MAXHP_PCT</c>, <c>OVERHEAL_AMOUNT</c>, …) and what they would mean applied to a stat op
/// is written nowhere.
/// </para>
/// </remarks>
internal sealed class AuthoredEffectValue : IEffectValueReader
{
    /// <summary>The single instance.</summary>
    internal static AuthoredEffectValue Instance { get; } = new();

    private AuthoredEffectValue()
    {
    }

    /// <inheritdoc />
    public double EffectiveValue(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.ValueScale is not null)
        {
            throw new NotSupportedException(
                $"'{effect.Id}' carries a valueScale, which 18 §1.1 evaluates against live state " +
                "(effectiveValue = value x steps, steps = min(floor(fn / per), cap)). Reading fn is " +
                "M2-05's and the evaluator wiring is M2-06's; ValueScale.EffectiveValue already owns the " +
                "arithmetic. Pass a StatAggregationSeams with a real IEffectValueReader.");
        }

        if (effect.ValueMode is { } mode && mode != ValueMode.FLAT)
        {
            throw new NotSupportedException(
                $"'{effect.Id}' is a stat op with valueMode {mode}. 18 §2.2's value modes are damage- " +
                "and heal-relative, and the only stat op in 18 that carries one is 9.1's " +
                "STAT_SET MAX_HP with FLAT. What the other seven would mean against a stat is written " +
                "nowhere, so M2-07 refuses rather than picking one. Op behaviour is M2-03's.");
        }

        return effect.Value ?? throw new ArgumentException(
            $"'{effect.Id}' is a {effect.Op} with no value. Every stat op in 18 §2.1 is a magnitude; " +
            "an absent one is an authoring hole, and treating it as 0 would make STAT_ADD_* a no-op and " +
            "STAT_MULT a wipe.",
            nameof(effect));
    }
}
