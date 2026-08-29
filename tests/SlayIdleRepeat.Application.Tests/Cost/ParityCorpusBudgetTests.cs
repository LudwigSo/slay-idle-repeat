using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Application.Tests.Parity;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Cost;

/// <summary>
/// What `14` §13's 1 000-sequence parity corpus costs, and the ceiling under which it stays in the
/// unit tier.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>In a namespace of its own, and that is the point.</b> The cross-architecture determinism
/// leg selects its cases by fully qualified name, and <c>Parity</c> is one of its terms — so this
/// case would be swept up with the corpus it measures. That leg runs under QEMU emulation, an order
/// of magnitude slower than native, and it reports ANY red case as a floating-point divergence
/// between architectures with fixed-point Q32.32 as the documented escalation. A wall-clock guard
/// measuring a busy local machine has no business being the thing that says two architectures
/// disagree, so it lives where the filter cannot reach it. The leg's own cost is bounded by the
/// job's <c>timeout-minutes</c> instead.
/// </para>
/// <para>
/// It is a guard against an algorithmic regression, not a performance target — the failure it exists
/// to catch is an accidental O(n²), which moves the number by orders of magnitude rather than by a
/// factor of five.
/// </para>
/// </remarks>
public sealed class ParityCorpusBudgetTests
{
    /// <summary>
    /// The ceiling, in seconds.
    /// </summary>
    /// <remarks>
    /// Two measurements, and the second is why this number is what it is: about <b>9 s</b> when the
    /// corpus is built alone, and <b>48 s</b> inside the full unit group, because xUnit runs
    /// collections in parallel and 1 500 other cases compete for the same cores. A budget set from
    /// the isolated figure goes red on a busy machine and teaches everyone to re-run the suite, which
    /// is worse than no budget at all.
    /// </remarks>
    private const int BudgetSeconds = 300;

    /// <summary>1 000 sequences through two pipelines belong to the unit tier, and there is no other tier.</summary>
    [Fact]
    public void The_1000_sequence_pass_stays_inside_the_unit_tier_budget()
    {
        var corpus = ParityCorpus.Instance;

        corpus.TotalElapsed.TotalSeconds.ShouldBeLessThan(
            BudgetSeconds,
            $"generation {corpus.GenerationElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s, " +
            $"comparison {corpus.ComparisonElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s. " +
            "This repository has no integration tier and is not getting one — if 1 000 sequences stop " +
            "fitting the unit tier, the answer is to say so, not to add a tier.");
    }
}
