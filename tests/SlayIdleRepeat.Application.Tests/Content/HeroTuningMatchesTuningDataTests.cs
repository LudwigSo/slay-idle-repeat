using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Checks that the hero's two authored tables — `07` §1.1's unlock ladder and `12` §2's free preset
/// allowance — cannot drift away from the readers that consume them.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic and proves the rules against fixtures that mirror these files; the
/// readers are internal to <c>Core</c>, so this suite reads the real documents instead. Neither half
/// is sufficient alone: that one proves the gate is right about a ladder, this one proves the shipped
/// ladder is the one the design docs authored.
/// </remarks>
public sealed class HeroTuningMatchesTuningDataTests
{
    private const string ProgressionDocument = "tuning/progression.json";

    private const string AdsDocument = "tuning/ads.json";

    /// <summary>
    /// `07` §1.1's unlock table, transcribed by hand from the document — the nine rows that name a
    /// system rather than the level cap.
    /// </summary>
    /// <remarks>
    /// Keep in step with <c>UnlockTuning.RowsAuthoredBy07</c> and with
    /// <c>ProgressionDocuments.ShippedUnlocks</c> in <c>SlayIdleRepeat.Core.Tests</c>. Transcribed
    /// rather than derived: a list read off <c>progression.json</c> would say the ladder is right
    /// because the ladder says so.
    /// </remarks>
    public static TheoryData<string, int, string> AuthoredUnlocks => new()
    {
        { "PET_SLOT_1", 5, "07 §1.1 — pet slot 1 and the Menagerie screen" },
        { "FORGE", 8, "07 §1.1 — the Forge (merge + enhance)" },
        { "PVP", 10, "07 §1.1 — PvP Ghost Duel" },
        { "PET_SLOT_2", 15, "07 §1.1 — pet slot 2" },
        { "MOUNT_SLOT", 20, "07 §1.1 — the mount slot" },
        { "PET_SLOT_3", 30, "07 §1.1 — pet slot 3" },
        { "TALENT_BRANCH_FORTUNE", 40, "07 §1.1 — talent tree branch 3 (Fortune)" },
        { "MYTHIC_TIER", 60, "07 §1.1 — the Mythic difficulty tier" },
        { "CODEX_MASTERY", 100, "07 §1.1 — Codex mastery bonuses" },
    };

    [Theory]
    [MemberData(nameof(AuthoredUnlocks))]
    public void The_unlocks_block_authors_the_rung_07_section_1_1_states(
        string unlock, int expected, string source)
    {
        var block = Block(ProgressionDocument, "unlocks");

        block.TryGetProperty(unlock, out var value).ShouldBeTrue(
            $"{ProgressionDocument}#/unlocks/{unlock} is gone. UnlockTuning REQUIRES every 07 §1.1 " +
            "row and throws MissingContentException without it, so the gate it guards would have " +
            "nothing to compare a Legend Level against.");

        value.ValueKind.ShouldBe(
            JsonValueKind.Number,
            $"{ProgressionDocument}#/unlocks/{unlock} is not a number. A null is game-data's " +
            "unauthorised-hole marker and the reader refuses to read one as a default (S6).");

        value.GetInt32().ShouldBe(
            expected,
            $"{source}. Changing a rung changes when a whole system becomes reachable: update the " +
            "design doc, this case and SlayIdleRepeat.Core.Tests' ProgressionDocuments together.");
    }

    /// <summary>
    /// The `07` §1.1 rows are a subset of the ladder rather than the whole of it — other documents
    /// author their own systems' rungs.
    /// </summary>
    /// <remarks>
    /// Both directions matter. The theory above proves every `07` §1.1 row is present at its level;
    /// this proves the reader is not quietly filtering the ladder down to those nine, which is what
    /// would make a system authored by `25`, `26` or `27` silently ungated.
    /// </remarks>
    [Fact]
    public void The_ladder_also_carries_rungs_other_documents_author()
    {
        var block = Block(ProgressionDocument, "unlocks");

        var rungs = block.EnumerateObject()
            .Where(member => !member.Name.StartsWith('_'))
            .Select(member => member.Name)
            .ToArray();

        rungs.Length.ShouldBeGreaterThan(
            AuthoredUnlocks.Count,
            "07 §1.1's nine rows are not the whole ladder — 25 authors DUNGEONS, 27 §2 GUILDS and " +
            "26 EVENTS. A ladder that shrank to exactly the nine means somebody removed them.");

        rungs.ShouldContain("DUNGEONS");
        rungs.ShouldContain("GUILDS");
        rungs.ShouldContain("EVENTS");
    }

    /// <summary>No rung sits above the Legend Level cap.</summary>
    /// <remarks>
    /// The invariant that replaces `07` §1.1's tenth row: the "200 | Level cap" row is the range's
    /// <c>max</c>, and a second copy of it under <c>#/unlocks</c> could disagree with itself. What
    /// the reader enforces instead is this, and this is the pin over the real file.
    /// </remarks>
    [Fact]
    public void No_rung_sits_above_the_authored_Legend_Level_cap()
    {
        var cap = Block(ProgressionDocument, "legendLevel").GetProperty("max").GetInt32();

        foreach (var rung in Block(ProgressionDocument, "unlocks").EnumerateObject())
        {
            if (rung.Name.StartsWith('_'))
            {
                continue;
            }

            rung.Value.GetInt32().ShouldBeInRange(
                1,
                cap,
                $"{ProgressionDocument}#/unlocks/{rung.Name} unlocks outside 1..{cap}, the range a " +
                "player can hold. A rung above the cap is a system no account can ever reach.");
        }
    }

    /// <summary>`12` §2's free preset allowance, where <c>SAVE_PRESET</c> reads it.</summary>
    [Fact]
    public void The_plus_block_authors_the_free_preset_allowance()
    {
        var plus = Block(AdsDocument, "plus");

        plus.TryGetProperty("freePresets", out var value).ShouldBeTrue(
            $"{AdsDocument}#/plus/freePresets is gone. It is the ONLY authored source of the " +
            "allowance — M1-02 left SavePresetCommand.PresetSlot unbounded rather than invent one — " +
            "so without it every SAVE_PRESET throws MissingContentException.");

        value.GetInt32().ShouldBe(
            3,
            "09 §2.1 and 12 §2 — three saved preset slots from the start, unlimited with Plus. " +
            "09 §53 permits raising this and forbids lowering it: presets are a core free feature.");
    }

    private static JsonElement Block(string documentPath, string member)
    {
        using var document = JsonDocument.Parse(RepoData.Documents[documentPath]);

        document.RootElement.TryGetProperty(member, out var block).ShouldBeTrue(
            $"{documentPath}#/{member} is gone — the block every case in this file reads.");

        return block.Clone();
    }
}
