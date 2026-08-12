using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Rules.Economy;

/// <summary>
/// 🔒 `21` §3.1 — the energy math and <c>game-data/tuning/progression.json</c> cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// <c>SlayIdleRepeat.Core.Rules.Economy.EnergyTuning</c> reads six pointers out of the
/// <c>energy</c> block and <c>EnergyMath</c> computes from what it finds. Neither half can see the
/// other: <c>Core.Tests</c> is hermetic, so it proves the math against a fixture that <em>mirrors</em>
/// the shipped file, and the reader is <c>internal</c> to <c>Core</c>, so this suite cannot call it
/// (<c>InternalsVisibleTo</c> names <c>SlayIdleRepeat.Core.Tests</c> alone, `30` §11.3).
/// </para>
/// <para>
/// What is left is the seam between them, and this is the case that closes it: the real file still
/// authors those six pointers, and still authors them with the numbers `10` §3 and `28` C2 write
/// out. Renaming <c>regenMinutesPerPoint</c> in the data would otherwise leave the Core fixture
/// green and the shipped game throwing <c>MissingContentException</c> on the first command.
/// </para>
/// <para>
/// It reads a file, which is why it is here rather than in <c>Core.Tests</c> — the same reason and
/// the same mechanism as <c>CurrencyIdMatchesTuningDataTests</c>. No adapter, no port, no container,
/// no network: <see cref="RepoData"/> over the checkout the test runs from.
/// </para>
/// </remarks>
public sealed class EnergyTuningMatchesTuningDataTests
{
    private const string ProgressionDocument = "tuning/progression.json";

    /// <summary>
    /// The six leaves <c>EnergyTuning</c> reads, with the values `10` §3 and `28` C2 author.
    /// 🔒 Keep in step with <c>EnergyTuningTests.The_pointers_the_reader_reads_are_the_documented_ones</c>
    /// in <c>SlayIdleRepeat.Core.Tests</c>: that case pins the strings the reader passes, this one
    /// pins the file they resolve against.
    /// </summary>
    public static TheoryData<string, decimal, string> AuthoredEnergyNumbers => new()
    {
        { "baseMax", 120m, "10 §3 — Max Energy 120" },
        { "perLegendLevel", 2m, "10 §3 — +2 per Legend Level" },
        { "maxCap", 200m, "10 §3 — cap 200" },
        { "regenMinutesPerPoint", 4m, "10 §3 — 1 per 4 minutes (15/hour)" },
        { "runCost", 20m, "10 §3 — Run cost 20" },
        { "reserveMultipleOfMax", 1m, "28 C2 — the Reserve holds 1× Max Energy" },
    };

    [Theory]
    [MemberData(nameof(AuthoredEnergyNumbers))]
    public void The_energy_block_authors_the_number_the_design_docs_state(
        string leaf, decimal expected, string source)
    {
        var energy = EnergyBlock();

        energy.TryGetProperty(leaf, out var value).ShouldBeTrue(
            $"{ProgressionDocument}#/energy/{leaf} is gone. SlayIdleRepeat.Core.Rules.Economy." +
            $"EnergyTuning reads that pointer and throws MissingContentException without it, so the " +
            "first command of every session would fail. The data moved — fix the reader, do not " +
            "delete the case.");

        value.ValueKind.ShouldBe(
            JsonValueKind.Number,
            $"{ProgressionDocument}#/energy/{leaf} is not a number. A null there is game-data's " +
            "unauthorised-hole marker, and the energy math refuses to read one as a default (S6).");

        value.GetDecimal().ShouldBe(expected, $"{source}. Changing this number is a balance change, " +
            "not a refactor: update the design doc and this case together.");
    }

    /// <summary>
    /// 🔒 S3 — the floor. Every case above is a lookup into one object, and an empty object would
    /// make each of them fail with the same "is gone" message rather than one legible signal that
    /// the block itself has moved.
    /// </summary>
    [Fact]
    public void The_progression_document_still_has_an_energy_block()
    {
        EnergyBlock().ValueKind.ShouldBe(JsonValueKind.Object);
        EnergyBlock().EnumerateObject().Count().ShouldBeGreaterThanOrEqualTo(
            AuthoredEnergyNumbers.Count(),
            $"{ProgressionDocument}#/energy holds fewer leaves than the energy math reads.");
    }

    /// <summary>
    /// 🔒 `28` C2 — the two structural facts the Reserve rests on are authored as constants in the
    /// schema (<c>"const": true</c> / <c>"const": false</c>), and the math is written against them:
    /// the Reserve receives overflow only, and never regenerates on its own. If either ever flips,
    /// <c>EnergyMath.Accrue</c> is wrong and nothing else would say so.
    /// </summary>
    [Fact]
    public void The_reserve_receives_overflow_only_and_never_regenerates()
    {
        var energy = EnergyBlock();

        energy.GetProperty("reserveReceivesOverflowOnly").GetBoolean().ShouldBeTrue(
            "28 C2: the Reserve receives overflow only. EnergyMath.Accrue routes into it exclusively " +
            "from what the main bar could not hold, and has no other path.");

        energy.GetProperty("reserveRegeneratesOnItsOwn").GetBoolean().ShouldBeFalse(
            "28 C2: the Reserve never regenerates on its own. EnergyMath.Accrue has no term that " +
            "adds to it independently of the main bar.");
    }

    /// <summary>
    /// 🔒 S6 / `10` §3.1 — the Soul Shard energy refill is authored as "escalating cost" in `10`
    /// §3.1 and priced in `10` §5.1 as 300 Soul Shards, +150 per use per day. Neither number is in
    /// <c>game-data/tuning/</c>, so M1-10 did not read one and did not invent one. This case pins
    /// the absence: the day somebody authors the ladder, it goes red and the gap gets closed
    /// deliberately rather than half-implemented.
    /// </summary>
    [Fact]
    public void The_soul_shard_refill_price_ladder_is_still_unauthored()
    {
        var sources = EnergyBlock().GetProperty("sources");

        sources.TryGetProperty("SOUL_SHARD_REFILL", out _).ShouldBeFalse(
            "somebody authored the Soul Shard energy refill in tuning/progression.json. 10 §5.1 " +
            "prices it at 300 Soul Shards, +150 per use per day, resetting daily — a shop price with " +
            "a per-day escalation, which the M1-10 energy math does not model. Close the gap in the " +
            "register rather than leaving the ladder half-read.");
    }

    private static JsonElement EnergyBlock()
    {
        using var document = JsonDocument.Parse(RepoData.Documents[ProgressionDocument]);

        return document.RootElement.GetProperty("energy").Clone();
    }
}
