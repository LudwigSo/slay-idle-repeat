using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔴 The xUnit collection that keeps the CPU-bound harness cases from running <b>concurrently
/// with</b> the one wall-clock assertion in this suite.
/// </summary>
/// <remarks>
/// xUnit runs collections in parallel, and the bulk boss-fight cases compete for every core with
/// <c>CombatSimulatorTests.A_worst_case_1800_tick_fight_simulates_inside_the_budget</c>. Measured on
/// one checkout: 12.6 ms isolated, 50.0 ms inside the full suite, against a 50 ms bound.
/// <para>
/// 🔒 The fix is to remove the contention, never to raise the multiple — widening the assertion to
/// fit the noise would delete the ~100× regression it was written for. The cost is ~6 s of
/// sequential running.
/// </para>
/// <para>
/// ⚠️ It does not make the measurement machine-independent, and nothing can. The real per-fight cost
/// is in the harness's own report (median 2.64–3.04 ms over 1.2 M fights).
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
