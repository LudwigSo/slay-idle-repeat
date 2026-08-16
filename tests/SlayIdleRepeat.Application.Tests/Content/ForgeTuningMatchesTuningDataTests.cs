using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Checks that the three free Forge operations in <c>tuning/forge.json</c> and Core's
/// <c>ForgeTuning</c> reader agree — the numbers <c>08</c> §4 authors, at the pointers the reader
/// actually reads.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic and mirrors the shipped file in a fixture, and the reader is
/// internal to Core, so this suite reads the real file instead. Neither half is sufficient alone:
/// the hermetic one proves the rules are right about the numbers, this one proves those are the
/// numbers we ship. The pointer strings are written out rather than shared with the reader — which
/// this assembly cannot see — so a rename in one place and not the other fails here.
/// </remarks>
public sealed class ForgeTuningMatchesTuningDataTests
{
    private const string ForgeDocument = "tuning/forge.json";

    private const string MergePointer = ForgeDocument + "#/merge";

    private const string EnhancePointer = ForgeDocument + "#/enhance";

    /// <summary><c>08</c> §4.1 — a fusion takes three items and dust may fill one of the slots.</summary>
    [Fact]
    public void The_shipped_fusion_takes_the_three_inputs_08_4_1_authors()
    {
        var merge = Block("merge");

        merge.GetProperty("inputCount").GetInt32().ShouldBe(
            3,
            $"08 §4.1 fuses THREE items, and {MergePointer}/inputCount is the one place that is a " +
            "number. The MERGE payload carries a list precisely so it can express three, and a " +
            "retune here that Core's ForgeTuning did not read would leave every hermetic test green.");

        merge.GetProperty("dustSubstituteMaxInputs").GetInt32().ShouldBe(
            1, "08 §4.1 substitutes Merge Dust for ONE of the three inputs.");
    }

    /// <summary><c>08</c> §4.1 — the Crown price of a fusion, keyed on the band it lands on.</summary>
    [Fact]
    public void The_shipped_Crown_prices_are_the_ones_08_4_1_authors()
    {
        var prices = Block("merge").GetProperty("crownCostByOutputRarity");

        prices.GetProperty("B").GetInt64().ShouldBe(120);
        prices.GetProperty("A").GetInt64().ShouldBe(600);
        prices.GetProperty("S").GetInt64().ShouldBe(3000);
        prices.GetProperty("SS").GetInt64().ShouldBe(15000);

        prices.TryGetProperty("C", out _).ShouldBeFalse(
            "the map is keyed on the OUTPUT band and nothing fuses onto the bottom rung, so a C row " +
            "would price an operation that does not exist.");
    }

    /// <summary>
    /// 🔒 <c>08</c> §4.1's <c>SS = n/a</c> is an authored absence, and it is authored as a JSON
    /// <c>null</c> rather than omitted — the two are different things, and the reader tells them
    /// apart.
    /// </summary>
    [Fact]
    public void The_shipped_dust_price_states_the_top_bands_absence_rather_than_omitting_it()
    {
        var prices = Block("merge").GetProperty("dustSubstituteCost");

        prices.GetProperty("C").GetInt64().ShouldBe(50);
        prices.GetProperty("B").GetInt64().ShouldBe(200);
        prices.GetProperty("A").GetInt64().ShouldBe(800);
        prices.GetProperty("S").GetInt64().ShouldBe(3200);

        prices.TryGetProperty("SS", out var top).ShouldBeTrue(
            "an omitted row and an authored null are different things: one is a hole in the document " +
            "and the other is a decision. 08 §4.1 writes n/a, which is the decision.");

        top.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary><c>08</c> §4.2 — the stone ladder, one cost per level, exactly as the table prints it.</summary>
    [Fact]
    public void The_shipped_stone_ladder_is_the_one_08_4_2_prints()
    {
        Block("enhance").GetProperty("stoneCostPerLevel")
            .EnumerateArray()
            .Select(cost => cost.GetInt64())
            .ShouldBe([2, 3, 4, 6, 8, 12, 16, 22, 30, 40, 55, 75, 100, 140, 200]);
    }

    /// <summary>
    /// 🔒 <c>08</c> §4.2's success ladder: the band endpoints spread LINEARLY and evenly across each
    /// band's five levels. The second band is non-integral by construction — a ladder rounded to
    /// look tidy would carry 0.44 and 0.31 where this one carries 0.4375 and 0.3125.
    /// </summary>
    [Fact]
    public void The_shipped_success_ladder_is_the_bands_endpoints_spread_evenly()
    {
        Block("enhance").GetProperty("perLevelSuccessRate")
            .EnumerateArray()
            .Select(rate => rate.GetDouble())
            .ShouldBe(
            [
                1.0, 1.0, 1.0, 1.0, 1.0,
                0.85, 0.8, 0.75, 0.7, 0.65,
                0.5, 0.4375, 0.375, 0.3125, 0.25,
            ],
            $"{EnhancePointer}/perLevelSuccessRate carried a deliberate null while 08 §4.2's arrow " +
            "notation was unread. It is now the expansion of the same table's endpoints, and " +
            "DeclaredRules checks the two against each other — but only this reads the shipped file.");
    }

    /// <summary><c>08</c> §4.2 — the bounds, and the total the ceiling is worth.</summary>
    [Fact]
    public void The_shipped_enhancement_bounds_are_the_ones_08_4_2_authors()
    {
        var enhance = Block("enhance");

        enhance.GetProperty("minLevel").GetInt32().ShouldBe(0);
        enhance.GetProperty("maxLevel").GetInt32().ShouldBe(15);
        enhance.GetProperty("statBonusPerLevel").GetDouble().ShouldBe(0.07);
        enhance.GetProperty("totalMultiplierAtMax").GetDouble().ShouldBe(2.05);
    }

    /// <summary><c>08</c> §4.3 — the salvage values, band by band, and the two shares.</summary>
    [Fact]
    public void The_shipped_salvage_values_are_the_ones_08_4_3_authors()
    {
        var salvage = Block("salvage");
        var dust = salvage.GetProperty("baseDustByRarity");

        dust.GetProperty("C").GetInt64().ShouldBe(10);
        dust.GetProperty("B").GetInt64().ShouldBe(40);
        dust.GetProperty("A").GetInt64().ShouldBe(160);
        dust.GetProperty("S").GetInt64().ShouldBe(640);
        dust.GetProperty("SS").GetInt64().ShouldBe(2560);

        salvage.GetProperty("dustPerEnhanceLevel").GetDouble().ShouldBe(0.15);
        salvage.GetProperty("stoneRefundShare").GetDouble().ShouldBe(0.6);
    }

    /// <summary>
    /// <c>24</c> §4.6's failure mercy, in the document Core's <c>LuckTuning</c> reads it from — the
    /// slope the enhancement rate is raised by, and the two flags the ad bonus obeys.
    /// </summary>
    [Fact]
    public void The_shipped_failure_mercy_is_the_one_24_4_6_authors()
    {
        using var document = JsonDocument.Parse(RepoData.Documents["tuning/luck.json"]);

        var enhance = document.RootElement.GetProperty("enhance");

        enhance.GetProperty("mercySlopePerConsecutiveFailure").GetDouble().ShouldBe(0.08);
        enhance.GetProperty("effectiveRateCap").GetDouble().ShouldBe(1.0);
        enhance.GetProperty("adEnhanceLuckStacksAdditively").GetBoolean().ShouldBeTrue();
        enhance.GetProperty("adEnhanceLuckAdvancesCounter").GetBoolean().ShouldBeFalse();
    }

    private static JsonElement Block(string name)
    {
        using var document = JsonDocument.Parse(RepoData.Documents[ForgeDocument]);

        return document.RootElement.GetProperty(name).Clone();
    }
}
