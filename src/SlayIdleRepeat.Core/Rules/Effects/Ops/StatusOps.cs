using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>The six status ops, over the twelve statuses.</summary>
/// <remarks>
/// <para>
/// The six split three ways: three name a status (<c>APPLY_STATUS</c>, <c>EXTEND_STATUS</c>,
/// <c>IMMUNE_STATUS</c>); one names either a status or a tag group (<c>REMOVE_STATUS</c>); two name
/// none and scale statuses in bulk (<c>STATUS_POWER_PCT</c>, <c>STATUS_DURATION_PCT</c>).
/// </para>
/// <para>
/// The last two point in opposite directions: <c>STATUS_POWER_PCT</c> scales potency of statuses
/// this actor applies (outgoing), <c>STATUS_DURATION_PCT</c> scales duration of statuses applied to
/// this actor (incoming). They call two differently named seam members rather than one with a
/// direction flag, so a mixed-up direction fails loudly rather than silently.
/// </para>
/// </remarks>
internal static class StatusOps
{
    /// <summary><c>APPLY_STATUS</c>. The value is the status's own potency.</summary>
    /// <remarks>
    /// The potency is not passed through <see cref="OpValue"/>: each status types its own potency in
    /// its own unit, so the status decides what its number means rather than a value mode
    /// reinterpreting it. <c>valueScale</c> still applies, since that scales the authored number
    /// rather than its meaning.
    /// </remarks>
    internal static double Apply(EffectDefinition effect, EffectOpContext context)
    {
        var statusId = RequireStatusId(effect);
        var potency = Potency(effect, context, statusId);

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            // The holder travels with the application — some statuses are typed against the
            // applier's own stats (e.g. a % of the applier's ATK), not the target's.
            context.Seams.Statuses.Apply(
                context.Holder, target, statusId, potency, effect.Duration, effect.Stacking, effect.Id);
        }

        return potency;
    }

    /// <summary>The effect's potency, or the sentinel <c>0.0</c> when the named status supplies its own fixed potency.</summary>
    /// <remarks>
    /// A value-less effect is normally an authoring hole and refused elsewhere; this narrows that
    /// only for a value-less <c>APPLY_STATUS</c> whose status answers yes to
    /// <see cref="IStatusEngine.HasFixedPotency"/> — asked through a seam since the op layer can't
    /// reach the status catalogue directly. The <c>0.0</c> is a sentinel, never read downstream when
    /// a fixed potency is set.
    /// </remarks>
    private static double Potency(EffectDefinition effect, EffectOpContext context, string statusId)
    {
        if (effect.Value is null && context.Seams.Statuses.HasFixedPotency(statusId))
        {
            return 0.0;
        }

        return OpRounding.Round(context.Seams.Values.ScaledValue(effect), effect.Id, "potency");
    }

    /// <summary><c>REMOVE_STATUS</c>: clear a status or a tag group. Exactly one of the two.</summary>
    /// <remarks>
    /// The tag group is a <see cref="StatusTag"/>, a different type from the effect's own author
    /// <see cref="AuthorTag"/>s, so the two can't be swapped by accident — which matters because the
    /// ward-bypass rule keys on the reserved <c>drawback</c> author tag.
    /// </remarks>
    internal static double Remove(EffectDefinition effect, EffectOpContext context)
    {
        var statusId = effect.StatusId;
        var tag = EffectTagging.StatusTagOf(effect);

        if ((statusId is null) == (tag is null))
        {
            throw new EffectContextException(
                effect.Id,
                statusId is null
                    ? "REMOVE_STATUS names neither a statusId nor a statusTag"
                    : "REMOVE_STATUS names both a statusId and a statusTag",
                "18 §2.3 offers one or the other — 'clear a status or a tag group'. Neither is an " +
                "effect that removes nothing and reports success; both is two different removals in " +
                "one effect, and 18 authors no precedence between them.");
        }

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            if (statusId is not null)
            {
                context.Seams.Statuses.Remove(target, statusId, effect.Id);
            }
            else if (tag is { } group)
            {
                context.Seams.Statuses.RemoveByTag(target, group, effect.Id);
            }
        }

        return 0.0;
    }

    /// <summary><c>EXTEND_STATUS</c>: add duration to an existing status.</summary>
    /// <remarks>
    /// The seconds come from <c>value</c>, not <c>duration.seconds</c> — <c>duration</c> means how
    /// long the effect itself lasts on every other op, so reusing it here would give one key two
    /// meanings.
    /// </remarks>
    internal static double Extend(EffectDefinition effect, EffectOpContext context)
    {
        var statusId = RequireStatusId(effect);
        var seconds = OpRounding.Round(
            context.Seams.Values.ScaledValue(effect), effect.Id, "extension in seconds");

        if (seconds <= 0.0)
        {
            throw new EffectContextException(
                effect.Id,
                $"EXTEND_STATUS adds {seconds} seconds",
                "18 §2.3 'adds duration'. A zero or negative extension is a shortening or a no-op " +
                "wearing the name of an extension, and no clause of 18 authorises either.");
        }

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            context.Seams.Statuses.Extend(target, statusId, seconds, effect.Id);
        }

        return seconds;
    }

    /// <summary><c>IMMUNE_STATUS</c>: immunity to one status for a duration.</summary>
    internal static double GrantImmunity(EffectDefinition effect, EffectOpContext context)
    {
        var statusId = RequireStatusId(effect);

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            context.Seams.Statuses.GrantImmunity(target, statusId, effect.Duration, effect.Id);
        }

        return 0.0;
    }

    /// <summary><c>STATUS_POWER_PCT</c>: scales the potency of statuses the actor applies.</summary>
    internal static double ScaleOutgoingPower(EffectDefinition effect, EffectOpContext context) =>
        ScaleBulk(effect, context, outgoingPower: true);

    /// <summary><c>STATUS_DURATION_PCT</c>: scales the duration of statuses applied to the actor.</summary>
    internal static double ScaleIncomingDuration(EffectDefinition effect, EffectOpContext context) =>
        ScaleBulk(effect, context, outgoingPower: false);

    private static double ScaleBulk(EffectDefinition effect, EffectOpContext context, bool outgoingPower)
    {
        var fraction = OpRounding.Round(
            context.Seams.Values.ScaledValue(effect), effect.Id, "status scaling fraction");

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            if (outgoingPower)
            {
                context.Seams.Statuses.ScaleOutgoingPower(target, fraction, effect.Duration, effect.Id);
            }
            else
            {
                context.Seams.Statuses.ScaleIncomingDuration(target, fraction, effect.Duration, effect.Id);
            }
        }

        return fraction;
    }

    private static string RequireStatusId(EffectDefinition effect) =>
        effect.StatusId ?? throw new EffectContextException(
            effect.Id,
            $"{effect.Op} names no statusId",
            "18 §2.3 gives APPLY_STATUS, EXTEND_STATUS and IMMUNE_STATUS one of 05 §5's twelve " +
            "statuses to act on, and game-data/schema/effect.schema.json requires the key. An " +
            "effect built in code rather than loaded from JSON is outside that enforcement, which " +
            "is what happened here.");
}
