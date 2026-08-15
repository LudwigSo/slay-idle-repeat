namespace SlayIdleRepeat.BalanceHarness.Model;

/// <summary>
/// The one restatement of <c>SlayIdleRepeat.Core.Primitives.DeterminismRounding.Round</c> inside
/// <c>tools/BalanceHarness</c> — that method is <c>internal</c> to <c>Core</c> and unreachable here.
/// </summary>
/// <remarks>
/// All three parts matter: 4 dp is the accumulation-point rounding; <see cref="MidpointRounding.ToEven"/>
/// is written out because it's <c>Math.Round</c>'s default and a reader shouldn't have to know that;
/// and the trailing <c>+ 0.0</c> normalises <c>-0.0</c> to <c>+0.0</c>, because <c>ActorStats.From</c>
/// rejects a negative zero and a scaled stat can drift a hair below zero. If the source's decimal
/// count ever moves, this must move with it — the duplication is intentional and documented there.
/// </remarks>
public static class HarnessRounding
{
    /// <summary>The number of decimal places every accumulated value carries.</summary>
    public const int Decimals = 4;

    /// <summary>Rounds one value the way every <c>double</c> that reaches <c>Core</c> must be rounded.</summary>
    public static double Round(double value) =>
        Math.Round(value, Decimals, MidpointRounding.ToEven) + 0.0;
}
