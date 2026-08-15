namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The five stacking modes.</summary>
public enum StackingMode
{
    /// <summary>Stacks sum.</summary>
    ADDITIVE = 1,

    /// <summary>Stacks multiply — a built-in enrage status's mode.</summary>
    MULTIPLICATIVE = 2,

    /// <summary>A new application replaces the existing one.</summary>
    REPLACE = 3,

    /// <summary>The strongest application wins.</summary>
    HIGHEST_WINS = 4,

    /// <summary>A second application is ignored.</summary>
    NONE = 5,
}
