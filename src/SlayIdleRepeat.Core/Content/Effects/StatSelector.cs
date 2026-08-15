namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// What a stat op's <c>stat</c> key names: one <see cref="StatId"/>, the <c>ALL_COMBAT</c> group, or
/// <see cref="EffectOp.STAT_COPY"/>'s <c>HIGHEST_PCT_BONUS</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ALL_COMBAT</c> is a selector, not a stat, and this type is where that ruling lives. A stat
/// has a base value, a cap and a row in aggregation; <c>ALL_COMBAT</c> has none of the three — it
/// names a set of fourteen stats and nothing else. Adding it as a fifteenth <see cref="StatId"/>
/// member would break the actor stat block's fixed-fourteen isomorphism, and every exhaustive
/// switch over stats would need an arm that can never be a real stat.
/// </para>
/// <para>
/// <see cref="EffectOp.STAT_SET"/>, <see cref="EffectOp.STAT_CONVERT"/> and
/// <see cref="EffectOp.STAT_CAP_OVERRIDE"/> require one concrete stat: a worked example proves it,
/// splitting one perk into two effects precisely because the group selector cannot express the
/// second. The schema admits <c>ALL_COMBAT</c> only on the additive/multiplicative stat ops and
/// rejects it everywhere else.
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

    /// <summary><c>ALL_COMBAT</c> — the 14 combat stats.</summary>
    public static StatSelector AllCombat { get; } = new(StatSelectorKind.ALL_COMBAT, null);

    /// <summary><c>HIGHEST_PCT_BONUS</c>, valid only on <see cref="EffectOp.STAT_COPY"/>.</summary>
    public static StatSelector HighestPctBonus { get; } = new(StatSelectorKind.HIGHEST_PCT_BONUS, null);

    /// <summary>One concrete stat.</summary>
    public static StatSelector Of(StatId stat) => new(StatSelectorKind.SINGLE, stat);

    /// <summary>
    /// The stats this selector resolves to, statically.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// For <see cref="StatSelectorKind.HIGHEST_PCT_BONUS"/>, which is only knowable at copy time,
    /// and for a <c>default</c> selector, which names nothing. Both throw rather than returning an
    /// empty list: an effect that silently selected no stats would apply to nothing and report success.
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
