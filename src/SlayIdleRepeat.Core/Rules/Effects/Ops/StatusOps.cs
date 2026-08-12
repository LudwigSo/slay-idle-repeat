using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// 🔒 `18` §2.3's six status ops, over `05` §5's twelve statuses.
/// </summary>
/// <remarks>
/// <para>
/// The six split three ways, and the split is what stops them being written as one method with a
/// flag: three <b>name</b> a status (<c>APPLY_STATUS</c>, <c>EXTEND_STATUS</c>,
/// <c>IMMUNE_STATUS</c> — the schema requires <c>statusId</c> on all three); one names <b>either</b>
/// a status or a tag group (<c>REMOVE_STATUS</c>); and two name <b>none</b> and scale statuses in
/// bulk (<c>STATUS_POWER_PCT</c>, <c>STATUS_DURATION_PCT</c>).
/// </para>
/// <para>
/// 🔒 <b>The last two point in opposite directions, and §2.3's two rows are the only place that is
/// said.</b> <c>STATUS_POWER_PCT</c> scales <em>"the potency of statuses <b>this actor
/// applies</b>"</em> — outgoing — while <c>STATUS_DURATION_PCT</c> scales <em>"duration of statuses
/// <b>applied to</b> this actor"</em> — incoming. Reading them as one direction is a silent balance
/// bug in whichever half is wrong, so they call two differently named seam members rather than one
/// with a parameter.
/// </para>
/// <para>
/// ⚠️ <b><c>statusId</c> is not validated against `05` §5's twelve here.</b> The schema enumerates
/// them (<c>$defs/statusId</c>) and M2-10's catalogue is the type that will hold them; a third copy
/// of the list in this file would be a set that must agree with two others with nothing making it.
/// </para>
/// </remarks>
internal static class StatusOps
{
    /// <summary>`18` §2.3 — <c>APPLY_STATUS</c>. The value is the status's own potency (its X in `05` §5).</summary>
    /// <remarks>
    /// The potency is <b>not</b> passed through <see cref="OpValue"/>: `05` §5 types every X as the
    /// status's own unit — <em>"X% of attacker ATK per second"</em>, <em>"−X% ASPD"</em>,
    /// <em>"−X% DEF"</em> — so the status decides what its number means and a value mode here would
    /// be a second, disagreeing answer. `18` §1.1's <c>valueScale</c> still applies, because that
    /// scales the authored number rather than reinterpreting it.
    /// </remarks>
    internal static double Apply(EffectDefinition effect, EffectOpContext context)
    {
        var statusId = RequireStatusId(effect);
        var potency = OpRounding.Round(context.Seams.Values.ScaledValue(effect), effect.Id, "potency");

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            context.Seams.Statuses.Apply(
                target, statusId, potency, effect.Duration, effect.Stacking, effect.Id);
        }

        return potency;
    }

    /// <summary>
    /// 🔒 `18` §2.3 — <c>REMOVE_STATUS</c>: <em>"clear a status or a tag group"</em>. Exactly one of
    /// the two.
    /// </summary>
    /// <remarks>
    /// R12 — the tag group is a <see cref="StatusTag"/>, which is a different type from the
    /// <see cref="AuthorTag"/>s in the effect's own <c>tags</c> array. The two cannot be swapped by
    /// accident, which matters because `05` §4.1's ward bypass keys on the reserved
    /// <c>drawback</c> author tag: a <c>REMOVE_STATUS</c> that could take one would clear the marker.
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
            else
            {
                context.Seams.Statuses.RemoveByTag(target, tag!.Value, effect.Id);
            }
        }

        return 0.0;
    }

    /// <summary>`18` §2.3 — <c>EXTEND_STATUS</c>: <em>"add duration to an existing status"</em>.</summary>
    /// <remarks>
    /// ⚠️ <b>The seconds come from <c>value</c>, not from <c>duration.seconds</c>, and that is a
    /// ruling.</b> `18` §2.3 names no key at all. <c>duration</c> means <em>how long the effect
    /// lasts</em> on all forty-two other ops (§6), so reading the extension out of it would give the
    /// same key two meanings; <c>value</c> is the op's free magnitude and is unused otherwise.
    /// Recorded as errata against §2.3.
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

    /// <summary>`18` §2.3 — <c>IMMUNE_STATUS</c>: immunity to one status for a duration.</summary>
    internal static double GrantImmunity(EffectDefinition effect, EffectOpContext context)
    {
        var statusId = RequireStatusId(effect);

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            context.Seams.Statuses.GrantImmunity(target, statusId, effect.Duration, effect.Id);
        }

        return 0.0;
    }

    /// <summary>`18` §2.3 — <c>STATUS_POWER_PCT</c>: scales the potency of statuses the actor <b>applies</b>.</summary>
    internal static double ScaleOutgoingPower(EffectDefinition effect, EffectOpContext context) =>
        ScaleBulk(effect, context, outgoingPower: true);

    /// <summary>`18` §2.3 — <c>STATUS_DURATION_PCT</c>: scales the duration of statuses <b>applied to</b> the actor.</summary>
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
