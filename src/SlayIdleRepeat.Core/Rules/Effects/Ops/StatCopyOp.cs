using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary><c>STAT_COPY</c> — the one op whose <c>target</c> is not who it writes to.</summary>
/// <remarks>
/// <para>
/// Copies <c>value</c> × the copy-source's final resolved stat onto the holder as a percent-bucket
/// add. On every other op, <c>target</c> names who the op happens to; here it names the copy
/// source — who the number is read from — and the write always lands on the holder. This inversion
/// is forced by the authored users, not chosen: a perk that gives pets the hero's crit chance means
/// the pet holds the effect and the hero is the source, so the ordinary reading would give the hero
/// its own pets' (zero) crit instead.
/// </para>
/// <para>
/// Reads the start-of-tick snapshot through <see cref="IResolvedStatReader"/>, so mutual copies
/// can't recurse — two actors copying each other both read the same frozen block and neither sees
/// the other's output.
/// </para>
/// <para>Resolves to a <c>STAT_ADD_PCT</c> write via <see cref="ICombatFlowSink.AddPercentBucket"/>, which is how the copy is picked up on the next aggregation pass.</para>
/// </remarks>
internal static class StatCopyOp
{
    /// <summary>Resolves one <c>STAT_COPY</c>: read from the target, write onto the holder.</summary>
    /// <returns>The total percent-bucket amount written onto the holder.</returns>
    internal static double Resolve(EffectDefinition effect, EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);

        var selector = effect.Stat ?? throw new EffectContextException(
            effect.Id,
            "STAT_COPY names no stat",
            "18 §2.4 copies a named stat or HIGHEST_PCT_BONUS; there is no default, and " +
            "game-data/schema/effect.schema.json requires the key.");

        var factor = OpRounding.Round(context.Seams.Values.ScaledValue(effect), effect.Id, "copy factor");
        var holder = OpTargets.Holder(context);

        var written = 0.0;

        // These are the copy sources, not the recipients — the recipient is `holder`.
        foreach (var source in OpTargets.Resolve(effect, context))
        {
            var stat = StatOf(effect, selector, source, context);
            var amount = OpRounding.Round(
                factor * context.Seams.Stats.FinalStat(source, stat), effect.Id, $"copied {stat}");

            context.Seams.Flow.AddPercentBucket(holder, stat, amount, effect.Duration, effect.Id);
            written += amount;
        }

        return OpRounding.Round(written, effect.Id, "total copied");
    }

    /// <summary>Which stat the selector names — one concrete stat, or <c>HIGHEST_PCT_BONUS</c> resolved against the copy source at copy time.</summary>
    /// <remarks><c>ALL_COMBAT</c> is refused rather than expanded — expanding it would turn a single-stat copy into a copy of the entire stat block.</remarks>
    private static StatId StatOf(
        EffectDefinition effect,
        StatSelector selector,
        IEffectActorView source,
        EffectOpContext context) => selector.Kind switch
    {
        StatSelectorKind.SINGLE => selector.Stat!.Value,

        // Read against the source, not the holder — which bucket is largest is a fact about the
        // actor being copied.
        StatSelectorKind.HIGHEST_PCT_BONUS => context.Seams.Stats.HighestPercentBonusStat(source),

        // The effect id, not the op name, so a test can trace the failure to a content row.
        _ => throw new EffectContextException(
            effect.Id,
            $"its stat selector is {selector}",
            "18 §2.4 admits 'a stat name or HIGHEST_PCT_BONUS'. ALL_COMBAT would copy fourteen " +
            "stats at once, which no clause authorises, and the schema's statCopySelector does not " +
            "admit it either."),
    };
}
