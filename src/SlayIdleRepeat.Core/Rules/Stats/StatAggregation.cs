using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// The result of one `18` §8 aggregation pass.
/// </summary>
/// <param name="Final">The capped, rounded 14-stat block — `18` §8 after step 10.</param>
/// <param name="PostMultiplierMaxHp">
/// 🔒 Max HP <b>as it stood after `18` §8 step 7</b>: post-multiplier, pre-<c>STAT_SET</c>.
/// <para>
/// `05` §4.1 asks for this value by name and for one reason: the ward pool cap is
/// <c>wardCapPct × "the actor's Max HP as it stood after `18` §8 step 7"</c>, <em>"which is what
/// keeps <c>CP_GLASS_HEART</c>'s re-based shields functional (`18` §9.1)"</em>. That perk is
/// <c>STAT_MULT ALL_COMBAT ×2</c> plus <c>STAT_SET MAX_HP 1</c>, and `18` §9.1 fixes the interaction:
/// <em>"<c>MAX_HP</c> is set after all multipliers (step 8), so ×2 never applies to it"</em>. Read
/// <see cref="Final"/> instead and every shield on that build is capped at 1 HP — which `18` §9.1
/// says explicitly must not happen, because shields <em>"are the build's entire survival
/// mechanism"</em>.
/// </para>
/// <para>
/// It is exposed as the one named intermediate rather than as the whole post-step-7 block: `05` §4.1
/// authorises this single reading, and a pre-cap block on the result would be an invitation to read
/// an uncapped CRIT from it.
/// </para>
/// <para>
/// ⚠️ <b>Two things M2-09 must do with it, neither of which anything can enforce from here.</b>
/// <b>(1)</b> Hold the whole <see cref="AggregatedStats"/> as actor state. A consumer that keeps
/// <see cref="Final"/> and discards the wrapper caps every <c>CP_GLASS_HEART</c> ward at 1 HP with
/// nothing going red — the one loss in this record that is not reported, unlike
/// <see cref="SkippedNonCombatStatEffects"/>. <b>(2)</b> Re-read it on every re-aggregation rather
/// than caching it at battle start: `05` §3.1's <c>SYS_ENRAGE</c> adds a <c>STAT_MULT</c> every
/// second from 70 s, so the post-step-7 Max HP of a boss is not a battle constant.
/// </para>
/// </param>
/// <param name="SkippedNonCombatStatEffects">
/// The ids of stat-op effects naming one of `18` §2.1's <b>12 non-combat</b> stats
/// (<c>GOLD_PCT</c>, <c>DROP_CHANCE</c>, …). They are authored, legitimate and simply not this
/// pipeline's subject — `05` §1's actor block is the 14 combat stats. They are reported rather than
/// dropped: a resolver that hands the whole build here and never asks what was left behind would
/// silently lose every "+X% Gold Gain" affix in the game, and nothing would go red.
/// </param>
/// <remarks>
/// ⚠️ <b>Record equality compares <see cref="SkippedNonCombatStatEffects"/> by reference</b>, as it
/// does for any collection member of a record. <see cref="Final"/> and
/// <see cref="PostMultiplierMaxHp"/> compare by value — <see cref="ActorStats"/> implements
/// <see cref="IEquatable{T}"/> over its fourteen slots — so two results of the same aggregation are
/// equal only when they happen to share the list instance. Compare the members where the comparison
/// carries the meaning; <c>Aggregation_does_not_depend_on_the_order_the_effects_arrive_in</c> does,
/// and records why.
/// </remarks>
internal sealed record AggregatedStats(
    ActorStats Final,
    double PostMultiplierMaxHp,
    IReadOnlyList<string> SkippedNonCombatStatEffects);

/// <summary>
/// 🔒 `18` §8 — the stat aggregation order, <em>"must be implemented exactly, or builds will produce
/// different numbers on client and server."</em>
/// </summary>
/// <remarks>
/// <para>
/// The ten steps, as `18` §8 writes them:
/// </para>
/// <code>
/// 1. Collect all active effects from: gear → affixes → set bonuses → talents →
///    pet auras → mount → run buffs → shrine buffs → curses → perks (in draft order)
/// 2. Filter by condition, evaluated against current state
/// 3. Group by (op, stat)
/// 4. Apply STAT_ADD_FLAT      (sum)
/// 5. Apply STAT_ADD_PCT       (sum, then multiply base once)
/// 6. Apply STAT_CONVERT       (reads post-step-5 values, in effect-id order)
/// 7. Apply STAT_MULT          (product, in effect-id order)
/// 8. Apply STAT_SET           (last writer wins, in effect-id order)
/// 9. Apply caps, honouring STAT_CAP_OVERRIDE
/// 10. Round every resulting stat to 4 decimal places
/// </code>
/// <para>
/// 🔴 <b><c>STAT_MULT</c> is <c>× Π(value)</c>, not <c>× Π(1 + value)</c>.</b> `05` §1.1 writes the
/// last term of its formula as <c>Π (1 + Multiplicative(stat))</c> and that is an <b>erratum</b>.
/// Three independent places in the locked documents say otherwise, and every one of them breaks
/// under the <c>(1 + v)</c> reading:
/// </para>
/// <list type="bullet">
/// <item>`18` §8 step 7 is <em>"apply <c>STAT_MULT</c> (<b>product</b>, in effect-id order)"</em> — a
/// product of the values themselves.</item>
/// <item>`18` §9.1 authors <c>{"op":"STAT_MULT","stat":"ALL_COMBAT","value":2.0}</c> and captions it
/// <em>"×2 all stats"</em>. Under <c>(1 + v)</c> it would be ×3.0.</item>
/// <item>`05` §3.1's <c>SYS_ENRAGE</c> is <c>STAT_MULT ATK ×1.08</c>, captioned <em>"+8% ATK per
/// second"</em>. Under <c>(1 + v)</c> that is ×2.08 <em>per second</em>, and the boss one-shots the
/// hero about three seconds into the enrage.</item>
/// </list>
/// <para>
/// The additive percent bucket keeps `05` §1.1's <c>(1 + Σ PctAdd)</c> form — only the multiplicative
/// term is corrected. <c>StatAggregationTests</c> pins both readings apart with literal numbers.
/// </para>
/// <para>
/// 🔒 <b>Step 1 is the caller's, and its order is immaterial.</b> `18` §8 step 1 lists ten sources
/// <em>"(in draft order)"</em> and the section then closes with <em>"effect-id order means the
/// ascending lexicographic order of effect IDs, not draft order"</em>. Those are not in conflict:
/// the list is the <b>collection</b> order — which effects to gather — and effect-id order is the
/// <b>application</b> order at steps 6, 7 and 8. This method sorts its whole input ordinally once,
/// through <see cref="EffectOrder"/>, before any step reads it, so the order it arrives in cannot
/// reach the arithmetic. <c>Aggregation_does_not_depend_on_the_order_the_effects_arrive_in</c> is
/// that claim as a test.
/// </para>
/// <para>
/// ⚠️ <b>With one caveat, recorded as errata: effect-id order is not <em>total</em> if two effects
/// share an id.</b> <see cref="Enumerable.OrderBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey}, IComparer{TKey})"/>
/// is a stable sort, so equal ids keep the order they arrived in — observable at step 8, where the
/// last writer wins. <see cref="EffectDefinition"/> calls the id <em>repository-unique</em> and
/// delegates uniqueness to the content pipeline (`14` §6's duplicate-id failure class), so within
/// one authored catalogue this cannot happen; what `18` §8 does not say is what a <em>build</em>
/// means when it collects the same authored affix from two gear slots. Nothing is invented here: the
/// sort is left stable, no duplicate is rejected, and the question is handed to M2-02, which owns
/// step 1's collection and is the only place that knows whether the two instances are one effect or
/// two.
/// </para>
/// <para>
/// 🔒 <b>Steps 4 and 5 are summed in effect-id order too, and `18` §8 does not say so.</b> It states
/// an order for steps 6, 7 and 8 only. Floating-point addition is not associative —
/// <c>(1e16 + 1) - 1e16</c> is 0 and <c>1e16 + (1 - 1)</c> is 0 while <c>(1e16 + 1) - 1</c> is not —
/// so a "sum" with no stated order is not a deterministic sum, which is the precise property `18` §8
/// exists to guarantee. Recorded as errata; the same comparer is used for all five steps.
/// </para>
/// <para>
/// ⚠️ <b>What this type does not do.</b> Step 1 (collection) is M2-02's, step 2's condition
/// evaluation is M2-05's, and steps 6 and 9's op behaviour is M2-03's — each is a seam on
/// <see cref="StatAggregationSeams"/>, with a strict default that refuses input it cannot resolve.
/// Ops outside `18` §2.1's six stat ops (<see cref="EffectOpFamily.STAT"/>) are ignored <em>before</em>
/// the step-2 gate is consulted: `18` §8's steps name five of them and nothing else changes a stat,
/// so a conditional <c>DAMAGE</c> clause is not this pipeline's to refuse. <c>STAT_COPY</c>
/// (`18` §2.4) is the one near-miss — it resolves <em>to</em> a percent-bucket add and therefore
/// reaches this pipeline as a <c>STAT_ADD_PCT</c>, which is M2-03's to emit.
/// </para>
/// <para>
/// ⚠️ <b>Step 2 therefore has two owners, and this is which one is authoritative.</b> M2-02's
/// resolver owns step 2 for the whole op set; this method re-applies the gate to the stat ops so
/// that it is correct when called on its own — the balance harness (`05` §9) and every test here do
/// call it on its own. `18` §4's functions are <em>"pure functions of current state"</em>, so a
/// second evaluation within one resolution pass returns the same answer and costs only the call. A
/// resolver that has already filtered may pass an always-true gate; it must not pass a gate that
/// answers <em>differently</em>, because then the two step-2s disagree and the later one wins.
/// </para>
/// </remarks>
internal static class StatAggregation
{
    /// <summary>
    /// Runs `18` §8 steps 2 through 10 over an already-collected effect list.
    /// </summary>
    /// <param name="baseStats">
    /// <c>Base(stat)</c> — `05` §2's hero curve, or `05` §6's enemy derivation.
    /// </param>
    /// <param name="effects">
    /// The effects step 1 collected, in any order. Sorted ordinally here before anything reads them.
    /// </param>
    /// <param name="caps">`05` §1's ceilings, before <c>STAT_CAP_OVERRIDE</c>.</param>
    /// <param name="seams">
    /// The three parts of `18` §8 that belong to other tasks. Use
    /// <see cref="StatAggregationSeams.Strict"/> for the M2-07 behaviour.
    /// </param>
    internal static AggregatedStats Aggregate(
        ActorStats baseStats,
        IReadOnlyList<EffectDefinition> effects,
        StatCaps caps,
        StatAggregationSeams seams)
    {
        ArgumentNullException.ThrowIfNull(baseStats);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentNullException.ThrowIfNull(seams);

        // ── Step 2 · filter by condition. The gate is M2-05's; the strict default refuses a
        //    conditional effect rather than guessing which way it would have gone.
        //
        //    🔒 Asked ONLY of `18` §2.1's six stat ops. Step 1 collects the whole build, and steps
        //    4-9 read nothing else — an ON_HIT DAMAGE clause with a TARGET_HP_PCT condition changes
        //    no stat, so refusing it here would make StatAggregationSeams.Strict unusable against
        //    any real build for a reason that has nothing to do with the stat pipeline. The full
        //    step-2 filter over every op is M2-02's, at the resolver.
        var active = new List<EffectDefinition>(effects.Count);
        foreach (var effect in effects)
        {
            ArgumentNullException.ThrowIfNull(effect, nameof(effects));

            if (effect.Family == EffectOpFamily.STAT && seams.Conditions.IsActive(effect))
            {
                active.Add(effect);
            }
        }

        // ── Steps 3-8's ordering, established ONCE. See the remarks: collection order is
        //    immaterial and application order is ordinal effect-id order, for every step.
        // ── Step 3 · "group by (op, stat)". The (op) half is this lookup; the (stat) half is the
        //    per-stat accumulation each step below performs.
        var byOp = active.InEffectIdOrder().ToLookup(e => e.Op);

        var skipped = new List<string>();
        var values = baseStats.ToSlots();

        // ── Step 4 · STAT_ADD_FLAT (sum), then round.
        var flat = Sum(byOp[EffectOp.STAT_ADD_FLAT], seams.Values, skipped);
        for (var slot = 0; slot < values.Length; slot++)
        {
            values[slot] += flat[slot];
        }

        RoundAll(values, "step 4 (STAT_ADD_FLAT)");

        // ── Step 5 · STAT_ADD_PCT (sum, then multiply base once), then round.
        var percent = Sum(byOp[EffectOp.STAT_ADD_PCT], seams.Values, skipped);
        for (var slot = 0; slot < values.Length; slot++)
        {
            values[slot] *= 1.0 + percent[slot];
        }

        RoundAll(values, "step 5 (STAT_ADD_PCT)");

        // ── Step 6 · STAT_CONVERT, reading post-step-5 values, in effect-id order. The block handed
        //    to the seam is frozen, so "reads post-step-5 values" is enforced rather than trusted.
        var postAdditive = ActorStats.FromSlots(values);
        foreach (var delta in seams.Ops.Convert(byOp[EffectOp.STAT_CONVERT].ToArray(), postAdditive, seams.Values))
        {
            values[ActorStats.SlotOf(delta.Stat)] += delta.Amount;
        }

        RoundAll(values, "step 6 (STAT_CONVERT)");

        // ── Step 7 · STAT_MULT (product, in effect-id order), then round.
        //    🔴 The product is of the VALUES. See the type remarks for the three confirmations.
        foreach (var (effect, stats) in Targets(byOp[EffectOp.STAT_MULT], skipped))
        {
            var factor = seams.Values.EffectiveValue(effect);
            foreach (var stat in stats)
            {
                values[ActorStats.SlotOf(stat)] *= factor;
            }
        }

        RoundAll(values, "step 7 (STAT_MULT)");

        // 🔒 `05` §4.1's ward cap reads Max HP HERE — post-multiplier, pre-STAT_SET.
        var postMultiplierMaxHp = values[ActorStats.SlotOf(StatId.MAX_HP)];

        // ── Step 8 · STAT_SET (last writer wins, in effect-id order), then round.
        foreach (var (effect, stats) in Targets(byOp[EffectOp.STAT_SET], skipped))
        {
            var value = seams.Values.EffectiveValue(effect);
            foreach (var stat in stats)
            {
                values[ActorStats.SlotOf(stat)] = value;
            }
        }

        RoundAll(values, "step 8 (STAT_SET)");

        // ── Step 9 · caps, honouring STAT_CAP_OVERRIDE, then round.
        //
        //    🔒 Two halves, because `18` §2.1 gives the op two jobs — "raise OR REDIRECT a stat
        //    cap". The raise is a change to the table below; the redirect (`09` §4's Perfect Strike,
        //    "crit above the 75% cap converts to crit damage") moves value from one stat to another
        //    and cannot be expressed as a cap at all. M2-03 added the second seam member for it.
        var overrides = byOp[EffectOp.STAT_CAP_OVERRIDE].ToArray();
        var effective = seams.Ops.OverrideCaps(overrides, caps, seams.Values);

        // 🔒 Frozen BEFORE the caps land — this is the only place the overshoot still exists, and
        //    freezing it is what stops one redirect reading another's output, exactly as step 6's
        //    post-step-5 block does. Built only when there IS an override: no authored content
        //    carries one today, and this runs per actor whenever the fight changes (05 §3.1's
        //    SYS_ENRAGE re-aggregates every second from 70 s).
        var preCap = overrides.Length == 0 ? null : ActorStats.FromSlots(values);

        for (var slot = 0; slot < values.Length; slot++)
        {
            values[slot] = effective.Apply(StatAt(slot), values[slot]);
        }

        if (preCap is not null)
        {
            foreach (var delta in seams.Ops.RedirectCappedExcess(overrides, preCap, effective, seams.Values))
            {
                values[ActorStats.SlotOf(delta.Stat)] += delta.Amount;
            }

            // 🔒 Re-applied, and it is not belt-and-braces. Step 9 is "apply caps"; a redirect that
            //    carried its DESTINATION past that stat's own ceiling would leave step 9 having
            //    produced an uncapped stat. Idempotent for every stat no redirect touched.
            for (var slot = 0; slot < values.Length; slot++)
            {
                values[slot] = effective.Apply(StatAt(slot), values[slot]);
            }
        }

        RoundAll(values, "step 9 (caps)");

        // ── Step 10 · "round every resulting stat to 4 decimal places". Idempotent after the per-step
        //    rounding above, and stated anyway: `18` §8 lists it, and a future step inserted before it
        //    should not have to remember.
        RoundAll(values, "step 10 (final rounding)");

        return new AggregatedStats(ActorStats.FromSlots(values), postMultiplierMaxHp, skipped);
    }

    /// <summary>`05` §1.1's rounding, at one `18` §8 step boundary.</summary>
    private static void RoundAll(double[] values, string step)
    {
        for (var slot = 0; slot < values.Length; slot++)
        {
            values[slot] = StatRounding.Round(values[slot], StatAt(slot), step);
        }
    }

    /// <summary>
    /// `18` §8 step 3's grouping for an additive step: one sum per stat, accumulated in effect-id
    /// order.
    /// </summary>
    private static double[] Sum(
        IEnumerable<EffectDefinition> ordered, IEffectValueReader values, List<string> skipped)
    {
        var sums = new double[StatIds.Combat.Count];

        foreach (var (effect, stats) in Targets(ordered, skipped))
        {
            var amount = values.EffectiveValue(effect);
            foreach (var stat in stats)
            {
                sums[ActorStats.SlotOf(stat)] += amount;
            }
        }

        return sums;
    }

    /// <summary>
    /// Each effect paired with the combat stats it writes, skipping — and recording — the effects
    /// that write none.
    /// </summary>
    /// <remarks>
    /// 🔒 The stats are resolved <b>before</b> <see cref="IEffectValueReader.EffectiveValue"/> is
    /// asked for a value, and an effect that names no combat stat never reaches the reader at all.
    /// That ordering is load-bearing: a "+X% Gold Gain" gear affix carrying `18` §1.1's
    /// <c>valueScale</c> (the <c>PK_HOARD</c> shape) is legitimate authored content that this
    /// pipeline does not touch, and asking the strict reader to value it would throw about M2-06 for
    /// a stat the actor block does not hold.
    /// </remarks>
    private static IEnumerable<(EffectDefinition Effect, IReadOnlyList<StatId> Stats)> Targets(
        IEnumerable<EffectDefinition> ordered, List<string> skipped)
    {
        foreach (var effect in ordered)
        {
            var stats = CombatStatsOf(effect);

            if (stats.Count == 0)
            {
                skipped.Add(effect.Id);
                continue;
            }

            yield return (effect, stats);
        }
    }

    /// <summary>
    /// The combat stats an effect's <c>stat</c> selector names — the empty list when it names only
    /// `18` §2.1's non-combat stats.
    /// </summary>
    /// <remarks>
    /// <c>ALL_COMBAT</c> expands to the fourteen (`18` §9.1 / <see cref="StatSelector"/>), a
    /// <c>SINGLE</c> selector to its one stat, and a <c>HIGHEST_PCT_BONUS</c> selector throws out of
    /// <see cref="StatSelector.Expand"/> — it is <c>STAT_COPY</c>'s and is only knowable at copy time
    /// (`18` §2.4), so it can never be a stat-op selector.
    /// </remarks>
    private static IReadOnlyList<StatId> CombatStatsOf(EffectDefinition effect)
    {
        if (effect.Stat is not { } selector)
        {
            throw new ArgumentException(
                $"'{effect.Id}' is a {effect.Op} with no 'stat'. Every stat op in 18 §2.1 names one, " +
                "and game-data/schema/effect.schema.json requires it — an effect built in code rather " +
                "than loaded from JSON is outside that enforcement, which is what happened here.",
                nameof(effect));
        }

        return selector.Expand().Where(StatIds.IsCombat).ToArray();
    }

    private static StatId StatAt(int slot) => StatIds.Combat[slot];
}
