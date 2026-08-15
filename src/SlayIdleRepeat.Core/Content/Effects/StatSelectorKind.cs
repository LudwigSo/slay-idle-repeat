namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>What the <c>stat</c> key of a stat op names.</summary>
public enum StatSelectorKind
{
    /// <summary>One concrete <see cref="StatId"/>.</summary>
    SINGLE = 1,

    /// <summary>
    /// <c>ALL_COMBAT</c> — the 14 combat stats as a group, e.g.
    /// <c>{"op":"STAT_MULT","stat":"ALL_COMBAT","value":2.0}</c>.
    /// </summary>
    ALL_COMBAT = 2,

    /// <summary>
    /// <c>HIGHEST_PCT_BONUS</c> — valid only on <see cref="EffectOp.STAT_COPY"/>, where it names
    /// whichever stat carries the largest percent bucket at copy time. Which stat that is cannot be
    /// known before evaluation.
    /// </summary>
    HIGHEST_PCT_BONUS = 3,
}
