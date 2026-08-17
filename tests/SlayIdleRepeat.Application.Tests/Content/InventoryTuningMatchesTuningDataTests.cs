using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Checks that the inventory reader and the two documents it spans cannot drift apart — and that the
/// capacity ceiling and the ladder that reaches it are one number rather than two.
/// </summary>
/// <remarks>
/// <para>
/// <c>SlayIdleRepeat.Core.Content.InventoryTuning</c> reads three leaves out of
/// <c>forge.json#/inventory</c> and four out of <c>currencies.json</c>. <c>Core.Tests</c> is hermetic
/// and proves the arithmetic against a fixture that mirrors the shipped files, and the reader is
/// internal to Core, so this suite reads the real files directly instead. Renaming a pointer in the
/// data would otherwise leave the Core fixture green and the shipped game throwing
/// <c>MissingContentException</c> on the first expansion.
/// </para>
/// <para>
/// 🔒 <b>The ceiling was 400 and the ladder reached 320.</b> The forge document said so in its own
/// <c>_doc</c>, in as many words, and carried a second key restating the ladder's answer beside the
/// first one. Two numbers for one quantity is not a conflict a reader can resolve, so the documents
/// picked: the ceiling is what the ladder reaches, the second key is gone, and the reader refuses a
/// data set where the two disagree.
/// </para>
/// </remarks>
public sealed class InventoryTuningMatchesTuningDataTests
{
    private const string ForgeDocument = "tuning/forge.json";
    private const string CurrenciesDocument = "tuning/currencies.json";

    /// <summary>The three leaves the reader takes from the forge document.</summary>
    public static TheoryData<string, decimal, string> AuthoredCapacityNumbers => new()
    {
        { "baseCapacity", 120m, "08 §5 — a player starts with 120 slots" },
        { "expansionStep", 20m, "08 §5 — +20 slots per expansion" },
        { "maxCapacity", 320m, "120 + 10 purchases of +20, which is as far as the ladder goes" },
    };

    [Theory]
    [MemberData(nameof(AuthoredCapacityNumbers))]
    public void The_inventory_block_authors_the_number_the_design_docs_state(
        string leaf, decimal expected, string source)
    {
        var value = Read(Inventory(), leaf, ForgeDocument + "#/inventory");

        value.ValueKind.ShouldBe(
            JsonValueKind.Number,
            $"{ForgeDocument}#/inventory/{leaf} is not a number. A null there is game-data's " +
            "unauthorised-hole marker, and the inventory reader refuses to read one as a default.");

        value.GetDecimal().ShouldBe(
            expected,
            $"{source}. Changing this number is a balance change, not a refactor: update the design " +
            "doc and this case together.");

        value.TryGetInt32(out _).ShouldBeTrue(
            $"{ForgeDocument}#/inventory/{leaf} is fractional. Slots are whole, and the reader takes " +
            "this through ReadInt32, which throws rather than round.");
    }

    /// <summary>
    /// 🔒 The ceiling is exactly what the ladder reaches, computed from the shipped numbers rather
    /// than restated.
    /// </summary>
    /// <remarks>
    /// The one assertion that would have caught the original conflict, and the one the reader itself
    /// now enforces at load time. Written as the arithmetic rather than as the literal 320, so it
    /// keeps holding if the ladder is ever lengthened and stops holding the moment only one of the
    /// two sides moves.
    /// </remarks>
    [Fact]
    public void The_capacity_ceiling_is_exactly_what_the_ladder_reaches()
    {
        var baseCapacity = Read(Inventory(), "baseCapacity", ForgeDocument + "#/inventory").GetInt32();
        var ceiling = Read(Inventory(), "maxCapacity", ForgeDocument + "#/inventory").GetInt32();

        var crowns = Crowns();
        var purchases = Read(crowns, "inventoryExpansionMaxPurchases", CurrenciesDocument + "#/crowns")
            .GetInt32();
        var slots = Read(crowns, "inventoryExpansionSlotsPerPurchase", CurrenciesDocument + "#/crowns")
            .GetInt32();

        ceiling.ShouldBe(
            baseCapacity + (purchases * slots),
            $"{ForgeDocument} caps capacity at {ceiling} and {CurrenciesDocument} sells " +
            $"{purchases} expansions of +{slots} from a base of {baseCapacity}. The two cannot both " +
            "be right, and InventoryTuning.Read refuses the data set when they disagree — so this " +
            "does not ship, it fails on the first command of every session.");
    }

    /// <summary>
    /// 🔒 The key that used to restate the ladder's answer beside a disagreeing ceiling is gone.
    /// </summary>
    /// <remarks>
    /// One quantity, one place. Left in, it would be a third number to keep in step and the obvious
    /// thing for a future reader to "fix" the ceiling against — which is how the conflict got written
    /// down as a fact in the first place.
    /// </remarks>
    [Fact]
    public void The_document_no_longer_carries_a_second_answer_for_the_ceiling()
    {
        Inventory().EnumerateObject().Select(p => p.Name).ShouldNotContain(
            "maxCapacityReachableFromLadder",
            $"{ForgeDocument}#/inventory authored the ladder's reach as a key of its own, next to a " +
            "maxCapacity that disagreed with it. maxCapacity now IS the ladder's reach and the reader " +
            "checks that, so a second key is a second source for one number.");
    }

    /// <summary>The expansion prices, both currencies.</summary>
    /// <remarks>
    /// The ladder is compared element by element: a reader pointed at the wrong array, or a ladder
    /// that lost a rung, would still be ten-ish escalating numbers.
    /// </remarks>
    [Fact]
    public void The_expansion_prices_are_the_ladder_and_the_flat_alternative()
    {
        var crowns = Crowns();

        Read(crowns, "inventoryExpansionLadder", CurrenciesDocument + "#/crowns")
            .EnumerateArray()
            .Select(rung => rung.GetInt64())
            .ShouldBe(
                new long[] { 800, 1000, 1250, 1560, 1950, 2440, 3050, 3810, 4770, 6000 },
                "10 §4's escalating Crown ladder, one rung per purchase.");

        Read(crowns, "inventoryExpansionMaxPurchases", CurrenciesDocument + "#/crowns")
            .GetInt32().ShouldBe(10);

        var sinks = Read(SoulShards(), "sinks", CurrenciesDocument + "#/soulShards");

        Read(sinks, "INVENTORY_EXPANSION_FLAT", CurrenciesDocument + "#/soulShards/sinks")
            .GetInt64().ShouldBe(
                400L,
                "10 §2 prices the alternative flat — the same 400 at the first expansion and at the " +
                "tenth, where the Crown rung is 6,000.");
    }

    /// <summary>Floor: every leaf the reader reads is present in the block it reads it from.</summary>
    /// <remarks>
    /// Stated as presence rather than as a count: both blocks hold other keys, so a count floor would
    /// hold even after every leaf the reader needs had gone.
    /// </remarks>
    [Fact]
    public void Every_leaf_the_inventory_reader_reads_is_present()
    {
        var forgeLeaves = Inventory().EnumerateObject().Select(p => p.Name).ToArray();

        new[] { "baseCapacity", "expansionStep", "maxCapacity" }
            .Except(forgeLeaves, StringComparer.Ordinal)
            .ShouldBeEmpty(
                $"a leaf InventoryTuning reads is gone from {ForgeDocument}#/inventory. It throws " +
                "MissingContentException without it. The data moved — fix the reader, do not delete " +
                "the case.");

        var crownLeaves = Crowns().EnumerateObject().Select(p => p.Name).ToArray();

        new[]
        {
            "inventoryExpansionLadder",
            "inventoryExpansionMaxPurchases",
            "inventoryExpansionSlotsPerPurchase",
        }
            .Except(crownLeaves, StringComparer.Ordinal)
            .ShouldBeEmpty($"a leaf InventoryTuning reads is gone from {CurrenciesDocument}#/crowns.");
    }

    /// <summary>Reads a member, failing with a diagnostic rather than throwing <c>KeyNotFoundException</c>.</summary>
    private static JsonElement Read(JsonElement parent, string member, string parentPointer)
    {
        parent.TryGetProperty(member, out var value).ShouldBeTrue(
            $"{parentPointer}/{member} is gone. The inventory reader or its pin reads it; the data " +
            "moved — fix the reader, do not delete the case.");

        return value;
    }

    private static JsonElement Inventory() => Block(ForgeDocument, "inventory");

    private static JsonElement Crowns() => Block(CurrenciesDocument, "crowns");

    private static JsonElement SoulShards() => Block(CurrenciesDocument, "soulShards");

    private static JsonElement Block(string documentPath, string member)
    {
        using var document = JsonDocument.Parse(RepoData.Documents[documentPath]);

        document.RootElement.TryGetProperty(member, out var block).ShouldBeTrue(
            $"{documentPath}#/{member} is gone — a block this file reads. The document moved; fix " +
            "the reader, do not delete the file.");

        return block.Clone();
    }
}
