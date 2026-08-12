using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Stacking;

/// <summary>
/// 🔒 `18` §6 — the applications of one effect on one owner, combined by its stacking mode.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Immutable, and <see cref="Apply"/> returns a new set.</b> `05` §3.1 reads a status's stack
/// count <em>"at the moment the tick lands"</em>, so the count is a value that belongs to a moment,
/// not a counter something increments. A mutable set would let a stat aggregation and a DoT tick in
/// the same simulation tick disagree about how many stacks there were.
/// </para>
/// <para>
/// 🔒 <b>The applied values are held, not folded as they arrive.</b> Each application carries its own
/// <c>value</c> — `18` §1.1's <c>valueScale</c> is re-read at fire time, so two applications of one
/// effect can legitimately differ — and <c>HIGHEST_WINS</c> could not be answered from a running
/// total at all.
/// </para>
/// <para>
/// 🔒 <b><see cref="CombinedValue"/> does NOT round to 4 decimal places, and that is deliberate.</b>
/// `05` §1.1's accumulation points are <em>"after each damage calculation, each heal, and each stat
/// aggregation step"</em>; a stack product is none of the three. `18` §8 <b>step 7</b> is the
/// accumulation point for a <c>STAT_MULT</c>, and <c>StatAggregation</c> already rounds there.
/// Rounding here as well is not belt-and-braces — it is an <em>earlier</em>, unauthorised
/// accumulation point, and it changes the answer: three seconds of `05` §3.1's <c>SYS_ENRAGE</c> is
/// <c>1.08³ = 1.259712</c>, which rounds to <c>1.2597</c>, and <c>100 × 1.2597 = 125.97</c> instead
/// of the <c>125.9712</c> the document fixes — the enrage quietly weakened in the fourth decimal
/// place, once a second, for the rest of the fight. <c>SysEnrageStackingTests</c> pins both readings
/// apart.
/// </para>
/// <para>
/// ⚠️ <b>M2-10's contract.</b> `05` §3.1's DoT/HoT cadence rule is that <em>"reapplication adds
/// stacks / refreshes duration per the status's stacking rule (`18` §6) but <b>never re-anchors the
/// cadence</b>. Per-tick amount = per-second potency × current stack count, read at the moment the
/// tick lands."</em> Nothing here touches a cadence anchor: <see cref="Apply"/> reports
/// <see cref="StackApplication.RefreshDuration"/>, which is about the effect's <em>duration</em>, and
/// the cadence is M2-10's to leave alone. <see cref="Count"/> is readable at any moment, which is what
/// "read at the moment the tick lands" needs.
/// </para>
/// </remarks>
internal sealed record EffectStackSet
{
    /// <summary>The effect's `18` §8 id — named in every failure (steering S2).</summary>
    internal required string EffectId { get; init; }

    /// <summary>`18` §6's stacking block.</summary>
    internal required EffectStacking Stacking { get; init; }

    /// <summary>The applications currently held, in application order.</summary>
    internal IReadOnlyList<double> Applications { get; init; } = [];

    /// <summary>
    /// How many stacks are held — `05` §3.1's <em>"current stack count, read at the moment the tick
    /// lands"</em>.
    /// </summary>
    internal int Count => Applications.Count;

    /// <summary>
    /// The applications combined per `18` §6's mode. Not rounded — see the type remarks.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The set holds no applications. Neither <c>0</c> nor <c>1</c> is the value of a status that is
    /// not there: under <c>MULTIPLICATIVE</c> a silent <c>1</c> is an invisible <c>STAT_MULT ×1</c> on
    /// a boss that has never enraged, and under <c>ADDITIVE</c> a silent <c>0</c> is a status the
    /// player can see with no potency. Steering S6 — a hole fails loudly.
    /// </exception>
    internal double CombinedValue
    {
        get
        {
            if (Applications.Count == 0)
            {
                throw new InvalidOperationException(
                    $"'{EffectId}' holds no applications, so 18 §6 gives it no combined value. " +
                    "An empty set is not a zero and not a one — under MULTIPLICATIVE a substituted 1 " +
                    "is an invisible STAT_MULT x1 and under ADDITIVE a substituted 0 is a visible " +
                    "status with no potency. Ask Count first.");
            }

            return Stacking.Mode switch
            {
                StackingMode.ADDITIVE => Fold(static (running, next) => running + next, 0.0),
                StackingMode.MULTIPLICATIVE => Fold(static (running, next) => running * next, 1.0),

                // 🔒 The three single-instance modes. Apply keeps exactly one application for each,
                // so "the one that won" is the whole set — REPLACE's newest, HIGHEST_WINS' strongest
                // and NONE's first are all Applications[0] by the time they get here.
                StackingMode.REPLACE or StackingMode.HIGHEST_WINS or StackingMode.NONE =>
                    Applications[0],

                _ => throw Unhandled(nameof(CombinedValue)),
            };
        }
    }

    /// <summary>An empty set for one effect and one stacking block.</summary>
    /// <param name="effectId">The effect's `18` §8 id.</param>
    /// <param name="stacking">`18` §6's stacking block.</param>
    /// <exception cref="ArgumentException"><paramref name="effectId"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="stacking"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <c>maxStacks</c> is below one. <c>game-data/schema/effect.schema.json</c> declares it
    /// <c>"minimum": 1</c>, and the two statements of the bound have to agree — a set built in code
    /// (the balance harness `05` §9, a test) must not reach a state no authored JSON can. A ceiling
    /// of zero is not "no stacking": <c>NONE</c> is.
    /// </exception>
    internal static EffectStackSet Empty(string effectId, EffectStacking stacking)
    {
        ArgumentException.ThrowIfNullOrEmpty(effectId);
        ArgumentNullException.ThrowIfNull(stacking);

        if (stacking.MaxStacks is { } max && max < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stacking), max,
                $"18 §6's maxStacks is a stack ceiling and '{effectId}' declares " +
                $"{max.ToString(CultureInfo.InvariantCulture)}. The schema declares it 'minimum': 1 " +
                "and null for uncapped, so a ceiling below one is a value no authored effect can " +
                "carry — and it is not 'no stacking', which is the NONE mode.");
        }

        return new EffectStackSet { EffectId = effectId, Stacking = stacking };
    }

    /// <summary>Applies one more application of the effect.</summary>
    /// <param name="value">
    /// The application's value — already scaled by `18` §1.1 if the effect carries a
    /// <c>valueScale</c>, because §1.1 re-reads it at fire time.
    /// </param>
    /// <exception cref="InvalidOperationException">The mode is outside `18` §6's five.</exception>
    internal StackApplication Apply(double value)
    {
        // 🔒 The FIRST application is not a reapplication. `18` §6's key is refreshOnReapply, and
        // reporting a refresh here would have M2-10 re-anchor a cadence it had only just set.
        if (Applications.Count == 0)
        {
            return new StackApplication(
                this with { Applications = Only(value) }, StackAdded: true, RefreshDuration: false);
        }

        // 🔒 An INDEPENDENT key, honoured for every mode including NONE. `05` §5's BLEED — "does not
        // stack; reapplication refreshes" — is exactly NONE plus refreshOnReapply, so the
        // combination is authored rather than hypothetical. An ABSENT key is not a refresh: `true`
        // is opt-in language and `18` §1's canonical {"mode":"ADDITIVE","maxStacks":1} omits it, so
        // the absence is the absence of the opt-in rather than a manufactured default (steering S6).
        var refresh = Stacking.RefreshOnReapply ?? false;

        // ⚠️ `18` §6 names the five modes and writes NOT ONE WORD about what any of them does — the
        // section is `mode`: ADDITIVE · MULTIPLICATIVE · REPLACE · HIGHEST_WINS · NONE, and that is
        // all of it. Each arm below is therefore the plain reading of the mode's NAME, recorded as an
        // inference rather than dressed up as a quotation (steering S6). Three of the five are pinned
        // to authored behaviour elsewhere and are not inferences at all: MULTIPLICATIVE is `05` §3.1's
        // SYS_ENRAGE ("multiplicative stacking, uncapped"), ADDITIVE is `05` §5's SUNDER and BURN
        // ("stacks to 5"), and NONE is `05` §5's BLEED ("does not stack; reapplication refreshes").
        // REPLACE and HIGHEST_WINS have no authored user in the content set today, so if either name
        // ever meant something other than the reading below, `18` §6 is what has to say so.
        return Stacking.Mode switch
        {
            // NONE — a second application is ignored. The stack set, not the duration.
            StackingMode.NONE => new StackApplication(this, StackAdded: false, refresh),

            // REPLACE — a new application replaces the existing one. The count does not grow.
            StackingMode.REPLACE => new StackApplication(
                this with { Applications = Only(value) }, StackAdded: false, refresh),

            // HIGHEST_WINS — the strongest application wins.
            // ⚠️ Compared LITERALLY, not by magnitude: `18` §6 authors no reading of "highest" for a
            // negative value, and taking the larger number is the only one that needs no invention.
            // The consequence, stated so nobody has to rediscover it: a negative-valued debuff
            // authored with HIGHEST_WINS keeps the LEAST negative application. No content does that
            // today, and the day some does, `18` §6 is what has to say what it meant.
            StackingMode.HIGHEST_WINS => new StackApplication(
                this with { Applications = Only(Math.Max(Applications[0], value)) },
                StackAdded: false,
                refresh),

            StackingMode.ADDITIVE or StackingMode.MULTIPLICATIVE => AtCeiling()
                // 🔒 The surplus application is DROPPED, and the duration still refreshes. `18` §6
                // states the ceiling and says nothing about eviction, so nothing is evicted —
                // dropping invents no policy, where "replace the oldest" would invent one that
                // changes the combined value. `05` §3.1 keeps the two questions apart: "reapplication
                // adds stacks / refreshes duration", not "adds stacks and therefore refreshes".
                ? new StackApplication(this, StackAdded: false, refresh)
                : new StackApplication(
                    this with { Applications = Grown(value) }, StackAdded: true, refresh),

            _ => throw Unhandled(nameof(Apply)),
        };
    }

    /// <summary>🔒 <c>maxStacks: null</c> is uncapped — `05` §3.1's <c>SYS_ENRAGE</c> depends on it.</summary>
    private bool AtCeiling() => Stacking.MaxStacks is { } max && Applications.Count >= max;

    /// <summary>The set's applications, plus one more, in application order.</summary>
    /// <remarks>
    /// ⚠️ <b>An explicit array rather than the collection expression <c>[.. Applications, value]</c>,
    /// and not as a style preference.</b> A collection expression targeting
    /// <see cref="IReadOnlyList{T}"/> is lowered by the compiler into a synthesized type
    /// (<c>&lt;&gt;z__ReadOnlyArray</c>, <c>&lt;&gt;z__ReadOnlySingleElementList</c>) that lands in
    /// the <b>global</b> namespace — which
    /// <c>AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace</c> rejects,
    /// because `30` §11.4 enumerates <c>Core</c>'s namespaces and <c>Core_internal_layering_holds</c>
    /// has no row for a type outside them. <c>[]</c> is safe (it lowers to <c>Array.Empty</c>); a
    /// non-empty one is not. Recorded here rather than left to be rediscovered — every M2 task
    /// building a read-only collection in <c>Core</c> hits it.
    /// </remarks>
    private double[] Grown(double value)
    {
        var grown = new double[Applications.Count + 1];

        for (var i = 0; i < Applications.Count; i++)
        {
            grown[i] = Applications[i];
        }

        grown[Applications.Count] = value;

        return grown;
    }

    /// <summary>The three single-instance modes' one held application. See <see cref="Grown"/>.</summary>
    private static double[] Only(double value) => new[] { value };

    private double Fold(Func<double, double, double> combine, double identity)
    {
        var running = identity;

        // 🔒 In application order, and by index rather than through LINQ: `18` §8's whole point is
        // that two implementations of one rule must produce the same double, and a fold's order is
        // part of its answer in floating point.
        for (var i = 0; i < Applications.Count; i++)
        {
            running = combine(running, Applications[i]);
        }

        return running;
    }

    private InvalidOperationException Unhandled(string member) =>
        new($"'{EffectId}' stacks with mode {Stacking.Mode}, which {member} does not answer for. " +
            "18 §6 authors five: ADDITIVE, MULTIPLICATIVE, REPLACE, HIGHEST_WINS, NONE.");
}

/// <summary>What one application of an effect did to its stack set and to its duration.</summary>
/// <param name="Stacks">The set after the application.</param>
/// <param name="StackAdded">
/// Whether the application increased <see cref="EffectStackSet.Count"/>. False for a
/// <c>NONE</c> reapplication, for one past <c>maxStacks</c>, and for the single-instance modes, whose
/// count is one from the first application onward.
/// </param>
/// <param name="RefreshDuration">
/// 🔒 Whether `18` §6's <c>refreshOnReapply</c> asks for the effect's duration to restart.
/// <b>Duration only</b> — `05` §3.1 is explicit that reapplication <em>"never re-anchors the
/// cadence"</em>, so M2-10 restarts the timer this reports on and leaves its one-second DoT/HoT
/// anchor exactly where it was.
/// </param>
internal readonly record struct StackApplication(
    EffectStackSet Stacks, bool StackAdded, bool RefreshDuration);
