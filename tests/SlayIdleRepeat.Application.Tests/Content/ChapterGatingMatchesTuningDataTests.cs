using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Checks that `10` §7's chapter/tier ladder, as shipped in
/// <c>tuning/progression.json#/chapterGating</c>, cannot drift away from the fixture ladder
/// <c>SlayIdleRepeat.Core.Tests</c> gates against.
/// </summary>
/// <remarks>
/// <para>
/// <c>Core.Tests</c> is hermetic, and that hermeticism has a price this file pays. Both
/// <c>ChapterGatingTuningTests</c> and <c>StartRunChapterGateTests</c> run over
/// <c>TuningDocuments.Shipped</c>, whose <c>chapterGating</c> block is assembled from
/// <c>ProgressionDocuments.ShippedNormalRequiresClear</c> and its siblings. Nothing in that project
/// can see <c>game-data</c>, so an edit to the real ladder leaves the whole Core gate suite green
/// against a ladder the game no longer has — the gate would keep being proved correct about rungs
/// nobody ships. This is the other half: that one proves <c>StartRun</c> enforces a ladder
/// correctly, this one proves the ladder it enforces is the one `10` §7 authored.
/// </para>
/// <para>
/// Transcribed by hand rather than read off the document twice: a list derived from
/// <c>progression.json</c> would say the ladder is right because the ladder says so.
/// </para>
/// <para>
/// The tier <em>vocabulary</em> is deliberately not re-checked here.
/// <c>DifficultyTierMatchesTuningDataTests.DifficultyTier_is_exactly_the_chapterGating_keys_of_progression_json</c>
/// already pins the rung set against <see cref="DifficultyTier"/>; what this file adds is a floor
/// over the <em>transcription</em>, which that case cannot see.
/// </para>
/// </remarks>
public sealed class ChapterGatingMatchesTuningDataTests
{
    private const string ProgressionDocument = "tuning/progression.json";

    /// <summary>The member every authored block carries its prose under, and the one non-rung key here.</summary>
    private const string DocMember = "_doc";

    /// <summary>
    /// `10` §7's three rungs, transcribed by hand from the document: the clear each tier demands,
    /// and the Legend Level it demands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keep in step with <c>ProgressionDocuments.ShippedNormalRequiresClear</c>,
    /// <c>…ShippedHeroicRequiresClear</c>, <c>…ShippedMythicRequiresClear</c> and
    /// <c>…ShippedMythicRequiresLegendLevel</c> in <c>SlayIdleRepeat.Core.Tests</c>. That fixture is
    /// the other copy of these four values, and every Core case about this ladder gates against it.
    /// </para>
    /// <para>
    /// Two of the three levels are <c>null</c> on purpose and are pinned as <c>null</c>, not skipped:
    /// `10` §7 states outright that there is <b>no</b> level gate on Normal chapters, so a number
    /// appearing on the Normal or Heroic rung is a design change, not a tightening.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, int?, string> AuthoredRungs => new()
    {
        {
            nameof(DifficultyTier.NORMAL),
            "PREVIOUS_CHAPTER_NORMAL",
            null,
            "10 §7 — chapter c Normal is unlocked by clearing chapter c-1 Normal, and there is no " +
            "level gate on Normal chapters at all"
        },
        {
            nameof(DifficultyTier.HEROIC),
            "SAME_CHAPTER_NORMAL",
            null,
            "10 §7 — chapter c Heroic is unlocked by clearing chapter c Normal, and demands no level"
        },
        {
            nameof(DifficultyTier.MYTHIC),
            "SAME_CHAPTER_HEROIC",
            60,
            "10 §7 — chapter c Mythic is unlocked by clearing chapter c Heroic AND Legend Level 60"
        },
    };

    [Theory]
    [MemberData(nameof(AuthoredRungs))]
    public void The_chapterGating_block_authors_the_rung_10_section_7_states(
        string tier, string requiresClear, int? requiresLegendLevel, string source)
    {
        var rung = Rung(tier);

        var clear = Read(rung, tier, "requiresClear");
        clear.ValueKind.ShouldBe(
            JsonValueKind.String,
            $"{ProgressionDocument}#/chapterGating/{tier}/requiresClear is not a token. " +
            "ChapterGatingTuning refuses anything it cannot translate rather than reading it as " +
            "'this rung demands no clear', so the whole tier would stop loading.");

        clear.GetString().ShouldBe(
            requiresClear,
            $"{source}. The token is the ladder: change it and every Core case that gates on this " +
            "rung is describing a game that no longer exists. Update the design doc, this case and " +
            "SlayIdleRepeat.Core.Tests' ProgressionDocuments together.");

        var level = Read(rung, tier, "requiresLegendLevel");

        if (requiresLegendLevel is null)
        {
            level.ValueKind.ShouldBe(
                JsonValueKind.Null,
                $"{source}. A level authored on this rung is a locked door 10 §7 refuses to fit: " +
                "under-levelled players are meant to fail the chapter and go and grind, not be " +
                "turned away at the picker.");
            return;
        }

        level.ValueKind.ShouldBe(
            JsonValueKind.Number,
            $"{ProgressionDocument}#/chapterGating/{tier}/requiresLegendLevel is null. Read as 'no " +
            "level gate', the hardest tier in the game opens to a brand-new account.");

        level.GetInt32().ShouldBe(
            requiresLegendLevel.Value,
            $"{source}. StartRun refuses below this level with LEGEND_LEVEL_TOO_LOW, so moving it " +
            "moves when Mythic becomes reachable for every player: update the design doc, this case " +
            "and SlayIdleRepeat.Core.Tests' ProgressionDocuments together.");
    }

    /// <summary>
    /// 🔒 Floor: the transcription above covers every rung the document authors, and every rung it
    /// names is there — stated as a set, because a count of three is satisfied by three rungs none
    /// of which is the one carrying the Legend Level.
    /// </summary>
    /// <remarks>
    /// Without it, a rung added to <c>chapterGating</c> would be gated by <c>StartRun</c> and pinned
    /// by nothing, and a rung this theory names that had left the document would leave its row
    /// asserting over a block that no longer has it. The tier vocabulary itself is
    /// <c>DifficultyTierMatchesTuningDataTests</c>' job; this is the pin on the hand-written copy.
    /// </remarks>
    [Fact]
    public void Every_rung_the_document_authors_is_transcribed_and_every_transcribed_rung_is_authored()
    {
        var authored = Block().EnumerateObject()
            .Select(member => member.Name)
            .Where(name => !name.Equals(DocMember, StringComparison.Ordinal))
            .ToArray();

        var transcribed = AuthoredRungs.Select(row => (string)row[0]).ToArray();

        authored.Except(transcribed, StringComparer.Ordinal).ShouldBeEmpty(
            $"{ProgressionDocument}#/chapterGating authors a rung this file does not transcribe. " +
            "StartRun gates on it and nothing here says what it ought to demand — fix the " +
            "transcription, do not delete the case.");

        transcribed.Except(authored, StringComparer.Ordinal).ShouldBeEmpty(
            $"this file transcribes a rung {ProgressionDocument}#/chapterGating no longer authors. " +
            "Its row above is asserting over a block that has lost it, which is how a theory keeps " +
            "reporting on a ladder rung that is gone.");
    }

    /// <summary>
    /// The Mythic rung's Legend Level and <c>#/unlocks/MYTHIC_TIER</c> are one number authored twice,
    /// and they have to agree.
    /// </summary>
    /// <remarks>
    /// `07` §1.1's ladder puts the Mythic difficulty tier at Legend Level 60 and `10` §7 states the
    /// same 60 as the Mythic rung's demand — one design decision with two authored homes in one
    /// file. <c>UnlockTuning</c> reads the first and <c>ChapterGatingTuning</c> the second, so a
    /// divergence would have the screen that announces the unlock and the gate that enforces it
    /// disagreeing about when Mythic opens. Asserted against each other rather than against 60 twice:
    /// the number itself is already pinned above and in <c>HeroTuningMatchesTuningDataTests</c>, and
    /// what is unpinned is that the two move together.
    /// </remarks>
    [Fact]
    public void The_Mythic_rungs_Legend_Level_is_the_same_number_07_section_1_1_unlocks_the_tier_at()
    {
        using var document = JsonDocument.Parse(RepoData.Documents[ProgressionDocument]);

        var gate = document.RootElement
            .GetProperty("chapterGating")
            .GetProperty(nameof(DifficultyTier.MYTHIC))
            .GetProperty("requiresLegendLevel");

        document.RootElement.GetProperty("unlocks").TryGetProperty("MYTHIC_TIER", out var unlock)
            .ShouldBeTrue(
                $"{ProgressionDocument}#/unlocks/MYTHIC_TIER is gone — 07 §1.1's row for the Mythic " +
                "tier, and the value UnlockTuning reads. The data moved; fix the reader.");

        gate.GetInt32().ShouldBe(
            unlock.GetInt32(),
            "07 §1.1 unlocks the Mythic difficulty tier at one Legend Level and 10 §7 gates the " +
            "Mythic rung at one Legend Level, and they are the same decision written down twice. " +
            "Disagreeing, the unlock ladder would tell a player Mythic is theirs while StartRun " +
            "answered LEGEND_LEVEL_TOO_LOW — or the reverse, which is worse.");
    }

    private static JsonElement Read(JsonElement rung, string tier, string member)
    {
        rung.TryGetProperty(member, out var value).ShouldBeTrue(
            $"{ProgressionDocument}#/chapterGating/{tier}/{member} is gone. ChapterGatingTuning " +
            "reads it and refuses rather than defaulting, so no run could be started on any tier — " +
            "the data moved, fix the reader rather than deleting the case.");

        return value;
    }

    private static JsonElement Rung(string tier)
    {
        Block().TryGetProperty(tier, out var rung).ShouldBeTrue(
            $"{ProgressionDocument}#/chapterGating/{tier} is gone. ChapterGatingTuning requires a " +
            "rung for every declared DifficultyTier, so the ladder would not load at all.");

        return rung.Clone();
    }

    private static JsonElement Block()
    {
        using var document = JsonDocument.Parse(RepoData.Documents[ProgressionDocument]);

        document.RootElement.TryGetProperty("chapterGating", out var block).ShouldBeTrue(
            $"{ProgressionDocument}#/chapterGating is gone — 10 §7's ladder, the block every case in " +
            "this file reads and the one StartRun gates every run on.");

        return block.Clone();
    }
}
