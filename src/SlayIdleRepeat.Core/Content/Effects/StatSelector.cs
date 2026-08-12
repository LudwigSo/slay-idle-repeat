namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// What a stat op's <c>stat</c> key names: one <see cref="StatId"/>, the <c>ALL_COMBAT</c> group, or
/// <see cref="EffectOp.STAT_COPY"/>'s <c>HIGHEST_PCT_BONUS</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b><c>ALL_COMBAT</c> is a selector, not a stat</b>, and this type is where that ruling lives.
/// Three reasons, none of them stylistic:
/// </para>
/// <list type="number">
/// <item>A stat has a base value, a cap and a row in `05` §1.1's aggregation. <c>ALL_COMBAT</c> has
/// none of the three — it names a <em>set</em> of fourteen stats and nothing else.</item>
/// <item>`05` §2 fixes the actor stat block at fourteen and says <em>"an unstated stat is a bug, not
/// a zero"</em>. A fifteenth <see cref="StatId"/> member would break that isomorphism, and every
/// exhaustive <c>switch</c> over stats in M2-03…M2-06 would need an arm that can never be a real
/// stat.</item>
/// <item><see cref="EffectOp.STAT_SET"/>, <see cref="EffectOp.STAT_CONVERT"/> and
/// <see cref="EffectOp.STAT_CAP_OVERRIDE"/> require one concrete stat. `18` §9.1's own ruling proves
/// it: <c>CP_GLASS_HEART</c> is <em>two</em> effects — <c>STAT_MULT ALL_COMBAT ×2</c> and a separate
/// <c>STAT_SET MAX_HP 1</c> — precisely because the group selector cannot express the second.
/// <c>game-data/schema/effect.schema.json</c> therefore admits <c>ALL_COMBAT</c> on
/// <c>STAT_ADD_FLAT</c>/<c>STAT_ADD_PCT</c>/<c>STAT_MULT</c> and rejects it everywhere else.</item>
/// </list>
/// <para>
/// The consequence `18` §9.1 wants falls out of the split: <em>"a pre-agreed downgrade to ×1.6 must
/// be a one-number edit in data, never a code change"</em>. Nothing here knows the number 2.0.
/// </para>
/// </remarks>
public readonly record struct StatSelector
{
    private StatSelector(StatSelectorKind kind, StatId? stat)
    {
        Kind = kind;
        Stat = stat;
    }

    /// <summary>Whether this names one stat, the combat group, or the copy-time highest bucket.</summary>
    public StatSelectorKind Kind { get; }

    /// <summary>The stat, when <see cref="Kind"/> is <see cref="StatSelectorKind.SINGLE"/>; otherwise <c>null</c>.</summary>
    public StatId? Stat { get; }

    /// <summary>`18` §9.1's <c>ALL_COMBAT</c> — the 14 combat stats of `05` §2.</summary>
    public static StatSelector AllCombat { get; } = new(StatSelectorKind.ALL_COMBAT, null);

    /// <summary>`18` §2.4's <c>HIGHEST_PCT_BONUS</c>, valid only on <see cref="EffectOp.STAT_COPY"/>.</summary>
    public static StatSelector HighestPctBonus { get; } = new(StatSelectorKind.HIGHEST_PCT_BONUS, null);

    /// <summary>One concrete stat.</summary>
    public static StatSelector Of(StatId stat) => new(StatSelectorKind.SINGLE, stat);

    /// <summary>
    /// The stats this selector resolves to, statically.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// For <see cref="StatSelectorKind.HIGHEST_PCT_BONUS"/>, which is only knowable at copy time,
    /// and for a <c>default</c> selector, which names nothing. 🔒 Both throw rather than returning an
    /// empty list: an effect that silently selected no stats would apply to nothing and report
    /// success, which is the failure mode `game-data/README.md` calls manufacturing confidence.
    /// </exception>
    public IReadOnlyList<StatId> Expand() => Kind switch
    {
        StatSelectorKind.SINGLE => new[] { Stat!.Value },
        StatSelectorKind.ALL_COMBAT => StatIds.Combat,
        StatSelectorKind.HIGHEST_PCT_BONUS => throw new InvalidOperationException(
            "HIGHEST_PCT_BONUS names whichever stat carries the largest percent bucket at copy time " +
            "(18 §2.4), so it cannot be expanded before evaluation. STAT_COPY's resolver reads " +
            "Kind and picks the stat itself."),
        _ => throw new InvalidOperationException(
            "a default StatSelector names no stat. Build one with StatSelector.Of, " +
            "StatSelector.AllCombat or StatSelector.HighestPctBonus."),
    };

    /// <summary>The DSL token this selector is written as in JSON.</summary>
    public override string ToString() => Kind switch
    {
        StatSelectorKind.SINGLE => Stat!.Value.ToString(),
        StatSelectorKind.ALL_COMBAT => nameof(StatSelectorKind.ALL_COMBAT),
        StatSelectorKind.HIGHEST_PCT_BONUS => nameof(StatSelectorKind.HIGHEST_PCT_BONUS),
        _ => "(unset)",
    };
}
