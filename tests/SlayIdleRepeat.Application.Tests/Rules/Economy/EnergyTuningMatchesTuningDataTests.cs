using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Rules.Economy;

/// <summary>Checks that the energy math and <c>tuning/progression.json</c> cannot drift apart.</summary>
/// <remarks>
/// <c>SlayIdleRepeat.Core.Rules.Economy.EnergyTuning</c> reads six pointers out of
/// <c>progression.json</c>'s <c>energy</c> block and <c>EnergyMath</c> computes from what it finds.
/// <c>Core.Tests</c> is hermetic and proves the math against a fixture that mirrors the shipped
/// file, and the reader is internal to Core, so this suite reads the real file directly instead.
/// Renaming a pointer in the data would otherwise leave the Core fixture green and the shipped game
/// throwing <c>MissingContentException</c> on the first command.
/// </remarks>
public sealed class EnergyTuningMatchesTuningDataTests
{
    private const string ProgressionDocument = "tuning/progression.json";
    private const string CurrenciesDocument = "tuning/currencies.json";

    /// <summary>
    /// The six leaves <c>EnergyTuning</c> reads. Keep in step with
    /// <c>EnergyTuningTests.The_pointers_the_reader_reads_are_the_documented_ones</c> in
    /// <c>SlayIdleRepeat.Core.Tests</c>, which pins the strings the reader passes; this one pins the
    /// file they resolve against.
    /// </summary>
    /// <remarks>
    /// The <c>WholeNumber</c> column is not decoration: five of the six reach
    /// <c>ContentSnapshot.ReadInt32</c>, which throws on a fraction, but
    /// <c>progression.schema.json</c> types two of them as <c>number</c>, so a fractional value
    /// would validate, ship, and take down the first command of every session. Only the
    /// regeneration interval is legitimately fractional.
    /// </remarks>
    public static TheoryData<string, decimal, bool, string> AuthoredEnergyNumbers => new()
    {
        { "baseMax", 120m, true, "10 §3 — Max Energy 120" },
        { "perLegendLevel", 2m, true, "10 §3 — +2 per Legend Level" },
        { "maxCap", 200m, true, "10 §3 — cap 200" },
        { "regenMinutesPerPoint", 4m, false, "10 §3 — 1 per 4 minutes (15/hour)" },
        { "runCost", 20m, true, "10 §3 — Run cost 20" },
        { "reserveMultipleOfMax", 1m, true, "28 C2 — the Reserve holds 1× Max Energy" },
    };

    /// <summary>
    /// The energy source table. <c>EnergyMath</c> models the amounts (<c>Grant</c> and
    /// <c>RefillToFull</c>) and none of the per-day caps: those need a daily counter and a reset,
    /// which belong to the granting commands, not the arithmetic. Pinned here so the deferral is
    /// visible and the numbers stay greppable.
    /// </summary>
    public static TheoryData<string, decimal?, int?> AuthoredEnergySources => new()
    {
        { "AD_ENERGY", 40m, 4 },
        { "DAILY_FREE_REFILL", null, 1 },
        { "LEGEND_LEVEL_UP", null, null },
        { "DAILY_QUEST", 20m, 3 },
        { "TILE_EVENT", 10m, null },
    };

    [Theory]
    [MemberData(nameof(AuthoredEnergyNumbers))]
    public void The_energy_block_authors_the_number_the_design_docs_state(
        string leaf, decimal expected, bool wholeNumber, string source)
    {
        var value = Read(EnergyBlock(), leaf, ProgressionDocument + "#/energy");

        value.ValueKind.ShouldBe(
            JsonValueKind.Number,
            $"{ProgressionDocument}#/energy/{leaf} is not a number. A null there is game-data's " +
            "unauthorised-hole marker, and the energy math refuses to read one as a default (S6).");

        value.GetDecimal().ShouldBe(expected, $"{source}. Changing this number is a balance change, " +
            "not a refactor: update the design doc and this case together.");

        if (wholeNumber)
        {
            value.TryGetInt32(out _).ShouldBeTrue(
                $"{ProgressionDocument}#/energy/{leaf} is fractional. EnergyTuning reads it through " +
                "ContentSnapshot.ReadInt32, which throws ContentTypeMismatchException rather than " +
                "round a number 10 §3 authors as whole Energy points — so this ships and then fails " +
                "on the first command of every session.");
        }
    }

    [Theory]
    [MemberData(nameof(AuthoredEnergySources))]
    public void The_energy_sources_of_10_3_1_are_authored_with_their_amounts_and_caps(
        string sourceId, decimal? amount, int? capPerDay)
    {
        var source = Read(Read(EnergyBlock(), "sources", ProgressionDocument + "#/energy"),
                          sourceId,
                          ProgressionDocument + "#/energy/sources");

        var authoredAmount = Read(source, "amount", $"{ProgressionDocument}#/energy/sources/{sourceId}");

        if (amount is null)
        {
            authoredAmount.GetString().ShouldBe(
                "FULL",
                $"10 §3.1 authors {sourceId} as a refill to full, which EnergyMath.RefillToFull " +
                "models as the bar's deficit. A number here would be a different mechanic.");
        }
        else
        {
            authoredAmount.GetDecimal().ShouldBe(
                amount.Value,
                $"10 §3.1 authors {sourceId} at +{amount.Value}. EnergyMathTests exercises " +
                "EnergyMath.Grant with this amount; if the data moves, that case is testing a " +
                "grant the game no longer makes.");
        }

        var authoredCap = Read(source, "capPerDay", $"{ProgressionDocument}#/energy/sources/{sourceId}");

        if (capPerDay is null)
        {
            authoredCap.ValueKind.ShouldBe(
                JsonValueKind.Null,
                $"10 §3.1 authors no daily cap for {sourceId}.");
        }
        else
        {
            authoredCap.GetInt32().ShouldBe(
                capPerDay.Value,
                $"10 §3.1 caps {sourceId} at {capPerDay.Value}/day. 🔒 M1-10 does NOT enforce this — " +
                "a per-day cap needs a daily counter and the 05:00 UTC reset, which belong to the " +
                "granting command. It is pinned here so the deferral cannot be mistaken for the " +
                "number never having existed.");
        }
    }

    /// <summary>
    /// Floor: the set that matters is "the six leaves the reader reads are all there", not a count
    /// — the block also holds several other keys, so a count floor of six would hold even if all
    /// six authored leaves vanished.
    /// </summary>
    [Fact]
    public void Every_leaf_the_energy_reader_reads_is_present_in_the_block()
    {
        var present = EnergyBlock().EnumerateObject().Select(p => p.Name).ToArray();
        var read = AuthoredEnergyNumbers.Select(row => (string)row[0]).ToArray();

        read.Length.ShouldBe(6, "EnergyTuning reads six pointers; this theory data has drifted.");
        read.Except(present, StringComparer.Ordinal).ShouldBeEmpty(
            $"a leaf EnergyTuning reads is gone from {ProgressionDocument}#/energy. It throws " +
            "MissingContentException without it, so the first command of every session fails. The " +
            "data moved — fix the reader, do not delete the case.");
    }

    /// <summary>The three structural facts the energy math is written against.</summary>
    /// <remarks>
    /// The two Reserve flags are also const-pinned by <c>progression.schema.json</c>, so they only
    /// move if the schema moves with them. <c>regenWhileOffline</c> is not const-pinned — it's a
    /// free boolean — and it's the one <c>EnergyMath.Accrue</c>'s whole signature rests on: taking
    /// an arbitrary elapsed span only makes sense because offline regeneration is the only offline
    /// accrual in the game.
    /// </remarks>
    [Fact]
    public void The_reserve_receives_overflow_only_never_regenerates_and_energy_accrues_offline()
    {
        var energy = EnergyBlock();

        Read(energy, "reserveReceivesOverflowOnly", ProgressionDocument + "#/energy")
            .ValueKind.ShouldBe(
                JsonValueKind.True,
                "28 C2: the Reserve receives overflow only. EnergyMath.Accrue routes into it " +
                "exclusively from what the main bar could not hold, and has no other path.");

        Read(energy, "reserveRegeneratesOnItsOwn", ProgressionDocument + "#/energy")
            .ValueKind.ShouldBe(
                JsonValueKind.False,
                "28 C2: the Reserve never regenerates on its own. EnergyMath.Accrue has no term " +
                "that adds to it independently of the main bar.");

        Read(energy, "regenWhileOffline", ProgressionDocument + "#/energy")
            .ValueKind.ShouldBe(
                JsonValueKind.True,
                "10 §3: regeneration while offline is the only offline accrual in the game. " +
                "EnergyMath.Accrue takes an arbitrary elapsed span — days, weeks — precisely " +
                "because of this row. Turn it off and the signature is wrong.");
    }

    /// <summary>The Soul Shard energy refill ladder is authored, and deliberately not read by the energy math.</summary>
    /// <remarks>
    /// This case originally asserted the ladder was unauthored, searching for a key under
    /// <c>progression.json#/energy/sources</c> — a permanently green assertion over an impossible
    /// question, since <c>progression.schema.json</c> constrains every <c>sources</c> entry to
    /// <c>{amount, capPerDay}</c> and no price could ever have lived at that pointer. Restated as
    /// what is true: the energy math models the amount of a refill (<c>EnergyMath.RefillToFull</c>),
    /// not the price, which escalates per use per day and resets daily — shop state, not energy
    /// arithmetic.
    /// </remarks>
    [Fact]
    public void The_soul_shard_refill_ladder_is_authored_in_currencies_and_not_read_by_the_energy_math()
    {
        var sinks = Read(
            SoulShards(), "sinks", CurrenciesDocument + "#/soulShards");

        Read(sinks, "ENERGY_REFILL_BASE", CurrenciesDocument + "#/soulShards/sinks")
            .GetDecimal().ShouldBe(
                300m,
                "10 §5.1 prices the full Energy refill at 300 Soul Shards. M1-10 does not read this " +
                "— the per-day escalation makes it a shop rule — but the number must stay greppable " +
                "so the command that does read it reads an authored value rather than inventing one.");

        Read(sinks, "ENERGY_REFILL_INCREMENT_PER_USE_PER_DAY", CurrenciesDocument + "#/soulShards/sinks")
            .GetDecimal().ShouldBe(
                150m,
                "10 §5.1: +150 Soul Shards per use per day, resetting daily.");

        // The other half of the claim: not read HERE. A refill price inside the energy block would
        // mean the shop rule had leaked into the energy math.
        EnergyBlock().EnumerateObject().Select(p => p.Name).ShouldNotContain(
            "soulShardRefill",
            "a Soul Shard price appeared in progression.json#/energy. The ladder is authored in " +
            "currencies.json and escalates per use per day; the energy math has no daily counter " +
            "and must not grow one by reading it from here.");
    }

    /// <summary>Reads a member, failing with a diagnostic rather than throwing <c>KeyNotFoundException</c>.</summary>
    /// <remarks><c>JsonElement.GetProperty</c> throws before any Shouldly message can print, so a renamed key would produce a bare "key not present" error instead of the diagnostics below.</remarks>
    private static JsonElement Read(JsonElement parent, string member, string parentPointer)
    {
        parent.TryGetProperty(member, out var value).ShouldBeTrue(
            $"{parentPointer}/{member} is gone. The energy math or its pin reads it; the data " +
            "moved — fix the reader, do not delete the case.");

        return value;
    }

    private static JsonElement EnergyBlock() => Block(ProgressionDocument, "energy");

    private static JsonElement SoulShards() => Block(CurrenciesDocument, "soulShards");

    private static JsonElement Block(string documentPath, string member)
    {
        using var document = JsonDocument.Parse(RepoData.Documents[documentPath]);

        document.RootElement.TryGetProperty(member, out var block).ShouldBeTrue(
            $"{documentPath}#/{member} is gone — the block every case in this file reads. " +
            "The document moved; fix the reader, do not delete the file.");

        return block.Clone();
    }
}
