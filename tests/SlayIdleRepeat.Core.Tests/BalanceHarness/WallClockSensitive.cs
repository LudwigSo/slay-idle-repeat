using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// The xUnit collection that keeps the CPU-bound harness cases from running concurrently with the one
/// wall-clock assertion in this suite. xUnit runs collections in parallel, and the bulk boss-fight
/// cases compete for every core with the timing assertion, which otherwise flakes under contention.
/// </summary>
[CollectionDefinition(WallClockSensitive.Name)]
public sealed class WallClockSensitiveCollection
{
}

/// <summary>
/// The collection name, in one place so a typo cannot silently un-serialise a class. A misspelled
/// <c>[Collection("...")]</c> argument is not an error in xUnit — it silently creates a new
/// single-member collection that runs in parallel with everything else.
/// </summary>
public static class WallClockSensitive
{
    public const string Name = "wall-clock sensitive (05 headnote budget vs 05 §9 bulk fights)";
}
