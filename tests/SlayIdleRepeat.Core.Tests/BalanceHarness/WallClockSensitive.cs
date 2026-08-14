using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔴 The xUnit collection that keeps M2-16a's CPU-bound harness cases from running <b>concurrently
/// with</b> the one wall-clock assertion in this suite.
/// </summary>
/// <remarks>
/// <para>
/// <c>CombatSimulatorTests.A_worst_case_1800_tick_fight_simulates_inside_the_budget</c> is M2-08's,
/// and its own remarks claim it <em>"never flakes"</em> at ten times `05`'s 5 ms headnote budget.
/// That was true of a suite whose other 2 875 cases are cheap. M2-16a added 121 cases that run
/// <b>real boss fights in bulk</b>, xUnit runs test collections in parallel by default, and the two
/// therefore compete for every core on the machine. Measured on this checkout, same binaries, same
/// commit:
/// </para>
/// <code>
/// isolated (--filter A_worst_case):  1200 ticks  8.243 ms   1800 ticks 12.619 ms   PASS
/// inside the full Core suite:        1200 ticks 29.804 ms   1800 ticks 50.038 ms   FAIL (&lt; 50)
/// </code>
/// <para>
/// 🔒 <b>The fix is to remove the contention, never to raise the multiple.</b> Widening the assertion
/// to fit the noise would delete exactly the regression M2-08 wrote it for — <em>"an accidental
/// per-tick aggregation of every actor, which is roughly 100×"</em> — and would hide it behind a
/// number that no longer means anything. Every class that runs fights in bulk, and the class holding
/// the measurement, declare this collection; xUnit then never schedules them against each other. The
/// cost is that those classes run sequentially, which is ~6 s.
/// </para>
/// <para>
/// ⚠️ <b>It does not make the measurement machine-independent, and nothing can.</b> The isolated
/// 8.243 ms above is already above `05`'s 5 ms on this hardware, in a debug build, in a test host —
/// which is the same caveat M2-08 recorded at 5.437 ms. The real per-fight cost of authored content
/// is in the harness's own report (median 2.64–3.04 ms, p90 3.45–4.12 ms over 1.2 M fights), and that
/// is the number `05`'s headnote should be read against.
/// </para>
/// </remarks>
[CollectionDefinition(WallClockSensitive.Name)]
public sealed class WallClockSensitiveCollection
{
}

/// <summary>The collection name, in one place so a typo cannot silently un-serialise a class.</summary>
/// <remarks>
/// ⚠️ A misspelled <c>[Collection("...")]</c> argument is not an error in xUnit — it silently creates
/// a <em>new</em> single-member collection, which runs in parallel with everything else and restores
/// exactly the flake this file exists to remove. <c>WallClockSensitiveTests</c> asserts that every
/// class named below really is in one collection, so the guard cannot go quiet.
/// </remarks>
public static class WallClockSensitive
{
    /// <summary>The one collection name.</summary>
    public const string Name = "wall-clock sensitive (05 headnote budget vs 05 §9 bulk fights)";
}
