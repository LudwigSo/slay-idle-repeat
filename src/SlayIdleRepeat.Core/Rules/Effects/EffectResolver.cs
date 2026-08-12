using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// What one `18` §8 steps 1-2 pass produced.
/// </summary>
/// <param name="Collected">
/// Everything step 1 gathered, in `18` §8's resolution order — before the step-2 gate. Carries each
/// effect's <see cref="CollectedEffect.Source"/> and index, which is what makes the order total.
/// </param>
/// <param name="Active">
/// 🔒 The <b>collected entries</b> that survived step 2, in the same order — each still carrying its
/// <see cref="CollectedEffect.Instance"/>, which is the identity M2-08's <c>TriggerRegistry</c> needs
/// and must not derive. Use <see cref="ResolvedEffects.ActiveDefinitions"/> for the bare list
/// <c>StatAggregation.Aggregate</c> takes.
/// </param>
/// <param name="GatedOut">
/// The ids step 2 removed, in the same order. Reported rather than dropped, on
/// <c>AggregatedStats.SkippedNonCombatStatEffects</c>' precedent: a build whose `PK_EXECUTIONER`
/// vanished because its condition read false is behaving correctly, and a build whose every perk
/// vanished is a bug — and without this the two look identical from outside.
/// </param>
/// <param name="Gate">
/// 🔒 <b>The step-2 gate this pass used</b> — handed back so that the caller gives
/// <c>StatAggregation</c> the <em>same</em> one. See <see cref="EffectResolver"/>'s remarks: `18` §8
/// step 2 is asked twice per pass and the two must not be able to answer differently. Returning it
/// is what makes that structural rather than a convention the next caller can miss.
/// </param>
internal sealed record ResolvedEffects(
    IReadOnlyList<CollectedEffect> Collected,
    IReadOnlyList<CollectedEffect> Active,
    IReadOnlyList<string> GatedOut,
    IEffectConditionGate Gate)
{
    /// <summary>
    /// The step-2 survivors as bare definitions, in `18` §8's resolution order — the argument
    /// <c>StatAggregation.Aggregate</c> takes for steps 3-10.
    /// </summary>
    /// <remarks>
    /// A projection rather than a second stored list: the order is <see cref="Active"/>'s, and two
    /// stored copies of one ordering is two things that can disagree.
    /// </remarks>
    internal IReadOnlyList<EffectDefinition> ActiveDefinitions
    {
        get
        {
            var definitions = new EffectDefinition[Active.Count];

            for (var i = 0; i < Active.Count; i++)
            {
                definitions[i] = Active[i].Effect;
            }

            return definitions;
        }
    }
}

/// <summary>
/// 🔒 `18` §8 <b>steps 1 and 2</b> — <em>"collect all active effects from [ten sources] · filter by
/// condition, evaluated against current state"</em> — and the composition that turns a build into the
/// ordered effect set the rest of §8 consumes.
/// </summary>
/// <remarks>
/// <para>
/// `18` §8 opens <em>"must be implemented exactly, or builds will produce different numbers on client
/// and server"</em> and lists ten steps. <b>Steps 3-10 are M2-07's</b>, in
/// <c>Rules.Stats.StatAggregation</c>, and are not reimplemented here:
/// </para>
/// <code>
/// 1. Collect all active effects from: gear → affixes → set bonuses → talents →
///    pet auras → mount → run buffs → shrine buffs → curses → perks (in draft order)   ← THIS TYPE
/// 2. Filter by condition, evaluated against current state                             ← THIS TYPE
/// 3.-10.  group, STAT_ADD_FLAT, STAT_ADD_PCT, STAT_CONVERT, STAT_MULT, STAT_SET,
///         caps, round                                                                 ← StatAggregation
/// </code>
/// <para>
/// 🔒 <b>The seam between them is a list of effects in a defined order, and nothing else.</b> R17
/// puts <c>Rules.Effects</c> below <c>Rules.Stats</c>, so this type cannot name
/// <c>StatAggregation</c>, <c>ActorStats</c> or <c>StatCaps</c> — and does not need to. The caller
/// (M2-08's simulator, or `05` §9's balance harness) writes
/// <c>StatAggregation.Aggregate(baseStats, resolved.Active, caps, seams)</c>, and
/// <c>EffectResolverTests</c> runs exactly that composition, since the test assembly can see both
/// namespaces.
/// </para>
/// <para>
/// 🔒 <b>R5 — step 1's <em>"(in draft order)"</em> and §8's closing <em>"not draft order"</em> are not
/// in conflict.</b> The ten-source list is the <b>collection</b> order: which effects to gather.
/// Effect-id order is the <b>application</b> order, at steps 6, 7 and 8. This type collects in the
/// document's order and then sorts the whole set through <see cref="EffectResolutionOrder"/>, so the
/// order effects arrive in cannot reach the arithmetic —
/// <c>Resolving_the_same_build_from_two_collection_orders_gives_one_answer</c> is that claim as a
/// test, and <c>StatAggregation</c> re-sorts independently for the same reason.
/// </para>
/// <para>
/// 🔒 <b>The order is <em>total</em>, which `18` §8's own sentence is not.</b> Two effects sharing one
/// id — the same authored affix from two gear slots — are separated by
/// <see cref="EffectResolutionOrder"/>'s documented tiebreak rather than by arrival. See that type for
/// the ruling; it is observable at step 8's <em>"last writer wins"</em> and is the client/server
/// divergence §8 exists to remove.
/// </para>
/// <para>
/// ⚠️ <b>Step 2 is applied to the whole op set here, and again to the stat ops by
/// <c>StatAggregation</c>.</b> That is M2-07's stated design — its method must be correct when called
/// on its own, which the balance harness does — and its remarks require the two evaluations to agree.
/// Both overloads therefore hand the gate back on <see cref="ResolvedEffects.Gate"/>, so the
/// composition is
/// <c>StatAggregationSeams.Strict with { Conditions = resolved.Gate }</c> and the second step 2
/// <em>cannot</em> answer differently from the first. `18` §4's functions are pure, so the repeat
/// evaluation costs the call and nothing else.
/// </para>
/// <para>
/// ⚠️ <b>What step 1 does NOT do: default a trigger, resolve a target, read a value or fire
/// anything.</b> An <c>ON_HIT</c> <c>DAMAGE</c> clause is collected and gated exactly like an
/// <c>ALWAYS</c> <c>STAT_ADD_PCT</c> — `18` §8 step 1 says <em>"all active effects"</em> and the
/// trigger layer (M2-04) is what decides when each fires. <see cref="ActiveOfKind"/> is the one
/// convenience over that, for a caller wanting only §3's <c>ALWAYS</c> passives.
/// </para>
/// </remarks>
internal static class EffectResolver
{
    /// <summary>
    /// 🔒 `18` §8 steps 1 and 2 over a live fight — the ordinary entry point.
    /// </summary>
    /// <param name="sources">The build's `18` §8 step 1 sources.</param>
    /// <param name="context">
    /// The state `18` §4's conditions read. One gate is built over it and used for the whole pass.
    /// </param>
    /// <exception cref="EffectContextException">
    /// A condition could not resolve against the context — see <see cref="EffectConditionGate"/> for
    /// why that is not swallowed.
    /// </exception>
    internal static ResolvedEffects Resolve(EffectSourceSet sources, EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Resolve(sources, new EffectConditionGate(context));
    }

    /// <summary>
    /// 🔒 `18` §8 steps 1 and 2 through an explicit step-2 gate.
    /// </summary>
    /// <param name="sources">The build's `18` §8 step 1 sources.</param>
    /// <param name="gate">
    /// The step-2 filter. 🔒 <b>Hand the same instance to <c>StatAggregation</c></b> — see the type
    /// remarks for why the two step-2s must not be able to disagree.
    /// </param>
    internal static ResolvedEffects Resolve(EffectSourceSet sources, IEffectConditionGate gate)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(gate);

        // ── Step 1 · collect in `18` §8 step 1's ten-source order, then sort — so that the order
        //    the effects arrived in cannot reach anything downstream (R5).
        var collected = EffectResolutionOrder.Sort(sources.Collect());

        var active = new List<CollectedEffect>(collected.Count);
        var gatedOut = new List<string>();

        // ── Step 2 · filter by condition, evaluated against current state.
        foreach (var entry in collected)
        {
            if (gate.IsActive(entry.Effect))
            {
                active.Add(entry);
            }
            else
            {
                gatedOut.Add(entry.Effect.Id);
            }
        }

        return new ResolvedEffects(collected, active, gatedOut, gate);
    }

    /// <summary>
    /// The step-2 survivors whose trigger is the given kind, in `18` §8's resolution order.
    /// </summary>
    /// <remarks>
    /// 🔒 An effect with <b>no</b> trigger counts as <see cref="TriggerKind.ALWAYS"/> — ruling 1, in
    /// <see cref="EffectDefaults"/>, read from `18` §1.1's exhaustive partition of effects into
    /// <c>ALWAYS</c> and triggered. <c>ALWAYS</c> is therefore the kind that answers for §9.1's
    /// <c>CP_GLASS_HEART</c> and §7.6's <em>Avatar of War</em>, which is what makes a `18` §8
    /// aggregation pass see them at all.
    /// </remarks>
    internal static IReadOnlyList<EffectDefinition> ActiveOfKind(ResolvedEffects resolved, TriggerKind kind)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var matching = new List<EffectDefinition>();

        foreach (var entry in resolved.Active)
        {
            if (EffectDefaults.TriggerKindOf(entry.Effect) == kind)
            {
                matching.Add(entry.Effect);
            }
        }

        return matching;
    }
}
