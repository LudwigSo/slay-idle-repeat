using Xunit;

namespace SlayIdleRepeat.Core.Tests.TestSupport;

/// <summary>
/// The xUnit collection that keeps this suite's one wall-clock assertion off the parallel
/// scheduler. Measured: run alongside the other ~6,100 cases it competes for every core and the
/// `05` headnote budget assertion flakes; run serialised it passes with margin.
/// </summary>
/// <remarks>
/// 🔒 <c>DisableParallelization</c> is the load-bearing part, and it is stronger than what this
/// collection carried before. It used to group the timing case with the balance harness's bulk
/// boss-fight classes, which serialised it against the only other CPU-bound work in the suite;
/// that worked only because those classes were the whole competition. They went with the harness,
/// so the guard now has to say what it always meant: this collection does not run beside anything.
/// </remarks>
[CollectionDefinition(WallClockSensitive.Name, DisableParallelization = true)]
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
    /// <summary>The collection name.</summary>
    public const string Name = "wall-clock sensitive (05 headnote budget)";
}
