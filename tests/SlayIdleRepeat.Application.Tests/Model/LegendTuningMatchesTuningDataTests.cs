using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Model;

/// <summary>
/// 🔒 `21` §3.1 — the <c>Player</c> aggregate's Legend Level invariant and
/// <c>game-data/tuning/</c> cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// `30` §11.5 puts <em>"a Legend Level is inside the authored range"</em> on the aggregate, and
/// `07` §1.1 authors that range at <c>progression.json#/legendLevel/min</c> and <c>/max</c>.
/// <c>LegendTuning</c> reads exactly those two pointers, and <c>Player.Rehydrate</c> refuses a row
/// outside them.
/// </para>
/// <para>
/// Neither half can see the other, for the reason its energy sibling documents: <c>Core.Tests</c>
/// is hermetic and proves the invariant against a fixture that <em>mirrors</em> the shipped file,
/// while <c>LegendTuning</c> is <c>internal</c> to <c>Core</c> so this suite cannot call it. This
/// file closes the seam — the real file still authors those pointers, with the numbers `07` §1.1
/// states, as whole numbers the reader can actually read. Renaming <c>max</c> in the data would
/// otherwise leave the Core fixture green and every rehydration in the shipped game throwing
/// <c>MissingContentException</c>.
/// </para>
/// <para>
/// Same mechanism as <c>Rules/Economy/EnergyTuningMatchesTuningDataTests</c>: it reads files, which
/// is why it lives here. No adapter, no port, no container, no network.
/// </para>
/// </remarks>
public sealed class LegendTuningMatchesTuningDataTests
{
    private const string ProgressionDocument = "tuning/progression.json";

    /// <summary>
    /// The two leaves <c>LegendTuning</c> reads, with the values `07` §1.1 authors.
    /// 🔒 Keep in step with <c>ProgressionDocuments.ShippedLegendLevelMin</c>/<c>Max</c> in
    /// <c>SlayIdleRepeat.Core.Tests</c>: those drive the Core-side range assertions, this pins the
    /// file they claim to mirror.
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
    /// 🔒 S3 — the floor. Both leaves the reader reads are present, stated as a set rather than a
    /// count: the block also holds <c>xpCoefficient</c>, <c>xpExponent</c>,
    /// <c>xpExponentSweepRange</c> and <c>talentPointsPerLevel</c>, so a count floor of two holds
    /// while both authored bounds vanish.
    /// </summary>
    [Fact]
    public void Every_leaf_the_legend_level_reader_reads_is_present_in_the_block()
    {
        var present = LegendLevelBlock().EnumerateObject().Select(p => p.Name).ToArray();
        var read = AuthoredLegendLevelBounds.Select(row => (string)row[0]!).ToArray();

        read.Length.ShouldBe(2, "LegendTuning reads two pointers; this theory data has drifted.");
        read.Except(present, StringComparer.Ordinal).ShouldBeEmpty(
            $"a leaf LegendTuning reads is gone from {ProgressionDocument}#/legendLevel. It throws " +
            "MissingContentException without it, so no player rehydrates at all. The data moved — " +
            "fix the reader, do not delete the case.");
    }

    /// <summary>
    /// ⚠️ The level-up <b>curve</b> is authored and deliberately <b>not</b> read by the aggregate.
    /// </summary>
    /// <remarks>
    /// `30` §11.5 keeps computation off the aggregate, and `07` §1's curve, grants and unlock ladder
    /// are M4-10's. Pinned here so the deferral is visible and the numbers stay greppable (S6) —
    /// and because `21` §12 calls <c>xpExponent</c> "the highest-suspicion number in the whole
    /// economy", which makes an accidental read of it from a validation path worth catching.
    /// </remarks>
    [Fact]
    public void The_level_up_curve_is_authored_and_belongs_to_M4_10_rather_than_to_the_aggregate()
    {
        var block = LegendLevelBlock();

        Read(block, "xpCoefficient").GetDecimal().ShouldBe(120m, "07 §1.1 — Level 2 needs 120 XP.");
        // ⚠️ Asserted as a RANGE, not as 1.05. 21 §12 calls this "the highest-suspicion number in
        // the whole economy — sweep it first", and progression.json authors its own sweep bounds
        // beside it. Pinning the exact value would turn the first balance sweep red inside a file
        // about the Player aggregate's Legend Level invariant, which reads nothing here.
        var sweep = Read(block, "xpExponentSweepRange");
        var exponent = Read(block, "xpExponent").GetDecimal();

        exponent.ShouldBeGreaterThanOrEqualTo(Read(sweep, "min").GetDecimal());
        exponent.ShouldBeLessThanOrEqualTo(Read(sweep, "max").GetDecimal());
        Read(block, "talentPointsPerLevel").GetInt32().ShouldBe(
            1, "09 §2 — one Talent Point per level. M4-10 grants them; M1-04 stores neither.");
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
