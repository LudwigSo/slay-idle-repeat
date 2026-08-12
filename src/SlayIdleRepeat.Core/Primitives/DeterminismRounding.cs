namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 <b>`05` §1.1's 4-decimal-place rule — the arithmetic, stated once for the whole assembly.</b>
/// </summary>
/// <remarks>
/// <para>
/// `05` §1.1, verbatim: <em>"all combat math uses <c>double</c>, <b>rounded to 4 decimal places
/// (<c>Math.Round(x, 4)</c>) at every accumulation point</b> — after each damage calculation, each
/// heal, and each stat aggregation step. This is the locked determinism rule (`14` §8.2, `18` §8
/// step 10, `16` A3)."</em>
/// </para>
/// <para>
/// 🔒 <b>Why this type exists, and what it replaces.</b> The rule had <b>six</b> independent
/// statements in <c>Core</c> before M2-02 — <c>Rules/Stats/StatRounding</c>,
/// <c>Rules/Effects/Ops/OpRounding</c>, <c>Content/Effects/ValueScale</c>,
/// <c>Rules/Effects/Conditions/ConditionEvaluator</c> and two in <c>Rules/Effects/Triggers/</c> —
/// plus two guard sites (<c>CanonicalStateWriter</c>, <c>CombatLog</c>) restating the predicate. Each
/// was written independently, and each was a place the <c>4</c>, the midpoint mode or the trailing
/// <c>+ 0.0</c> could drift. <b>Two of the six had already drifted:</b>
/// <c>Rules/Effects/Triggers/TriggerInstance</c> and <c>TriggerRouting</c> rounded without the
/// <c>+ 0.0</c>, so a <c>-0.0</c> survived them — unobservable in the first (its result is only
/// compared with <c>&gt;</c>) and reaching <c>IRunEffectSink</c> in the second, where <c>CombatLog</c>
/// would have refused it later, naming the log rather than the trigger. Consolidating them normalises
/// both. That is the strongest single argument for this type existing and it is recorded rather than
/// smoothed over. M2-03 recorded the duplication it could see and named the fix:
/// <em>"a shared primitive under <c>Core.Primitives</c> is the real fix and belongs with M2-02's
/// relocation of the `18` seams out of <c>Rules/Stats/</c>."</em> This is it.
/// </para>
/// <para>
/// 🔒 <b><c>Primitives</c>, and it has to be.</b> R17 makes <c>Rules.Effects</c> the bottom of the
/// intra-<c>Rules</c> layering, so it cannot reach <c>Rules.Stats</c> — which is <em>why</em>
/// <c>OpRounding</c> was written as a second statement in the first place. `30` §11.4's dependency
/// direction is <c>Handlers ▶ Rules ▶ Model ▶ Content ▶ Primitives</c>, so <c>Primitives</c> is the
/// one place every layer that rounds can name. It is deliberately <em>not</em> in <c>Rules/</c>:
/// <c>Content.Effects.ValueScale</c> rounds too, and <c>Content</c> may not name <c>Rules</c>.
/// </para>
/// <para>
/// ⚠️ <b>The callers keep their own failure types, and that is not a second statement of the
/// rule.</b> <c>StatRounding</c> throws an <c>ArithmeticException</c> naming the `18` §8 step and the
/// stat; <c>OpRounding</c> throws an <c>EffectContextException</c> naming the effect id. Both
/// messages are load-bearing — steering S2 asks which rule fired, not merely that one did — and
/// neither is arithmetic. What is consolidated here is the <b>number of places</b>, the
/// <b>midpoint mode</b> and the <b>negative-zero normalisation</b>: the three things whose
/// disagreement would produce two different hashes for one state.
/// </para>
/// <para>
/// 🔒 <b>The trailing <c>+ 0.0</c> is not redundant.</b> <c>Math.Round(-0.00004, 4)</c> is
/// <c>-0.0</c> and .NET preserves the sign of zero, so a value that drifts a hair below zero — a `18`
/// §7.10 Bog Air <c>-0.35</c> against a small base, a <c>SUNDER</c> stack taking DEF through zero —
/// would reach <c>CanonicalStateWriter</c> as a negative zero. That writer <b>throws</b> rather than
/// encoding one, because <c>-0.0 == 0.0</c> is true in C# while the bit patterns differ, so two
/// states the language calls identical would carry different <c>stateHash</c>es.
/// </para>
/// <para>
/// 🔒 <b><see cref="MidpointRounding.ToEven"/> is written out.</b> It is <c>Math.Round</c>'s default
/// and two of the six former statements already spelled it while four relied on the default. Stating
/// it makes the rule independent of that default rather than of six authors agreeing about it.
/// </para>
/// <para>
/// ⚠️ <b>NaN and infinity are not handled here.</b> They are not rounding questions — an infinite
/// value is an overflow upstream and a NaN is a <c>0 × ∞</c> or a <c>0/0</c> — and the caller is the
/// only layer that knows which step, stat or effect produced it. Every caller refuses them before
/// calling, with a message that names its own subject; <see cref="IsRounded"/> answers <c>false</c>
/// for both so a guard site needs no second check.
/// </para>
/// </remarks>
internal static class DeterminismRounding
{
    /// <summary>🔒 The number of decimal places `05` §1.1, `14` §8.2 and `18` §8 step 10 lock.</summary>
    internal const int Decimals = 4;

    /// <summary>
    /// Rounds one accumulated value to <see cref="Decimals"/> places and normalises <c>-0.0</c> to
    /// <c>+0.0</c>.
    /// </summary>
    /// <remarks>
    /// Total: a NaN rounds to a NaN and an infinity to itself, both unchanged. Refusing them is the
    /// caller's, because only the caller can name what produced one. See the type remarks.
    /// </remarks>
    internal static double Round(double value) =>
        Math.Round(value, Decimals, MidpointRounding.ToEven) + 0.0;

    /// <summary>
    /// True when a value is already in the form <see cref="Round"/> produces: finite, rounded to
    /// <see cref="Decimals"/> places, and not a negative zero.
    /// </summary>
    /// <remarks>
    /// The predicate <c>ActorStats</c> and <c>StatCaps</c> guard with, through
    /// <c>StatRounding.IsRounded</c>.
    /// <para>
    /// ⚠️ <c>CanonicalStateWriter</c> and <c>CombatLog</c> do <b>not</b> use it, and the asymmetry is
    /// deliberate: they state the NaN, the infinity, the negative zero and the unrounded value as
    /// four separate arms so that each carries its own message (steering S2). Their rounding arm is
    /// <c>Round(v) != v</c>, which is exactly equivalent to this predicate's third clause.
    /// </para>
    /// <para>
    /// NaN answers <c>false</c> — deliberately, since <c>Math.Round(NaN, 4) != NaN</c> and a NaN is
    /// exactly the value that must not be waved through.
    /// </para>
    /// </remarks>
    internal static bool IsRounded(double value) =>
        double.IsFinite(value) &&
        Math.Round(value, Decimals, MidpointRounding.ToEven) == value &&
        !(double.IsNegative(value) && value == 0.0);
}
