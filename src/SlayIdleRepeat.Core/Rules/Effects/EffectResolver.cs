using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// What one collect-and-filter pass produced.
/// </summary>
/// <param name="Collected">Everything collected, in resolution order — before the condition gate.</param>
/// <param name="Active">
/// The collected entries that survived the gate, in the same order, each still carrying the
/// identity <c>TriggerRegistry</c> needs. Use <see cref="ResolvedEffects.ActiveDefinitions"/> for the
/// bare list <c>StatAggregation.Aggregate</c> takes.
/// </param>
/// <param name="GatedOut">
/// The ids the gate removed, in the same order. Reported rather than dropped, so a build whose one
/// perk vanished (condition read false) reads as correct and a build whose every perk vanished reads
/// as a bug — without this the two look identical from outside.
/// </param>
/// <param name="Gate">
/// The gate this pass used — handed back so the caller gives <c>StatAggregation</c> the same
/// instance, since the gate is applied twice per pass and the two must not be able to disagree.
/// </param>
internal sealed record ResolvedEffects(
    IReadOnlyList<CollectedEffect> Collected,
    IReadOnlyList<CollectedEffect> Active,
    IReadOnlyList<string> GatedOut,
    IEffectConditionGate Gate)
{
    /// <summary>The survivors as bare definitions, in resolution order — what <c>StatAggregation.Aggregate</c> takes.</summary>
    /// <remarks>A projection rather than a second stored list, so there's only one ordering to keep in sync.</remarks>
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
/// Collects effects from a build's sources and filters them by condition, producing the ordered set
/// the stat aggregation pass consumes.
/// </summary>
/// <remarks>
/// <para>
/// The seam between this type and <c>Rules.Stats.StatAggregation</c> is a list of effects in a
/// defined order, nothing else — the layering keeps <c>Rules.Effects</c> from naming
/// <c>StatAggregation</c> directly.
/// </para>
/// <para>
/// Collection order (the source list) and application order (effect-id order) are different things:
/// this collects in source order, then sorts the whole set through <see cref="EffectResolutionOrder"/>,
/// so the order effects arrive in can't reach the arithmetic downstream.
/// </para>
/// <para>
/// The condition gate is applied to the whole set here, and again to just the stat ops by
/// <c>StatAggregation</c> — both must agree, so both hand the gate back on
/// <see cref="ResolvedEffects.Gate"/> and the caller passes the same instance through.
/// </para>
/// <para>
/// Collection does not default a trigger, resolve a target, read a value or fire anything — a
/// triggered effect is collected and gated exactly like an always-on one; the trigger layer decides
/// when each fires. <see cref="ActiveOfKind"/> is a convenience for callers wanting only the passives.
/// </para>
/// </remarks>
internal static class EffectResolver
{
    /// <summary>Collects and filters over a live fight — the ordinary entry point.</summary>
    /// <param name="sources">The build's effect sources.</param>
    /// <param name="context">The state conditions read. One gate is built over it and used for the whole pass.</param>
    /// <exception cref="EffectContextException">A condition could not resolve against the context.</exception>
    internal static ResolvedEffects Resolve(EffectSourceSet sources, EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Resolve(sources, new EffectConditionGate(context));
    }

    /// <summary>Collects and filters through an explicit condition gate.</summary>
    /// <param name="sources">The build's effect sources.</param>
    /// <param name="gate">The filter. Hand the same instance to <c>StatAggregation</c> afterward.</param>
    internal static ResolvedEffects Resolve(EffectSourceSet sources, IEffectConditionGate gate)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(gate);

        // Collect in source order, then sort — so arrival order can't reach anything downstream.
        var collected = EffectResolutionOrder.Sort(sources.Collect());

        var active = new List<CollectedEffect>(collected.Count);
        var gatedOut = new List<string>();

        // Filter by condition, evaluated against current state.
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

    /// <summary>The survivors whose trigger is the given kind, in resolution order.</summary>
    /// <remarks>An effect with no trigger counts as <see cref="TriggerKind.ALWAYS"/> (see <see cref="EffectDefaults"/>).</remarks>
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
