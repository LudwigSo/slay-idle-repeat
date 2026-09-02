using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Model;

/// <summary>Checks that the Player aggregate's Legend Level invariant and <c>tuning/progression.json</c> cannot drift apart.</summary>
/// <remarks>
/// The aggregate requires a Legend Level to be inside the authored range; <c>LegendTuning</c> reads
/// that range from <c>progression.json#/legendLevel/min</c> and <c>/max</c>, and
/// <c>Player.Rehydrate</c> refuses a row outside it. <c>Core.Tests</c> is hermetic and proves the
/// invariant against a fixture that mirrors the shipped file, and the reader is internal to Core,
/// so this suite reads the real file directly via <see cref="RepoData"/> instead. Renaming a bound
/// in the data would otherwise leave the Core fixture green and every rehydration in the shipped
/// game throwing <c>MissingContentException</c>.
/// </remarks>
public sealed class LegendTuningMatchesTuningDataTests
{
    private const string ProgressionDocument = "tuning/progression.json";

    /// <summary>
    /// The two leaves <c>LegendTuning</c> reads. Keep in step with
    /// <c>ProgressionDocuments.ShippedLegendLevelMin</c>/<c>Max</c> in <c>SlayIdleRepeat.Core.Tests</c>,
    /// which drive the Core-side range assertions this file claims to mirror.
    /// </summary>
    public static TheoryData<string, int, string> AuthoredLegendLevelBounds => new()
    {
        { "min", 1, "07 §1.1 — a player starts at Legend Level 1" },
        { "max", 200, "07 §1.1 — the v1 ladder ends at 200" },
    };

    [Theory]
    [MemberData(nameof(AuthoredLegendLevelBounds))]
    public void The_legendLevel_block_authors_the_bound_the_design_docs_state(
        string leaf, int expected, string source)
    {
        var value = Read(LegendLevelBlock(), leaf);

        value.ValueKind.ShouldBe(
            JsonValueKind.Number,
            $"{ProgressionDocument}#/legendLevel/{leaf} is not a number. A null there is " +
            "game-data's unauthorised-hole marker, and LegendTuning refuses to read one as a " +
            "default (S6) — so every Player.Rehydrate in the game would throw " +
            "UnauthorisedTunableException instead of validating a level.");

        value.TryGetInt32(out var authored).ShouldBeTrue(
            $"{ProgressionDocument}#/legendLevel/{leaf} is fractional. LegendTuning reads it " +
            "through ContentSnapshot.ReadInt32, which throws ContentTypeMismatchException rather " +
            "than round a level 07 §1.1 authors as a whole number.");

        authored.ShouldBe(expected, $"{source}. Changing this bound changes which persisted players " +
            "load at all: update the design doc, this case and " +
            "SlayIdleRepeat.Core.Tests' ProgressionDocuments together.");
    }

    /// <summary>
    /// Floor: both leaves the reader reads are present, stated as a set rather than a count — the
    /// block also holds four other keys, so a count floor of two would hold even if both authored
    /// bounds vanished.
    /// </summary>
    [Fact]
    public void Every_leaf_the_legend_level_reader_reads_is_present_in_the_block()
    {
        var present = LegendLevelBlock().EnumerateObject().Select(p => p.Name).ToArray();
        var read = AuthoredLegendLevelBounds.Select(row => (string)row[0]).ToArray();

        read.Length.ShouldBe(2, "LegendTuning reads two pointers; this theory data has drifted.");
        read.Except(present, StringComparer.Ordinal).ShouldBeEmpty(
            $"a leaf LegendTuning reads is gone from {ProgressionDocument}#/legendLevel. It throws " +
            "MissingContentException without it, so no player rehydrates at all. The data moved — " +
            "fix the reader, do not delete the case.");
    }

    /// <summary>The level-up curve is authored and deliberately not read by the aggregate.</summary>
    /// <remarks>
    /// Computation stays off the aggregate: <c>LegendCurveTuning</c> reads these three leaves and
    /// <c>Rules.Hero.LegendLevelCurve</c> computes with them, while the aggregate holds only the
    /// range. Pinned here so the split is visible and the numbers stay greppable.
    /// <para>
    /// ⚠️ This used to say M1-04 stores neither the points nor the level-ups. M4-10 landed both —
    /// <c>Player.TalentPoints</c> and <c>Player.AdvanceLegendLevel</c> — so the sentence was
    /// corrected rather than left standing: a note telling a later task to add a field that already
    /// exists is worse than none.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_level_up_curve_is_authored_and_belongs_to_M4_10_rather_than_to_the_aggregate()
    {
        var block = LegendLevelBlock();

        Read(block, "xpCoefficient").GetDecimal().ShouldBe(120m, "07 §1.1 — Level 2 needs 120 XP.");
        // Asserted as a RANGE, not a fixed value: this is a balance-sensitive number expected to be
        // swept, and progression.json authors its own sweep bounds beside it. Pinning the exact
        // value would turn a balance sweep red in a file about the Legend Level invariant, which
        // reads nothing here.
        var sweep = Read(block, "xpExponentSweepRange");
        var exponent = Read(block, "xpExponent").GetDecimal();

        exponent.ShouldBeGreaterThanOrEqualTo(Read(sweep, "min").GetDecimal());
        exponent.ShouldBeLessThanOrEqualTo(Read(sweep, "max").GetDecimal());
        Read(block, "talentPointsPerLevel").GetInt32().ShouldBe(
            1, "09 §2 — one Talent Point per level. M4-10 grants and stores them.");
    }

    private static JsonElement Read(JsonElement parent, string member)
    {
        parent.TryGetProperty(member, out var value).ShouldBeTrue(
            $"{ProgressionDocument}#/legendLevel/…/{member} is gone. The Player aggregate's " +
            "invariant or its pin reads it; the data moved — fix the reader, do not delete the case.");

        return value;
    }

    private static JsonElement LegendLevelBlock()
    {
        using var document = JsonDocument.Parse(RepoData.Documents[ProgressionDocument]);

        document.RootElement.TryGetProperty("legendLevel", out var block).ShouldBeTrue(
            $"{ProgressionDocument}#/legendLevel is gone — the block every case in this file reads, " +
            "and the block Player.Rehydrate validates every persisted Legend Level against.");

        return block.Clone();
    }
}
