using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The counter map's own contract, where the <c>Player</c> seam cannot show it. Everything else —
/// rehydrate refusals, round trips, reads — is covered at <c>Player.Rehydrate</c>/<c>SetPityCounter</c>
/// in <c>PlayerPityCounterTests</c>.
/// </summary>
public sealed class PityCountersTests
{
    private const string ChestA = "chest.standard:A";
    private const string ChestS = "chest.standard:S";
    private const string PremiumS = "chest.premium:S";

    /// <remarks>
    /// The keys are authored ids: a culture- or case-insensitive map would address one counter on
    /// one device and two on another.
    /// </remarks>
    [Fact]
    public void Counter_keys_are_compared_ordinally()
    {
        var counters = PityCounters.Empty.With(ChestA, 7);

        counters.Get("CHEST.STANDARD:A").ShouldBe(PityCounters.Unstarted);
        counters.Get("chest.standard:a").ShouldBe(PityCounters.Unstarted);
        counters.Get(ChestA).ShouldBe(7);
    }

    /// <remarks><c>24</c> §1.2: counters never pool across classes or rungs.</remarks>
    [Fact]
    public void With_stores_one_counter_and_leaves_every_other_counter_alone()
    {
        var before = PityCounters.Empty.With(ChestA, 9).With(ChestS, 39).With(PremiumS, 4);

        var after = before.With(ChestA, 10);

        Canonical(after).ShouldBe("chest.premium:S=4;chest.standard:A=10;chest.standard:S=39");
        Canonical(before).ShouldBe(
            "chest.premium:S=4;chest.standard:A=9;chest.standard:S=39",
            "the map is immutable and replaced wholesale — a resolution answers the counters it " +
            "would leave behind, and the aggregate decides whether to keep them");
    }

    /// <summary>One map as ordinal-sorted <c>key=value</c> text — record equality would compare the dictionary by reference.</summary>
    private static string Canonical(PityCounters counters) =>
        string.Join(
            ";",
            counters.Counters
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}={entry.Value.ToString(CultureInfo.InvariantCulture)}"));
}
