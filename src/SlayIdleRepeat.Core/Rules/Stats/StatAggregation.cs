using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// The result of one stat aggregation pass.
/// </summary>
/// <param name="Final">The capped, rounded 14-stat block.</param>
/// <param name="PostMultiplierMaxHp">
/// Max HP as it stood after the <c>STAT_MULT</c> step: post-multiplier, pre-<c>STAT_SET</c>.
/// <para>
/// The ward pool cap is <c>wardCapPct × this value</c>, not <c>× Final.MaxHp</c> — a shield-rebase
/// perk (<c>STAT_MULT ALL_COMBAT ×2</c> plus <c>STAT_SET MAX_HP 1</c>) relies on <c>MAX_HP</c> being
/// set after multipliers so the ×2 never applies to it; reading <see cref="Final"/> instead would cap
/// every shield on that build at 1 HP.
/// </para>
/// <para>
/// Callers must hold the whole <see cref="AggregatedStats"/> as actor state, not just
/// <see cref="Final"/> — discarding this member silently breaks that perk with nothing going red —
/// and must re-read it every re-aggregation rather than caching it, since a boss's post-multiplier
/// Max HP can change mid-fight (an enrage adding a <c>STAT_MULT</c> over time).
/// </para>
/// </param>
/// <param name="SkippedNonCombatStatEffects">
/// The ids of stat-op effects naming a non-combat stat (e.g. <c>GOLD_PCT</c>). Authored, legitimate,
/// and simply not this pipeline's subject. Reported rather than dropped, so a caller that hands the
/// whole build here and never checks what was left behind doesn't silently lose an affix.
/// </param>
/// <remarks>
/// Record equality compares <see cref="SkippedNonCombatStatEffects"/> by reference, as for any
/// collection member of a record — two results of the same aggregation are equal only when they
/// happen to share the list instance, unlike <see cref="Final"/> and
/// <see cref="PostMultiplierMaxHp"/>, which compare by value.
/// </remarks>
/// <param name="HealCeilingFraction">
/// The fraction of Max HP above which this actor cannot be healed, or <c>null</c> when no override
/// is active. Rides on the aggregate rather than a stat slot because it isn't a stat and has no
/// other vehicle for crossing into the attack pipeline that bounds healing with it; being a member
/// here also means it's re-read every pass like every other member.
/// </param>
internal sealed record AggregatedStats(
    ActorStats Final,
    double PostMultiplierMaxHp,
    IReadOnlyList<string> SkippedNonCombatStatEffects,
    double? HealCeilingFraction = null);

/// <summary>
/// The stat aggregation order — must be implemented exactly, or builds produce different numbers
/// on client and server.
/// </summary>
/// <remarks>
/// <para>
/// The ten steps:
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
/// <c>STAT_MULT</c> is <c>× Π(value)</c>, not <c>× Π(1 + value)</c> — a corrected erratum. Under the
/// <c>(1 + v)</c> reading, an authored <c>×2 all stats</c> effect would actually apply ×3, and a
/// boss's authored "+8% ATK per second" enrage would compound to ×2.08 per second and one-shot the
/// hero within a few seconds. <c>StatAggregationTests</c> pins both readings apart with literal
/// numbers. The additive percent bucket keeps the <c>(1 + Σ PctAdd)</c> form — only the
/// multiplicative term is corrected.
/// </para>
/// <para>
/// Step 1 (collection) is the caller's, and its order is immaterial: this method sorts its whole
/// input ordinally once, before any step reads it, so the arrival order can't reach the arithmetic.
/// That sort is stable, so effects sharing an id (which shouldn't happen within one authored
/// catalogue, but isn't rejected here) keep their arrival order — left to the collector to resolve.
/// Steps 4 and 5's sums are also done in that same ordinal order, though the spec only states an
/// order for steps 6-8: floating-point addition is not associative, so an unordered "sum" would not
/// be a deterministic one.
/// </para>
/// <para>
/// Ops outside the six stat ops are ignored before the step-2 condition gate is consulted, since
/// nothing else changes a stat and a conditional non-stat clause is not this pipeline's to refuse.
/// Step 2 is re-applied here even though a caller's resolver may already have filtered, so this
/// method is correct when called on its own (e.g. by the balance harness) — a caller that has
/// already filtered may pass an always-true gate, but must not pass one that answers differently.
/// </para>
/// </remarks>
internal static class StatAggregation
{
    /// <summary>
    /// Runs the aggregation's steps 2 through 10 over an already-collected effect list.
    /// </summary>
    /// <param name="baseStats">
    /// <c>Base(stat)</c> — the hero curve, or the enemy derivation.
    /// </param>
    /// <param name="effects">
    /// The effects step 1 collected, in any order. Sorted ordinally here before anything reads them.
    /// </param>
    /// <param name="caps">The stat ceilings, before <c>STAT_CAP_OVERRIDE</c>.</param>
    /// <param name="seams">
    /// The parts of the pipeline that belong to other subsystems. Use
    /// <see cref="StatAggregationSeams.Strict"/> for the strict default.
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

        // Step 2 · filter by condition. Asked only of the six stat ops — steps 4-9 read nothing
        // else, so refusing a non-stat conditional effect here would be out of scope and would make
        // the strict seam unusable against any real build.
        var active = new List<EffectDefinition>(effects.Count);
        foreach (var effect in effects)
        {
            ArgumentNullException.ThrowIfNull(effect, nameof(effects));

            if (effect.Family == EffectOpFamily.STAT && seams.Conditions.IsActive(effect))
            {
                active.Add(effect);
            }
        }

        // Steps 3-8's ordering, established once: grouped by op, in effect-id order.
        var byOp = active.InEffectIdOrder().ToLookup(e => e.Op);

        var skipped = new List<string>();
        var values = baseStats.ToSlots();

        // Step 4 · STAT_ADD_FLAT (sum), then round.
        var flat = Sum(byOp[EffectOp.STAT_ADD_FLAT], seams.Values, skipped);
        for (var slot = 0; slot < values.Length; slot++)
        {
            values[slot] += flat[slot];
        }

        RoundAll(values, "step 4 (STAT_ADD_FLAT)");

        // Step 5 · STAT_ADD_PCT (sum, then multiply base once), then round.
        var percent = Sum(byOp[EffectOp.STAT_ADD_PCT], seams.Values, skipped);
        for (var slot = 0; slot < values.Length; slot++)
        {
            values[slot] *= 1.0 + percent[slot];
        }

        RoundAll(values, "step 5 (STAT_ADD_PCT)");

        // Step 6 · STAT_CONVERT, reading post-step-5 values, in effect-id order. The block handed to
        // the seam is frozen, so "reads post-step-5 values" is enforced rather than trusted.
        var postAdditive = ActorStats.FromSlots(values);
        foreach (var delta in seams.Ops.Convert(byOp[EffectOp.STAT_CONVERT].ToArray(), postAdditive, seams.Values))
        {
            values[ActorStats.SlotOf(delta.Stat)] += delta.Amount;
        }

        RoundAll(values, "step 6 (STAT_CONVERT)");

        // Step 7 · STAT_MULT (product of the values, in effect-id order), then round. See the type
        // remarks for why it's not (1 + value).
        foreach (var (effect, stats) in Targets(byOp[EffectOp.STAT_MULT], skipped))
        {
            var factor = seams.Values.EffectiveValue(effect);
            foreach (var stat in stats)
            {
                values[ActorStats.SlotOf(stat)] *= factor;
            }
        }

        RoundAll(values, "step 7 (STAT_MULT)");

        // The ward cap reads Max HP here — post-multiplier, pre-STAT_SET. See AggregatedStats.
        var postMultiplierMaxHp = values[ActorStats.SlotOf(StatId.MAX_HP)];

        // Step 8 · STAT_SET (last writer wins, in effect-id order), then round.
        foreach (var (effect, stats) in Targets(byOp[EffectOp.STAT_SET], skipped))
        {
            var value = seams.Values.EffectiveValue(effect);
            foreach (var stat in stats)
            {
                values[ActorStats.SlotOf(stat)] = value;
            }
        }

        RoundAll(values, "step 8 (STAT_SET)");

        // Step 9 · caps, honouring STAT_CAP_OVERRIDE, then round. Two halves: the override can
        // raise a cap (a change to the table below) or redirect it (move value from one stat to
        // another, e.g. crit above the cap converting to crit damage), which can't be expressed as
        // a cap at all.
        var overrides = byOp[EffectOp.STAT_CAP_OVERRIDE].ToArray();
        var effective = seams.Ops.OverrideCaps(overrides, caps, seams.Values);

        // Frozen before the caps land — this is the only place the overshoot still exists, and
        // freezing it stops one redirect from reading another's output.
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

            // Re-applied: a redirect can carry its destination past that stat's own ceiling, which
            // would otherwise leave an uncapped stat. Idempotent for every stat no redirect touched.
            for (var slot = 0; slot < values.Length; slot++)
            {
                values[slot] = effective.Apply(StatAt(slot), values[slot]);
            }
        }

        RoundAll(values, "step 9 (caps)");

        // Step 10 · round every resulting stat to 4 decimal places. Idempotent after the per-step
        // rounding above; stated anyway so a future step inserted before it doesn't have to remember.
        RoundAll(values, "step 10 (final rounding)");

        // HEAL_CEILING, read off the same overrides array step 9 already gathered. Not a step — it
        // changes no stat, it bounds Heal() elsewhere — which is why it rides on the record instead
        // of a slot.
        var healCeiling = overrides.Length == 0
            ? null
            : seams.Ops.HealCeilingFraction(overrides, seams.Values);

        return new AggregatedStats(
            ActorStats.FromSlots(values), postMultiplierMaxHp, skipped, healCeiling);
    }

    /// <summary>Rounding at one aggregation step boundary.</summary>
    private static void RoundAll(double[] values, string step)
    {
        for (var slot = 0; slot < values.Length; slot++)
        {
            values[slot] = StatRounding.Round(values[slot], StatAt(slot), step);
        }
    }

    /// <summary>An additive step's grouping: one sum per stat, accumulated in effect-id order.</summary>
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
    /// The stats are resolved before <see cref="IEffectValueReader.EffectiveValue"/> is asked for a
    /// value, so an effect naming no combat stat never reaches the reader at all — a "+X% Gold Gain"
    /// gear affix carrying a <c>valueScale</c> is legitimate authored content this pipeline doesn't
    /// touch, and asking the strict reader to value it would throw for a stat the actor block
    /// doesn't hold.
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
    /// non-combat stats.
    /// </summary>
    /// <remarks>
    /// <c>ALL_COMBAT</c> expands to the fourteen (see <see cref="StatSelector"/>), a <c>SINGLE</c>
    /// selector to its one stat, and a <c>HIGHEST_PCT_BONUS</c> selector throws out of
    /// <see cref="StatSelector.Expand"/> — it's only knowable at copy time, so it can never be a
    /// stat-op selector.
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
