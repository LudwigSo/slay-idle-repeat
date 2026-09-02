using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 <c>08</c> §2–§3 — the shipped gear documents author the numbers <c>Core.Tests</c>' hermetic
/// <c>GearDocuments</c> fixture transcribes.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This is the half <c>GearDocuments</c>' own remarks promised and nothing delivered.</b> That
/// fixture says the other half of the claim — that these <em>are</em> the numbers we ship — belongs
/// to <c>Application.Tests</c>, which resolves the same pointers in the real files. No such file
/// existed, so around fifteen hundred lines of <c>Core</c> gear assertions rested on a hand
/// transcription of three documents that nothing tied to <c>game-data/</c>. A number re-authored in
/// the shipped file and left in the fixture would have kept every gear case in the repository green
/// while the game shipped something else.
/// </para>
/// <para>
/// The <c>*MatchesTuningDataTests</c> files beside this one are the pattern, and this follows it
/// rather than inventing one: every pointer is written out as a literal here rather than shared with
/// the reader or the fixture — neither of which this assembly can see — so a rename in one place
/// without the other fails this suite instead of silently pointing both halves at the same wrong
/// leaf.
/// </para>
/// <para>
/// 🔒 <b>Member lists as well as values.</b> A value assertion catches a number that changed; only an
/// exact member list catches a leaf that was <em>added</em> — a tunable the readers ignore — or a row
/// appended to a table the fixture believes it enumerates. The rarity ladder's rung members and the
/// percent-stat table's stat names get one; the affix pool gets an exact ordered <em>id</em> list,
/// which is what its own reader indexes into.
/// </para>
/// <para>
/// ⚠️ <b>What is deliberately not pinned.</b> <c>drops.json</c>'s <c>containerContents</c> and
/// <c>acquisitionRates</c>, and <c>par_power.json</c>'s Heroic and Mythic columns and everything
/// below <c>parPower</c>. Nothing under <c>Rules/Gear/</c> reads them, so the fixture does not
/// transcribe them either, and pinning here what no reader resolves would assert a shape this claim
/// is not about. <c>containerContents</c> is M4-02's, with its own readers.
/// </para>
/// </remarks>
public sealed class GearDocumentsMatchesTuningDataTests
{
    private const string GearDocument = "content/gear/gear.json";
    private const string DropsDocument = "tuning/drops.json";
    private const string ParPowerDocument = "tuning/par_power.json";

    /// <summary>The five bands, bottom to top, in the order the ladder authors them.</summary>
    private static readonly string[] Bands = ["C", "B", "A", "S", "SS"];

    // ═════════════════════════════════════════════════════════ the rarity ladder

    /// <summary><c>08</c> §2 — each band's multiplier on item power and how many affixes it rolls.</summary>
    public static TheoryData<string, double, int> EveryAuthoredBand() => new()
    {
        { "C", 1.0, 0 },
        { "B", 1.55, 1 },
        { "A", 2.4, 2 },
        { "S", 3.8, 3 },
        { "SS", 6.0, 4 },
    };

    /// <summary><c>08</c> §2 — the five rungs of the ladder every generated item is banded on.</summary>
    [Theory]
    [MemberData(nameof(EveryAuthoredBand))]
    public void The_shipped_rarity_ladder_is_the_one_08_2_authors(
        string band, double statMultiplier, int affixCount)
    {
        var row = Rung(band);

        row.GetProperty("statMultiplier").GetDouble().ShouldBe(
            statMultiplier, $"08 §2: band {band}'s multiplier on item power.");
        row.GetProperty("affixCount").GetInt32().ShouldBe(
            affixCount, $"08 §2/§3.1: how many affixes band {band} rolls.");
    }

    /// <summary>The ladder's shape: five rungs, in order, each with the four leaves the reader reads.</summary>
    /// <remarks>
    /// ⚠️ The order pins the <b>document</b>, not the reader: <c>DropsTuning</c> keys the rungs by id,
    /// so a reordered file produces a byte-identical reader. It is pinned anyway because the document
    /// is what a human reads to check a band against the design set, and an unordered ladder is
    /// unreadable. The affix pool below is the table whose order a reader really does index into.
    /// </remarks>
    [Fact]
    public void The_shipped_rarity_ladder_is_ordered_and_carries_no_leaf_the_reader_ignores()
    {
        var rarities = Drops().GetProperty("rarities");

        rarities.EnumerateArray().Select(row => row.GetProperty("id").GetString()).ShouldBe(
            Bands, "08 §2 authors the ladder ascending.");

        rarities.EnumerateArray()
            .SelectMany(row => row.EnumerateObject().Select(member => member.Name))
            .Distinct(StringComparer.Ordinal)
            .ShouldBe(
                new[] { "id", "displayName", "statMultiplier", "affixCount" },
                ignoreOrder: true,
                "a leaf added to a ladder rung is a tunable DropsTuning does not read.");
    }

    // ═════════════════════════════════════════════════════════ item generation

    /// <summary><c>08</c> §3 — the item-power coefficient and the quality range's two scales.</summary>
    public static TheoryData<string, double> EveryGenerationLeaf() => new()
    {
        { "#/itemGeneration/itemPowerCoefficient", 0.1 },
        { "#/itemGeneration/quality/min", 0.0 },
        { "#/itemGeneration/quality/max", 1.0 },
        { "#/itemGeneration/quality/primaryScale/base", 0.9 },
        { "#/itemGeneration/quality/primaryScale/span", 0.2 },
        { "#/itemGeneration/quality/secondaryScale/base", 0.85 },
        { "#/itemGeneration/quality/secondaryScale/span", 0.3 },
    };

    /// <summary><c>08</c> §3 — every dial the item generator reads before it rolls anything.</summary>
    /// <remarks>
    /// 🔒 The two scale pairs are where a mistake is invisible: a primary base of 0.85 and a
    /// secondary of 0.9 swaps two numbers that are both plausible, both in range, and produce items
    /// wrong by a few percent for the life of the game.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryGenerationLeaf))]
    public void The_shipped_item_generation_block_is_the_one_08_3_authors(string pointer, double authored)
    {
        Leaf(DropsDocument, pointer).GetDouble().ShouldBe(authored, $"08 §3: {pointer}");
    }

    // ═════════════════════════════════════════════════════════ the drop table

    /// <summary><c>08</c> §3 — the four chapter bands and the shares each authors.</summary>
    public static TheoryData<int, int, int, double[]> EveryChapterBand() => new()
    {
        { 0, 1, 2, [60.0, 27.0, 10.0, 2.7, 0.3] },
        { 1, 3, 4, [40.0, 36.0, 18.0, 5.4, 0.6] },
        { 2, 5, 6, [15.0, 40.0, 32.0, 11.0, 2.0] },
        { 3, 7, 8, [0.0, 28.0, 44.0, 23.0, 5.0] },
    };

    /// <summary><c>08</c> §3 — the table a run drop's band is drawn from, banded by chapter.</summary>
    /// <remarks>
    /// The band bounds are pinned with the shares: a band that shipped as 1–3 rather than 1–2 would
    /// leave chapter 3 drawing the wrong table while every share in it still matched.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryChapterBand))]
    public void The_shipped_drop_shares_are_the_ones_08_3_authors(
        int index, int from, int to, double[] shares)
    {
        var row = Drops().GetProperty("dropShareByChapterBand")[index];

        row.GetProperty("chapterFrom").GetInt32().ShouldBe(from);
        row.GetProperty("chapterTo").GetInt32().ShouldBe(to);

        var share = row.GetProperty("share");

        share.EnumerateObject().Select(member => member.Name).ShouldBe(
            Bands, $"chapters {from}–{to} must author a share for every band and no other.");

        Bands.Select(band => share.GetProperty(band).GetDouble()).ShouldBe(
            shares, $"08 §3: what chapters {from}–{to} draw, band by band.");
    }

    /// <summary>Four bands, covering chapters 1–8 with no gap and no overlap.</summary>
    [Fact]
    public void The_shipped_drop_shares_cover_every_authored_chapter()
    {
        Drops().GetProperty("dropShareByChapterBand").GetArrayLength().ShouldBe(
            4, "08 §3 authors four chapter bands. A fifth is a chapter range no reader was written " +
            "against; a fourth removed is a chapter whose drops resolve to nothing.");
    }

    // ═════════════════════════════════════════════════════════ slots and stats

    /// <summary><c>08</c> §3 — the six slot rows: two stats each, and the coefficient of each.</summary>
    /// <remarks>A <see langword="null"/> coefficient is the authored "read the per-rarity table instead".</remarks>
    public static TheoryData<int, string, string, double?, string, double?> EverySlotRow() => new()
    {
        { 0, "WEAPON", "ATK", 0.2, "CRIT", null },
        { 1, "HELMET", "DEF", 0.18, "MAX_HP", 0.55 },
        { 2, "ARMOR", "MAX_HP", 1.1, "DEF", 0.12 },
        { 3, "BOOTS", "ASPD", null, "DODGE", null },
        { 4, "RING", "CRIT", null, "PEN", null },
        { 5, "AMULET", "MAX_HP", 0.45, "LIFESTEAL", null },
    };

    /// <summary><c>08</c> §3 — which two stats a slot carries and how strongly.</summary>
    /// <remarks>
    /// 🔒 <b>A <c>null</c> coefficient is an authored decision, not a hole</b>, and the two are told
    /// apart: it is the document saying <em>"this stat is a percentage, read
    /// <c>percentStatsByRarity</c> instead"</em>. An omitted key and an authored <c>null</c> mean
    /// different things to the reader, so presence is checked before the value.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EverySlotRow))]
    public void The_shipped_slot_coefficients_are_the_ones_08_3_authors(
        int index, string slot, string primary, double? primaryCoef, string secondary, double? secondaryCoef)
    {
        var row = Drops().GetProperty("slotCoefficients")[index];

        row.GetProperty("slot").GetString().ShouldBe(slot, "the rows are in slot order.");
        row.GetProperty("primaryStat").GetString().ShouldBe(primary, $"08 §3: {slot}'s primary stat.");
        row.GetProperty("secondaryStat").GetString().ShouldBe(secondary, $"08 §3: {slot}'s secondary stat.");

        Coefficient(row, "primaryCoef", primaryCoef, slot);
        Coefficient(row, "secondaryCoef", secondaryCoef, slot);
    }

    /// <summary>Every gear slot carries a row.</summary>
    [Fact]
    public void The_shipped_slot_coefficients_cover_every_slot()
    {
        Drops().GetProperty("slotCoefficients").GetArrayLength().ShouldBe(
            6, "08 §3 gives every one of the six gear slots a row; a slot without one derives no stats.");
    }

    /// <summary><c>08</c> §3.0a — the five percent stats, one value per band.</summary>
    public static TheoryData<string, double[]> EveryPercentStat() => new()
    {
        { "CRIT", [0.015, 0.025, 0.04, 0.06, 0.08] },
        { "ASPD", [0.02, 0.03, 0.045, 0.07, 0.1] },
        { "DODGE", [0.01, 0.015, 0.025, 0.04, 0.055] },
        { "PEN", [0.02, 0.035, 0.05, 0.08, 0.11] },
        { "LIFESTEAL", [0.015, 0.025, 0.04, 0.06, 0.08] },
    };

    /// <summary><c>08</c> §3.0a — a percent stat scales with the band and never with the chapter.</summary>
    [Theory]
    [MemberData(nameof(EveryPercentStat))]
    public void The_shipped_percent_stats_are_the_ones_08_3_0a_authors(string stat, double[] byBand)
    {
        var row = Drops().GetProperty("percentStatsByRarity").GetProperty(stat);

        row.EnumerateObject().Select(member => member.Name).ShouldBe(
            Bands, $"{stat} must author a value for every band and no other.");

        Bands.Select(band => row.GetProperty(band).GetDouble()).ShouldBe(
            byBand, $"08 §3.0a: {stat}, band by band.");
    }

    /// <summary>The percent-stat table holds exactly the five stats a null coefficient can point at.</summary>
    [Fact]
    public void The_shipped_percent_stat_table_holds_exactly_the_five_stats()
    {
        Drops().GetProperty("percentStatsByRarity")
            .EnumerateObject()
            .Select(member => member.Name)
            .Where(name => !name.StartsWith('_'))
            .ShouldBe(
                EveryPercentStat().Select(row => (string)row[0]),
                "a sixth stat here is one a slot row's null coefficient could point at and no " +
                "reader resolves; a fifth removed is a null coefficient that resolves to nothing.");
    }

    // ═════════════════════════════════════════════════════════ the affix pool

    /// <summary><c>08</c> §3.1 — the thirteen affixes: range, eligible slots, and any band floor.</summary>
    public static TheoryData<int, string, double, double, string[], string?> EveryAffix() => new()
    {
        { 0, "AFX_CRIT_CHANCE", 0.02, 0.08, ["WEAPON", "RING", "HELMET"], null },
        { 1, "AFX_CRIT_DAMAGE", 0.1, 0.35, ["WEAPON", "RING"], null },
        { 2, "AFX_ATTACK_SPEED", 0.03, 0.12, ["WEAPON", "BOOTS"], null },
        { 3, "AFX_PEN", 0.04, 0.15, ["WEAPON", "RING"], null },
        { 4, "AFX_MAX_HP", 0.05, 0.2, ["ARMOR", "HELMET", "AMULET"], null },
        { 5, "AFX_DEF", 0.05, 0.22, ["ARMOR", "HELMET", "BOOTS"], null },
        { 6, "AFX_DODGE", 0.02, 0.08, ["BOOTS", "AMULET"], null },
        { 7, "AFX_BLOCK", 0.03, 0.12, ["ARMOR", "HELMET"], null },
        { 8, "AFX_LIFESTEAL", 0.02, 0.09, ["AMULET", "WEAPON"], null },
        // D46 re-sign: a flat add onto the 1.0 damage-taken multiplier, so "2 to 8 points
        // less damage taken" authors as a negative range.
        { 9, "AFX_DAMAGE_REDUCTION", -0.08, -0.02, ["ARMOR", "AMULET"], null },
        { 10, "AFX_GOLD_GAIN", 0.08, 0.3, ["RING", "AMULET"], null },
        { 11, "AFX_PET_AURA_POWER", 0.05, 0.2, ["AMULET", "RING"], null },
        { 12, "AFX_DAMAGE_VS_ELITES", 0.08, 0.25, ["WEAPON", "RING"], null },
    };

    /// <summary><c>08</c> §3.1 — one affix as the pool authors it.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The slot lists carry more weight here than the ranges.</b> They are what makes a band's
    /// affix count reachable: only three of the thirteen allow <c>BOOTS</c>, against an SS count of
    /// four, which is the content defect M4-03 found and the conductor answered with a slot-aware
    /// cap. A slot quietly dropped from a list moves that boundary with nothing else noticing.
    /// </para>
    /// <para>
    /// ⚠️ Three ids are authored by the design set and the rest are derived from the same
    /// <c>AFX_&lt;STAT&gt;</c> convention. Pinned identically: the derivation was a ruling, and what
    /// this file asserts is what the document ships. <c>AFX_REROLL_CHARGE</c> was the fourteenth and
    /// is gone with the reroll charge it granted, so no affix carries a band floor any more.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryAffix))]
    public void The_shipped_affix_pool_is_the_one_08_3_1_authors(
        int index, string id, double min, double max, string[] slots, string? minRarity)
    {
        var row = Affixes()[index];

        row.GetProperty("id").GetString().ShouldBe(
            id, "the pool is ordered and GearAffixRoller indexes into that order.");
        row.GetProperty("min").GetDouble().ShouldBe(min, $"08 §3.1: {id}'s floor.");
        row.GetProperty("max").GetDouble().ShouldBe(max, $"08 §3.1: {id}'s ceiling.");

        row.GetProperty("slots").EnumerateArray().Select(slot => slot.GetString()).ShouldBe(
            slots,
            $"08 §3.1: the slots {id} may roll on. A slot dropped here shrinks the pool a band draws " +
            "from, which is where the SS-boots cap came from.");

        if (minRarity is null)
        {
            row.TryGetProperty("minRarity", out _).ShouldBeFalse(
                $"{id} authors no band floor. One added here is a restriction no reader was written " +
                "against.");

            return;
        }

        row.GetProperty("minRarity").GetString().ShouldBe(
            minRarity,
            $"{id} carries a band floor; without it the affix becomes reachable at every band that " +
            "rolls one.");
    }

    /// <summary>
    /// The damage-vs-Elites affix ships as the target-gated percent-add the fixture transcribes.
    /// </summary>
    /// <remarks>
    /// The one affix whose <c>stat</c>/<c>op</c> pair and gate are load-bearing enough to pin here:
    /// it was the pool's single null pair until the conditional standing-effect bucket made it
    /// authorable, and <c>GearDocuments</c> now transcribes the authored shape — so the real file
    /// must author the same one, or fifteen hundred lines of Core gear assertions are resting on a
    /// row the game does not ship.
    /// </remarks>
    [Fact]
    public void The_shipped_damage_vs_elites_affix_is_the_target_gated_shape_the_fixture_transcribes()
    {
        var row = Affixes()[12];

        row.GetProperty("id").GetString().ShouldBe("AFX_DAMAGE_VS_ELITES");
        row.GetProperty("stat").GetString().ShouldBe(
            "DMG_PCT", "the roll composes onto the damage multiplier, ×(1 + v) against elites");
        row.GetProperty("op").GetString().ShouldBe(
            "STAT_ADD_PCT", "the multiplier stat takes the percent bucket off its base 1.0");

        var condition = row.GetProperty("condition");
        condition.GetProperty("fn").GetString().ShouldBe("TARGET_IS_ELITE");
        condition.GetProperty("op").GetString().ShouldBe("eq");
        condition.GetProperty("value").GetBoolean().ShouldBeTrue();
    }

    /// <summary>The pool is thirteen — the number an SS roll of four draws against.</summary>
    /// <remarks>
    /// ⚠️ Thirteen, not the document's fourteen: <c>AFX_REROLL_CHARGE</c> is gone with the reroll
    /// charge it granted, and nothing was authored in its place.
    /// </remarks>
    [Fact]
    public void The_shipped_affix_pool_is_thirteen_deep()
    {
        Affixes().GetArrayLength().ShouldBe(
            13,
            "08 §3.1 authors fourteen affixes and one of them is removed, so the pool is thirteen — " +
            "and the pool's size is what a band's affix count is capped against per slot.");
    }

    // ═════════════════════════════════════════════════════════ sets

    /// <summary><c>08</c> §3.2 — the three set breakpoints, ascending.</summary>
    /// <remarks>
    /// Only the breakpoints are pinned. The four sets themselves author <c>id: null</c> — the design
    /// set names them and gives no id vocabulary — and an unauthored id is a hole this file must not
    /// fill with a plausible value.
    /// </remarks>
    [Fact]
    public void The_shipped_set_breakpoints_are_the_three_08_3_2_authors()
    {
        Drops().GetProperty("sets").GetProperty("breakpoints")
            .EnumerateArray().Select(point => point.GetInt32())
            .ShouldBe([2, 4, 6], "08 §3.2's set bonuses fire at two, four and six pieces.");
    }

    // ═════════════════════════════════════════════════════════ the base-item roster

    /// <summary><c>08</c> §1 — the twenty-four base items: six slots, four families each.</summary>
    public static TheoryData<int, string, int, string, string, string> EveryBaseItem()
    {
        var rows = new TheoryData<int, string, int, string, string, string>();
        var slots = new (string Slot, (string Id, string Family, string Axis)[] Families)[]
        {
            ("WEAPON", [("GEAR_WEAPON_BLADE", "BLADE", "BALANCED"), ("GEAR_WEAPON_AXE", "AXE", "HEAVY"), ("GEAR_WEAPON_STAFF", "STAFF", "CASTER"), ("GEAR_WEAPON_BOW", "BOW", "AGILE")]),
            ("HELMET", [("GEAR_HELMET_HOOD", "HOOD", "BALANCED"), ("GEAR_HELMET_HELM", "HELM", "HEAVY"), ("GEAR_HELMET_CIRCLET", "CIRCLET", "CASTER"), ("GEAR_HELMET_MASK", "MASK", "AGILE")]),
            ("ARMOR", [("GEAR_ARMOR_LEATHERS", "LEATHERS", "BALANCED"), ("GEAR_ARMOR_PLATE", "PLATE", "HEAVY"), ("GEAR_ARMOR_ROBE", "ROBE", "CASTER"), ("GEAR_ARMOR_SCALEMAIL", "SCALEMAIL", "AGILE")]),
            ("BOOTS", [("GEAR_BOOTS_TREADS", "TREADS", "BALANCED"), ("GEAR_BOOTS_GREAVES", "GREAVES", "HEAVY"), ("GEAR_BOOTS_SLIPPERS", "SLIPPERS", "CASTER"), ("GEAR_BOOTS_SANDALS", "SANDALS", "AGILE")]),
            ("RING", [("GEAR_RING_BAND", "BAND", "BALANCED"), ("GEAR_RING_SIGNET", "SIGNET", "HEAVY"), ("GEAR_RING_LOOP", "LOOP", "CASTER"), ("GEAR_RING_SEAL", "SEAL", "AGILE")]),
            ("AMULET", [("GEAR_AMULET_PENDANT", "PENDANT", "BALANCED"), ("GEAR_AMULET_TALISMAN", "TALISMAN", "HEAVY"), ("GEAR_AMULET_CHARM", "CHARM", "CASTER"), ("GEAR_AMULET_IDOL", "IDOL", "AGILE")]),
        };

        for (var slot = 0; slot < slots.Length; slot++)
        {
            for (var family = 0; family < slots[slot].Families.Length; family++)
            {
                var authored = slots[slot].Families[family];

                rows.Add(slot, slots[slot].Slot, family, authored.Id, authored.Family, authored.Axis);
            }
        }

        return rows;
    }

    /// <summary><c>08</c> §1 — one base item, as the catalogue authors it.</summary>
    /// <remarks>
    /// The family axis is pinned with the family because the axis is what a set is keyed on: a blade
    /// authored as HEAVY rather than BALANCED moves one item between two set engines and changes
    /// nothing else.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryBaseItem))]
    public void The_shipped_base_items_are_the_ones_08_1_authors(
        int slotIndex, string slot, int familyIndex, string id, string family, string axis)
    {
        var block = Gear().GetProperty("slots")[slotIndex];

        block.GetProperty("slot").GetString().ShouldBe(slot, "the slots are in 08 §1's order.");

        var row = block.GetProperty("families")[familyIndex];

        row.GetProperty("id").GetString().ShouldBe(id, $"08 §1: {slot}'s {family}.");
        row.GetProperty("family").GetString().ShouldBe(family);
        row.GetProperty("familyAxis").GetString().ShouldBe(
            axis, "08 §3.2 keys a set on the family axis, so this is which set the piece counts toward.");
    }

    /// <summary>Six slots, four families each — the grid, not a list.</summary>
    [Fact]
    public void The_shipped_base_item_roster_is_a_complete_grid()
    {
        var slots = Gear().GetProperty("slots");

        slots.GetArrayLength().ShouldBe(6, "08 §1 authors six gear slots.");

        slots.EnumerateArray()
            .Select(block => block.GetProperty("families").GetArrayLength())
            .ShouldAllBe(count => count == 4,
                "08 §1 gives every slot one family per axis. A slot with three leaves an axis with a " +
                "hole in it, and a set that can never be completed in that slot.");
    }

    // ═════════════════════════════════════════════════════════ the par curve

    /// <summary><c>17</c> §1 — the Normal column of the content par table.</summary>
    public static TheoryData<int, int> EveryParChapter() => new()
    {
        { 1, 1000 }, { 2, 2000 }, { 3, 4000 }, { 4, 8000 },
        { 5, 16000 }, { 6, 32000 }, { 7, 64000 }, { 8, 128000 },
    };

    /// <summary><c>17</c> §1 — what one chapter's content is scored against, and item power a tenth of.</summary>
    /// <remarks>
    /// The Normal column only: it is the one <c>ParPowerTuning</c> resolves for gear generation, and
    /// the Heroic and Mythic columns are the difficulty tiers' rather than the item's.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryParChapter))]
    public void The_shipped_par_power_normal_column_is_the_one_gear_generation_reads(
        int chapter, int normal)
    {
        var row = ParPower().GetProperty("parPower")[chapter - 1];

        row.GetProperty("chapter").GetInt32().ShouldBe(chapter, "the rows are in chapter order.");
        row.GetProperty("NORMAL").GetInt32().ShouldBe(
            normal, $"08 §3: an item found in chapter {chapter} is worth a tenth of this.");
    }

    /// <summary>Every authored chapter carries a par row.</summary>
    [Fact]
    public void The_shipped_par_table_covers_every_authored_chapter()
    {
        ParPower().GetProperty("parPower").GetArrayLength().ShouldBe(
            8, "chapters 1–8 each carry a par row; a chapter without one generates no item power.");
    }

    // ═════════════════════════════════════════════════════════ the floor

    /// <summary>Every block the cases above navigate from.</summary>
    public static TheoryData<string, string> EveryResolvedBlock() => new()
    {
        { DropsDocument, "rarities" },
        { DropsDocument, "dropShareByChapterBand" },
        { DropsDocument, "itemGeneration" },
        { DropsDocument, "slotCoefficients" },
        { DropsDocument, "percentStatsByRarity" },
        { DropsDocument, "affixPool" },
        { DropsDocument, "sets" },
        { GearDocument, "slots" },
        { ParPowerDocument, "parPower" },
    };

    /// <summary>
    /// 🔒 The floor under every case above (steering S3): all three documents exist and carry the
    /// blocks the cases resolve into.
    /// </summary>
    /// <remarks>
    /// Every case above navigates from a block. <see cref="Block"/> fails loudly on a missing
    /// document — but a document that existed while its blocks had been renamed would leave the cases
    /// resolving into nothing, and that is what this names. Stated by identity rather than count.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryResolvedBlock))]
    public void Every_block_the_gear_readers_resolve_is_present(string document, string block)
    {
        Block(document).TryGetProperty(block, out _).ShouldBeTrue(
            $"'{document}' authors no '{block}'. Cases in this file resolve into it and the Core " +
            "reader throws without it, so a rename here is a reader that fails at load and a " +
            "transcription that nothing checks.");
    }

    // ═════════════════════════════════════════════════════════ helpers

    /// <summary>Asserts one coefficient, telling an authored <c>null</c> apart from an omitted key.</summary>
    private static void Coefficient(JsonElement row, string member, double? expected, string slot)
    {
        row.TryGetProperty(member, out var value).ShouldBeTrue(
            $"'{slot}' omits '{member}'. An omitted key and an authored null are different things: " +
            "the second says the stat is a percentage read from percentStatsByRarity, and the first " +
            "says nothing at all.");

        if (expected is { } coefficient)
        {
            value.GetDouble().ShouldBe(coefficient, $"08 §3: {slot}'s {member}.");

            return;
        }

        value.ValueKind.ShouldBe(
            JsonValueKind.Null,
            $"{slot}'s {member} is authored null on purpose — 08 §3.0a's 'read the per-rarity table " +
            "instead'. A number here silently turns a capped percentage into a flat stat.");
    }

    private static JsonElement Rung(string band) =>
        Drops().GetProperty("rarities")
               .EnumerateArray()
               .Single(row => row.GetProperty("id").GetString() == band);

    private static JsonElement Affixes() => Drops().GetProperty("affixPool").GetProperty("affixes");

    private static JsonElement Drops() => Block(DropsDocument);

    private static JsonElement Gear() => Block(GearDocument);

    private static JsonElement ParPower() => Block(ParPowerDocument);

    /// <summary>Walks a <c>#/a/b</c> pointer from a document's root.</summary>
    private static JsonElement Leaf(string document, string pointer) =>
        pointer.TrimStart('#', '/')
               .Split('/')
               .Aggregate(Block(document), (element, step) => element.GetProperty(step));

    /// <summary>One shipped document's root, read off disk.</summary>
    /// <remarks>
    /// Parsed per call and cloned, because the <c>JsonDocument</c> is disposed — the same shape the
    /// <c>*MatchesTuningDataTests</c> files beside this one use.
    /// </remarks>
    private static JsonElement Block(string document)
    {
        RepoData.Documents.TryGetValue(document, out var text).ShouldBeTrue(
            $"'{document}' is not among the shipped documents. This whole file is the claim that " +
            "Core.Tests' GearDocuments transcribes what we ship; without the document there is " +
            "nothing to compare it to, and every case here would report success over a fixture " +
            "nobody had checked.");

        using var parsed = JsonDocument.Parse(text);

        return parsed.RootElement.Clone();
    }
}
