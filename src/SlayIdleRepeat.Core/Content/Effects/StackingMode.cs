namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>🔒 The five stacking modes of `18` §6.</summary>
public enum StackingMode
{
    /// <summary>Stacks sum.</summary>
    ADDITIVE = 1,

    /// <summary>Stacks multiply — the enrage's mode (`05` §3.1's <c>SYS_ENRAGE</c>).</summary>
    MULTIPLICATIVE = 2,

    /// <summary>A new application replaces the existing one.</summary>
    REPLACE = 3,

    /// <summary>The strongest application wins.</summary>
    HIGHEST_WINS = 4,

    /// <summary>A second application is ignored.</summary>
    NONE = 5,
}
