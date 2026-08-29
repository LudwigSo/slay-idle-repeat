using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Cost;

/// <summary>
/// What `14` §8.2's 10 000-triple corpus costs, and the ceiling under which it stays in the unit
/// tier.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>In a namespace of its own, and that is the point.</b> The cross-architecture determinism
/// leg selects its cases by fully qualified name, and every term in that filter — including
/// <c>Determinism</c> — would sweep this case up with the corpus it measures. That leg runs under
/// QEMU emulation, an order of magnitude slower than native, and it reports ANY red case as a
/// floating-point divergence between architectures with fixed-point Q32.32 as the documented
/// escalation. A wall-clock guard measuring a busy local machine has no business being the thing
/// that says two architectures disagree, so it lives where the filter cannot reach it. The leg's own
/// cost is bounded by the job's <c>timeout-minutes</c> instead.
/// </para>
/// <para>
/// It is a guard against an algorithmic regression, not a performance target — the failure it exists
/// to catch is an accidental O(n²), which moves the number by orders of magnitude rather than by a
/// factor of five.
/// </para>
/// </remarks>
public sealed class LogHashCorpusBudgetTests
{
    /// <summary>
    /// The ceiling, in seconds.
    /// </summary>
    /// <remarks>
    /// Two measurements, and the second is why this number is what it is: about <b>10 s</b> when the
    /// corpus is built alone (0.1 s generating, 9.7 s simulating, 0.01 s hashing), and several times
    /// that inside the full unit group, because xUnit runs collections in parallel and six thousand
    /// other cases compete for the same cores. A budget set from the isolated figure goes red on a
    /// busy machine and teaches everyone to re-run the suite, which is worse than no budget at all.
    /// </remarks>
    private const int BudgetSeconds = 300;

    /// <summary>10 000 triples belong to the unit tier, and there is no other tier.</summary>
    [Fact]
    public void The_10000_triple_pass_stays_inside_the_unit_tier_budget()
    {
        var corpus = SimulatedCorpus.Instance;

        corpus.TotalElapsed.TotalSeconds.ShouldBeLessThan(
            BudgetSeconds,
            $"generation {corpus.GenerationElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s, " +
            $"simulation {corpus.SimulationElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s, " +
            $"hashing {corpus.HashingElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s. " +
            "This repository has no integration tier and is not getting one — if 10 000 triples stop " +
            "fitting the unit tier, the answer is to say so, not to add a tier.");
    }
}
