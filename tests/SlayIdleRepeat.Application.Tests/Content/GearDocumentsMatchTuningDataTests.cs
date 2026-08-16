using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 <c>08</c> §2–§3 — the shipped gear documents author the numbers <c>Core.Tests</c>' hermetic
/// fixture transcribes.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This is the half <c>GearDocuments</c>' own remarks promised and nothing delivered.</b> That
/// fixture says <em>"the other half of the claim — that these ARE the numbers we ship — belongs to
/// <c>Application.Tests</c>, which resolves the same pointers in the real files"</em>, and no such
/// file existed. Around fifteen hundred lines of <c>Core</c> assertions — item power, slot
/// coefficients, the fourteen affixes, the chapter-banded drop shares, the four set engines — rested
/// on a hand transcription of three documents that nothing tied to <c>game-data/</c>. A number
/// re-authored in the shipped file and left in the fixture would have kept every gear case in the
/// repository green while the game shipped something else.
/// </para>
/// <para>
/// The three <c>*MatchesTuningDataTests</c> files beside this one are the pattern, and this follows
/// it rather than inventing one: every pointer is written out as a literal here rather than shared
/// with the reader or the fixture — neither of which this assembly can see — so a rename in one place
/// without the other fails this suite instead of silently pointing both halves at the same wrong
/// leaf.
/// </para>
/// <para>
/// 🔒 <b>Member lists as well as values.</b> A value assertion catches a number that changed; only an
/// exact member list catches a leaf that was <em>added</em> — a tunable the readers ignore — or a row
/// appended to a table the fixture believes it enumerates. Both tables the generator draws against
/// (the rarity ladder and the affix pool) get one.
/// </para>
/// <para>
/// ⚠️ <b>What is deliberately not pinned.</b> <c>drops.json</c>'s <c>containerContents</c> and
/// <c>acquisitionRates</c>, and <c>par_power.json</c>'s Heroic and Mythic columns and everything
/// below <c>parPower</c>. Nothing under <c>Rules/Gear/</c> reads them, so the fixture does not
/// transcribe them either, and pinning here what no reader resolves would assert a shape this claim
/// is not about. <c>containerContents</c> is M4-02's, with its own readers.
/// </para>
/// </remarks>
public sealed class GearDocumentsMatchTuningDataTests
{
    private const string GearDocument = "content/gear/gear.json";
    private const string DropsDocument = "tuning/drops.json";
    private const string ParPowerDocument = "tuning/par_power.json";

    /// <summary>The five bands, bottom to top, in the order the ladder authors them.</summary>
    private static readonly string[] Bands = ["C", "B", "A", "S", "SS"];

    // ═════════════════════════════════════════════════════════ the rarity ladder

    /// <summary><c>08</c> §2 — the five bands, their multipliers on item power and their affix counts.</summary>
    /// <remarks>
    /// The order is asserted, not just the membership: <c>DropsTuning</c> reads the ladder as a
    /// sequence and the bands are compared by ordinal elsewhere, so a document that authored SS
    /// before C would be a different ladder carrying the same numbers.
    /// </remarks>
    [Fact]
    public void The_shipped_rarity_ladder_is_the_five_bands_08_2_authors()
    {
        var rarities = Drops().GetProperty("rarities");

        rarities.EnumerateArray().Select(row => row.GetProperty("id").GetString()).ShouldBe(
            Bands,
            "08 §2's ladder is ordered, and DropsTuning reads it as one. A reordered document is a " +
            "different ladder wearing the same numbers.");

        var multipliers = new[] { 1.0, 1.55, 2.4, 3.8, 6.0 };
        var affixCounts = new[] { 0, 1, 2, 3, 4 };

        for (var band = 0; band < Bands.Length; band++)
        {
            var row = rarities[band];

            row.GetProperty("statMultiplier").GetDouble().ShouldBe(
                multipliers[band], $"08 §2: band {Bands[band]}'s multiplier on item power.");
            row.GetProperty("affixCount").GetInt32().ShouldBe(
                affixCounts[band], $"08 §2/§3.1: how many affixes band {Bands[band]} rolls.");
        }

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
    /// <remarks>
    /// 🔒 The two scale pairs are the ones a mistake is invisible in: a primary base of 0.85 and a
    /// secondary of 0.9 would swap two numbers that are both plausible, both in range, and produce
    /// items that are wrong by a few percent for the life of the game.
    /// </remarks>
    [Fact]
    public void The_shipped_item_generation_block_is_the_one_08_3_authors()
    {
        var generation = Drops().GetProperty("itemGeneration");

        generation.GetProperty("itemPowerCoefficient").GetDouble().ShouldBe(
            0.1, "08 §3: the fraction of a chapter's power target one item carries.");

        var quality = generation.GetProperty("quality");

        quality.GetProperty("min").GetDouble().ShouldBe(0.0);
        quality.GetProperty("max").GetDouble().ShouldBe(1.0);

        quality.GetProperty("primaryScale").GetProperty("base").GetDouble().ShouldBe(0.9);
        quality.GetProperty("primaryScale").GetProperty("span").GetDouble().ShouldBe(0.2);
        quality.GetProperty("secondaryScale").GetProperty("base").GetDouble().ShouldBe(0.85);
        quality.GetProperty("secondaryScale").GetProperty("span").GetDouble().ShouldBe(0.3);
    }

    // ═════════════════════════════════════════════════════════ the drop table

    /// <summary><c>08</c> §3 — the four chapter bands and the shares each authors.</summary>
    /// <remarks>
    /// The band bounds are pinned with the shares. A band that shipped as 1–3 rather than 1–2 would
    /// leave chapter 3 drawing the wrong table while every share below still matched.
    /// </remarks>
    [Fact]
    public void The_shipped_drop_shares_are_the_four_bands_08_3_authors()
    {
        var bands = Drops().GetProperty("dropShareByChapterBand");

        bands.GetArrayLength().ShouldBe(4, "08 §3 authors four chapter bands, covering chapters 1–8.");

        var expected = new[]
        {
            (From: 1, To: 2, Shares: new[] { 60.0, 27.0, 10.0, 2.7, 0.3 }),
            (From: 3, To: 4, Shares: new[] { 40.0, 36.0, 18.0, 5.4, 0.6 }),
            (From: 5, To: 6, Shares: new[] { 15.0, 40.0, 32.0, 11.0, 2.0 }),
            (From: 7, To: 8, Shares: new[] { 0.0, 28.0, 44.0, 23.0, 5.0 }),
        };

        for (var index = 0; index < expected.Length; index++)
        {
            var row = bands[index];
            var (from, to, shares) = expected[index];

            row.GetProperty("chapterFrom").GetInt32().ShouldBe(from);
            row.GetProperty("chapterTo").GetInt32().ShouldBe(to);

            var share = row.GetProperty("share");

            for (var band = 0; band < Bands.Length; band++)
            {
                share.GetProperty(Bands[band]).GetDouble().ShouldBe(
                    shares[band],
                    $"08 §3: chapters {from}–{to} draw {Bands[band]} at this share.");
            }
        }
    }

    // ═════════════════════════════════════════════════════════ slots and stats

    /// <summary>
    /// <c>08</c> §3 — the six slot rows: which two stats each slot carries, and the coefficient of
    /// each.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>A <c>null</c> coefficient is an authored decision, not a hole</b>, and the two are
    /// asserted apart: it is the document saying <em>"this stat is a percentage, read
    /// <c>percentStatsByRarity</c> instead"</em>. An omitted key and an authored <c>null</c> mean
    /// different things to the reader, so presence is checked before the value.
    /// </remarks>
    [Fact]
    public void The_shipped_slot_coefficients_are_the_six_rows_08_3_authors()
    {
        var rows = Drops().GetProperty("slotCoefficients");

        rows.GetArrayLength().ShouldBe(6, "08 §1 authors six gear slots and every one carries a row.");

        var expected = new (string Slot, string Primary, double? PrimaryCoef, string Secondary, double? SecondaryCoef)[]
        {
            ("WEAPON", "ATK", 0.2, "CRIT", null),
            ("HELMET", "DEF", 0.18, "MAX_HP", 0.55),
            ("ARMOR", "MAX_HP", 1.1, "DEF", 0.12),
            ("BOOTS", "ASPD", null, "DODGE", null),
            ("RING", "CRIT", null, "PEN", null),
            ("AMULET", "MAX_HP", 0.45, "LIFESTEAL", null),
        };

        for (var index = 0; index < expected.Length; index++)
        {
            var row = rows[index];
            var authored = expected[index];

            row.GetProperty("slot").GetString().ShouldBe(authored.Slot);
            row.GetProperty("primaryStat").GetString().ShouldBe(authored.Primary);
            row.GetProperty("secondaryStat").GetString().ShouldBe(authored.Secondary);

            Coefficient(row, "primaryCoef", authored.PrimaryCoef, authored.Slot);
            Coefficient(row, "secondaryCoef", authored.SecondaryCoef, authored.Slot);
        }
    }

    /// <summary><c>08</c> §3.0a — the five percent stats, one value per band.</summary>
    /// <remarks>
    /// These scale with the band and never with the chapter, which is why they are a table of their
    /// own rather than a coefficient: they feed capped percentages.
    /// </remarks>
    [Fact]
    public void The_shipped_percent_stats_are_the_five_08_3_0a_authors()
    {
        var table = Drops().GetProperty("percentStatsByRarity");

        var expected = new (string Stat, double[] ByBand)[]
        {
            ("CRIT", [0.015, 0.025, 0.04, 0.06, 0.08]),
            ("ASPD", [0.02, 0.03, 0.045, 0.07, 0.1]),
            ("DODGE", [0.01, 0.015, 0.025, 0.04, 0.055]),
            ("PEN", [0.02, 0.035, 0.05, 0.08, 0.11]),
            ("LIFESTEAL", [0.015, 0.025, 0.04, 0.06, 0.08]),
        };

        table.EnumerateObject()
            .Select(member => member.Name)
            .Where(name => !name.StartsWith('_'))
            .ShouldBe(
                expected.Select(row => row.Stat),
                "the percent-stat table is exactly these five. A sixth is a stat the slot rows can " +
                "point a null coefficient at and nothing reads.");

        foreach (var (stat, byBand) in expected)
        {
            var row = table.GetProperty(stat);

            for (var band = 0; band < Bands.Length; band++)
            {
                row.GetProperty(Bands[band]).GetDouble().ShouldBe(
                    byBand[band], $"08 §3.0a: {stat} at band {Bands[band]}.");
            }
        }
    }

    // ═════════════════════════════════════════════════════════ the affix pool

    /// <summary>
    /// <c>08</c> §3.1 — the fourteen affixes: the ids, their ranges, the slots each may roll on, and
    /// the one rarity floor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The slot lists carry more weight here than the ranges.</b> They are what makes a band's
    /// affix count reachable or not: only three of the fourteen allow <c>BOOTS</c>, against an SS
    /// count of four, which is the content defect M4-03 found and the conductor answered with a
    /// slot-aware cap. A slot quietly dropped from a list moves that boundary with nothing else
    /// noticing.
    /// </para>
    /// <para>
    /// ⚠️ Three ids are authored by the design set and eleven are derived from the same
    /// <c>AFX_&lt;STAT&gt;</c> convention. Pinned identically: the derivation was a ruling, and what
    /// this file asserts is what the document ships.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_shipped_affix_pool_is_the_fourteen_08_3_1_authors()
    {
        var affixes = Drops().GetProperty("affixPool").GetProperty("affixes");

        affixes.GetArrayLength().ShouldBe(
            14, "08 §3.1 authors fourteen affixes. The pool's size is what an SS roll of four draws " +
            "against.");

        var expected = new (string Id, double Min, double Max, string[] Slots, string? MinRarity)[]
        {
            ("AFX_CRIT_CHANCE", 0.02, 0.08, ["WEAPON", "RING", "HELMET"], null),
            ("AFX_CRIT_DAMAGE", 0.1, 0.35, ["WEAPON", "RING"], null),
            ("AFX_ATTACK_SPEED", 0.03, 0.12, ["WEAPON", "BOOTS"], null),
            ("AFX_PEN", 0.04, 0.15, ["WEAPON", "RING"], null),
            ("AFX_MAX_HP", 0.05, 0.2, ["ARMOR", "HELMET", "AMULET"], null),
            ("AFX_DEF", 0.05, 0.22, ["ARMOR", "HELMET", "BOOTS"], null),
            ("AFX_DODGE", 0.02, 0.08, ["BOOTS", "AMULET"], null),
            ("AFX_BLOCK", 0.03, 0.12, ["ARMOR", "HELMET"], null),
            ("AFX_LIFESTEAL", 0.02, 0.09, ["AMULET", "WEAPON"], null),
            ("AFX_DAMAGE_REDUCTION", 0.02, 0.08, ["ARMOR", "AMULET"], null),
            ("AFX_GOLD_GAIN", 0.08, 0.3, ["RING", "AMULET"], null),
            ("AFX_PET_AURA_POWER", 0.05, 0.2, ["AMULET", "RING"], null),
            ("AFX_REROLL_CHARGE", 1, 1, ["RING", "AMULET"], "S"),
            ("AFX_DAMAGE_VS_ELITES", 0.08, 0.25, ["WEAPON", "RING"], null),
        };

        affixes.EnumerateArray().Select(row => row.GetProperty("id").GetString()).ShouldBe(
            expected.Select(row => row.Id),
            "the pool is ordered and GearAffixRoller draws against it by index.");

        for (var index = 0; index < expected.Length; index++)
        {
            var row = affixes[index];
            var authored = expected[index];

            row.GetProperty("min").GetDouble().ShouldBe(authored.Min, $"08 §3.1: {authored.Id} floor.");
            row.GetProperty("max").GetDouble().ShouldBe(authored.Max, $"08 §3.1: {authored.Id} ceiling.");

            row.GetProperty("slots").EnumerateArray().Select(slot => slot.GetString()).ShouldBe(
                authored.Slots,
                $"08 §3.1: the slots {authored.Id} may roll on. A slot dropped here shrinks the pool " +
                "a band draws from, which is where the SS-boots cap came from.");

            if (authored.MinRarity is { } floor)
            {
                row.GetProperty("minRarity").GetString().ShouldBe(
                    floor,
                    $"{authored.Id} is the one affix carrying a band floor; without it the affix " +
                    "becomes reachable at every band that rolls one.");
            }
            else
            {
                row.TryGetProperty("minRarity", out _).ShouldBeFalse(
                    $"{authored.Id} authors no floor. A floor added here is a restriction no reader " +
                    "was written against.");
            }
        }
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
    /// <remarks>
    /// The family axis is pinned with the family because the axis is what a set is keyed on: a
    /// blade authored as HEAVY rather than BALANCED moves one item between two set engines and
    /// changes nothing else.
    /// </remarks>
    [Fact]
    public void The_shipped_base_items_are_the_twenty_four_08_1_authors()
    {
        var slots = Gear().GetProperty("slots");

        slots.GetArrayLength().ShouldBe(6, "08 §1 authors six gear slots.");

        var expected = new (string Slot, (string Id, string Family, string Axis)[] Families)[]
        {
            ("WEAPON", [("GEAR_WEAPON_BLADE", "BLADE", "BALANCED"), ("GEAR_WEAPON_AXE", "AXE", "HEAVY"), ("GEAR_WEAPON_STAFF", "STAFF", "CASTER"), ("GEAR_WEAPON_BOW", "BOW", "AGILE")]),
            ("HELMET", [("GEAR_HELMET_HOOD", "HOOD", "BALANCED"), ("GEAR_HELMET_HELM", "HELM", "HEAVY"), ("GEAR_HELMET_CIRCLET", "CIRCLET", "CASTER"), ("GEAR_HELMET_MASK", "MASK", "AGILE")]),
            ("ARMOR", [("GEAR_ARMOR_LEATHERS", "LEATHERS", "BALANCED"), ("GEAR_ARMOR_PLATE", "PLATE", "HEAVY"), ("GEAR_ARMOR_ROBE", "ROBE", "CASTER"), ("GEAR_ARMOR_SCALEMAIL", "SCALEMAIL", "AGILE")]),
            ("BOOTS", [("GEAR_BOOTS_TREADS", "TREADS", "BALANCED"), ("GEAR_BOOTS_GREAVES", "GREAVES", "HEAVY"), ("GEAR_BOOTS_SLIPPERS", "SLIPPERS", "CASTER"), ("GEAR_BOOTS_SANDALS", "SANDALS", "AGILE")]),
            ("RING", [("GEAR_RING_BAND", "BAND", "BALANCED"), ("GEAR_RING_SIGNET", "SIGNET", "HEAVY"), ("GEAR_RING_LOOP", "LOOP", "CASTER"), ("GEAR_RING_SEAL", "SEAL", "AGILE")]),
            ("AMULET", [("GEAR_AMULET_PENDANT", "PENDANT", "BALANCED"), ("GEAR_AMULET_TALISMAN", "TALISMAN", "HEAVY"), ("GEAR_AMULET_CHARM", "CHARM", "CASTER"), ("GEAR_AMULET_IDOL", "IDOL", "AGILE")]),
        };

        for (var index = 0; index < expected.Length; index++)
        {
            var block = slots[index];
            var (slot, families) = expected[index];

            block.GetProperty("slot").GetString().ShouldBe(slot);

            var authored = block.GetProperty("families");

            authored.GetArrayLength().ShouldBe(
                4,
                $"08 §1 gives every slot four families, one per axis; {slot} is the grid, not a list.");

            for (var family = 0; family < families.Length; family++)
            {
                var row = authored[family];

                row.GetProperty("id").GetString().ShouldBe(families[family].Id);
                row.GetProperty("family").GetString().ShouldBe(families[family].Family);
                row.GetProperty("familyAxis").GetString().ShouldBe(
                    families[family].Axis,
                    "08 §3.2 keys a set on the family axis, so an axis is which set the piece counts " +
                    "toward.");
            }
        }
    }

    // ═════════════════════════════════════════════════════════ the par curve

    /// <summary>
    /// <c>17</c> §1 — the Normal column of the content par table, which item power is a fraction of.
    /// </summary>
    /// <remarks>
    /// The Normal column only: it is the one <c>ParPowerTuning</c> resolves for gear generation, and
    /// the Heroic and Mythic columns are the difficulty tiers' rather than the item's.
    /// </remarks>
    [Fact]
    public void The_shipped_par_power_normal_column_is_the_one_gear_generation_reads()
    {
        var rows = ParPower().GetProperty("parPower");

        rows.GetArrayLength().ShouldBe(8, "chapters 1–8 each carry a par row.");

        var expected = new[] { 1000, 2000, 4000, 8000, 16000, 32000, 64000, 128000 };

        for (var index = 0; index < expected.Length; index++)
        {
            rows[index].GetProperty("chapter").GetInt32().ShouldBe(index + 1);
            rows[index].GetProperty("NORMAL").GetInt32().ShouldBe(
                expected[index],
                $"08 §3: an item found in chapter {index + 1} is worth a tenth of this.");
        }
    }

    // ═════════════════════════════════════════════════════════ the floor

    /// <summary>
    /// 🔒 The floor under every case above (steering S3): all three documents exist and carry the
    /// blocks the cases resolve into.
    /// </summary>
    /// <remarks>
    /// Every case above navigates from a block. <see cref="Block"/> throws rather than answering an
    /// empty element on a missing document, so a moved file fails loudly — but a document that
    /// existed while its blocks had been renamed would leave the cases resolving into nothing, and
    /// that is what this names. Stated by identity rather than count.
    /// </remarks>
    [Fact]
    public void All_three_gear_documents_are_present_and_carry_the_blocks_the_readers_resolve()
    {
        foreach (var block in new[]
                 {
                     "rarities", "dropShareByChapterBand", "itemGeneration", "slotCoefficients",
                     "percentStatsByRarity", "affixPool", "sets",
                 })
        {
            Drops().TryGetProperty(block, out _).ShouldBeTrue(
                $"'{DropsDocument}' authors no '{block}'. Every case in this file resolves into a " +
                "block of it, and DropsTuning throws without one — so a rename here is a reader that " +
                "fails at load and a transcription that nothing checks.");
        }

        Gear().TryGetProperty("slots", out _).ShouldBeTrue(
            $"'{GearDocument}' authors no 'slots', so the base-item grid GearCatalogue reads is gone.");

        ParPower().TryGetProperty("parPower", out _).ShouldBeTrue(
            $"'{ParPowerDocument}' authors no 'parPower', so the curve item power is a fraction of " +
            "is gone.");
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
        }
        else
        {
            value.ValueKind.ShouldBe(
                JsonValueKind.Null,
                $"{slot}'s {member} is authored null on purpose — 08 §3.0a's 'read the per-rarity " +
                "table instead'. A number here silently turns a capped percentage into a flat stat.");
        }
    }

    private static JsonElement Drops() => Block(DropsDocument);

    private static JsonElement Gear() => Block(GearDocument);

    private static JsonElement ParPower() => Block(ParPowerDocument);

    /// <summary>One shipped document's root, read off disk.</summary>
    /// <remarks>
    /// Parsed per call and cloned, because the <c>JsonDocument</c> is disposed — the same shape the
    /// three <c>*MatchesTuningDataTests</c> files beside this one use.
    /// </remarks>
    private static JsonElement Block(string document)
    {
        RepoData.Documents.TryGetValue(document, out var text).ShouldBeTrue(
            $"'{document}' is not among the shipped documents. This whole file is the claim that " +
            "Core.Tests' GearDocuments transcribes what we ship; without the document there is " +
            "nothing to compare it to, and every case here would report success over a fixture " +
            "nobody had checked.");

        using var parsed = JsonDocument.Parse(text!);

        return parsed.RootElement.Clone();
    }
}
