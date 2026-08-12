using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// 🔒 `18` §2.4's <c>STAT_COPY</c> — <b>the one op whose <c>target</c> is not who it writes to.</b>
/// </summary>
/// <remarks>
/// <para>
/// §2.4, verbatim: <em>"Copy <c>value</c> × the copy-source's <b>final resolved</b> stat onto the
/// <b>holder</b> as a percent-bucket add for <c>duration</c>. Reads the start-of-tick snapshot, so
/// mutual copies cannot recurse. <c>stat</c> may be a stat name or <c>HIGHEST_PCT_BONUS</c>
/// (Cogitator's Recalibrate — `17` §7; <c>PK_PACK_LEADER</c> copying the hero's CRIT to pets)."</em>
/// </para>
/// <para>
/// 🔴 <b>THE INVERSION, stated where nobody can miss it.</b> On all forty-two other ops,
/// <c>target</c> names <em>who the op happens to</em>. Here it names the <em>copy source</em> — who
/// the number is read <b>from</b> — and the write lands on the holder. `18` never flags this, which
/// makes it precisely the kind of thing a later reader "fixes". It is not a bug, and
/// <c>StatCopyOpTests.STAT_COPY_reads_the_target_as_the_copy_SOURCE_and_writes_onto_the_HOLDER</c>
/// is named so that changing it turns a rename into a red test with the reason in its own title.
/// </para>
/// <para>
/// The reading is forced by the two authored users, not chosen: <c>PK_PACK_LEADER</c> is <em>"pets
/// gain <b>your</b> crit chance"</em> (`06`), so the pet holds the effect and the hero is the
/// source; Cogitator's <em>Recalibrate</em> copies the hero's best bonus onto the boss. Under the
/// ordinary reading the perk would give the <em>hero</em> the <em>pets'</em> crit, which is zero.
/// </para>
/// <para>
/// 🔒 <b>Why it cannot recurse.</b> The copy-source's stat is read through
/// <see cref="IResolvedStatReader"/>, whose contract is the <em>start-of-tick snapshot</em>. Two
/// actors copying each other therefore both read the same frozen block and neither sees the other's
/// output — pinned by
/// <c>StatCopyOpTests.Two_actors_copying_each_other_both_read_the_start_of_tick_snapshot</c>. The op
/// itself has no way to enforce this, which is exactly why the obligation is on the seam and the
/// test drives the real op through a frozen reader rather than asserting the interface's doc
/// comment.
/// </para>
/// <para>
/// ⚠️ <b>It resolves to a <c>STAT_ADD_PCT</c>, which is how it reaches `18` §8 at all.</b> The op is
/// filed under §2.4 combat-flow, not §2.1, and M2-07's aggregation ignores every non-§2.1 op — so
/// the copy is applied by writing into the percent bucket (<see cref="ICombatFlowSink.AddPercentBucket"/>)
/// and step 5 picks it up on the next aggregation pass.
/// </para>
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

        // 🔴 R13 — these are the copy SOURCES, not the recipients. The recipient is `holder`.
        foreach (var source in OpTargets.Resolve(effect, context))
        {
            var stat = StatOf(selector, source, context);
            var amount = OpRounding.Round(
                factor * context.Seams.Stats.FinalStat(source, stat), effect.Id, $"copied {stat}");

            context.Seams.Flow.AddPercentBucket(holder, stat, amount, effect.Duration, effect.Id);
            written += amount;
        }

        return OpRounding.Round(written, effect.Id, "total copied");
    }

    /// <summary>
    /// Which stat the selector names — one concrete stat, or `18` §2.4's <c>HIGHEST_PCT_BONUS</c>
    /// resolved against the copy source at copy time.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>ALL_COMBAT</c> is refused rather than expanded. `18` §2.4 offers <em>"a stat name or
    /// <c>HIGHEST_PCT_BONUS</c>"</em> and nothing else, the schema's <c>statCopySelector</c>
    /// excludes the group token, and <c>StatSelector.Expand</c> would happily hand back fourteen
    /// stats — which would turn Recalibrate into a copy of the hero's entire stat block.
    /// </remarks>
    private static StatId StatOf(
        StatSelector selector, IEffectActorView source, EffectOpContext context) => selector.Kind switch
    {
        StatSelectorKind.SINGLE => selector.Stat!.Value,

        // 🔒 Read against the SOURCE, not the holder: §2.4 copies "the copy-source's final resolved
        // stat", so which bucket is largest is a fact about the actor being copied.
        StatSelectorKind.HIGHEST_PCT_BONUS => context.Seams.Stats.HighestPercentBonusStat(source),

        _ => throw new EffectContextException(
            nameof(EffectOp.STAT_COPY),
            $"its stat selector is {selector}",
            "18 §2.4 admits 'a stat name or HIGHEST_PCT_BONUS'. ALL_COMBAT would copy fourteen " +
            "stats at once, which no clause authorises, and the schema's statCopySelector does not " +
            "admit it either."),
    };
}
