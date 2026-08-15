using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>Checks that the minigame reward tables in <c>tuning/currencies.json</c> and Core's <c>MinigameRewardTuning</c> reader agree.</summary>
/// <remarks>
/// The same seam as <see cref="LoginCalendarTuningMatchesTuningDataTests"/>: <c>Core.Tests</c> is
/// hermetic and the reader is internal to Core, so neither half can see whether the other reads the
/// real numbers.
/// </remarks>
public sealed class MinigameRewardTuningMatchesTuningDataTests
{
    private const string CurrenciesDocument = "tuning/currencies.json";
    private const string RewardsPointer = CurrenciesDocument + "#/minigameRewards";

    /// <summary>The four minigame ids and the tier count each authors.</summary>
    public static TheoryData<string, int> ExpectedTierCounts => new()
    {
        { "MG_CHEST_PICK", 3 },
        { "MG_TIMING_BAR", 4 },
        { "MG_DICE_DUEL", 3 },
        { "MG_MEMORY_RUNE", 3 },
    };

    /// <summary>The tier count Core.Tests' hermetic fixture authors matches the shipped row count exactly.</summary>
    [Theory]
    [MemberData(nameof(ExpectedTierCounts))]
    public void The_shipped_row_count_matches_the_hermetic_fixtures_TierCount(string minigameId, int expectedTierCount)
    {
        Rewards(minigameId).GetArrayLength().ShouldBe(
            expectedTierCount,
            $"{RewardsPointer}/{minigameId} is read positionally by MinigameRewardTuning.TierCount, " +
            "against 03 §6.1's authored row count. A mismatch here leaves every hermetic Core test " +
            "green against a fixture with the wrong number of tiers, while the shipped server " +
            "legality-checks MINIGAME_SUBMIT against a different one.");
    }

    /// <summary>Every reward row's five currency columns are present and non-negative integers.</summary>
    [Theory]
    [MemberData(nameof(ExpectedTierCounts))]
    public void Every_row_carries_all_five_non_negative_integer_columns(string minigameId, int _)
    {
        foreach (var row in Rewards(minigameId).EnumerateArray())
        {
            foreach (var column in new[] { "gold", "crowns", "beastFeed", "enhanceStones", "rerollCharges" })
            {
                row.TryGetProperty(column, out var value).ShouldBeTrue(
                    $"{RewardsPointer}/{minigameId} is missing '{column}' on a row; " +
                    "MinigameRewardTuning.Read requires all five on every row.");
                value.TryGetInt64(out var amount).ShouldBeTrue($"'{column}' is not an integer.");
                amount.ShouldBeGreaterThanOrEqualTo(0L, $"'{column}' is negative.");
            }
        }
    }

    /// <summary>adBundleScalar is the chapter-scaling factor shared with ad bundles, the login calendar, and the shop's scaled material offers.</summary>
    [Fact]
    public void The_shipped_adBundleScalar_is_0_35()
    {
        using var document = JsonDocument.Parse(RepoData.Documents[CurrenciesDocument]);

        document.RootElement
            .GetProperty("chapterScalars")
            .GetProperty("adBundleScalar")
            .GetDouble()
            .ShouldBe(
                0.35,
                "03 §7a's chapter-scaling formula is 1 + adBundleScalar * (chapter - 1), and " +
                "MinigameRewardTuning reads this exact pointer. Core.Tests' hermetic fixture mirrors " +
                "0.35; a drift here leaves every hermetic Core reward-scaling assertion green against " +
                "the wrong factor.");
    }

    /// <summary>The worked numbers, spot-checked against the row this handler pays a Chapter-1 MG_CHEST_PICK gold-tier resolution from.</summary>
    [Fact]
    public void MG_CHEST_PICKs_gold_tier_matches_03_section_6_1s_authored_row()
    {
        var goldTier = Rewards("MG_CHEST_PICK")[2];

        goldTier.GetProperty("gold").GetInt64().ShouldBe(500L);
        goldTier.GetProperty("crowns").GetInt64().ShouldBe(60L);
        goldTier.GetProperty("beastFeed").GetInt64().ShouldBe(15L);
    }

    private static JsonElement Rewards(string minigameId)
    {
        using var document = JsonDocument.Parse(RepoData.Documents[CurrenciesDocument]);

        return document.RootElement
            .GetProperty("minigameRewards")
            .GetProperty(minigameId)
            .Clone();
    }
}
