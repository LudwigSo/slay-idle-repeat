namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>What the <c>stat</c> key of a `18` §2.1 / §2.4 stat op names.</summary>
public enum StatSelectorKind
{
    /// <summary>One concrete <see cref="StatId"/>.</summary>
    SINGLE = 1,

    /// <summary>
    /// `18` §9.1's <c>ALL_COMBAT</c> — the 14 combat stats of `05` §2 as a group.
    /// <c>CP_GLASS_HEART</c>'s <c>{"op":"STAT_MULT","stat":"ALL_COMBAT","value":2.0}</c>.
    /// </summary>
    ALL_COMBAT = 2,

    /// <summary>
    /// `18` §2.4's <c>HIGHEST_PCT_BONUS</c> — valid only on <see cref="EffectOp.STAT_COPY"/>, where
    /// it names whichever stat carries the largest percent bucket at copy time (Cogitator's
    /// Recalibrate, `17` §7). Which stat that is cannot be known before evaluation.
    /// </summary>
    HIGHEST_PCT_BONUS = 3,
}
