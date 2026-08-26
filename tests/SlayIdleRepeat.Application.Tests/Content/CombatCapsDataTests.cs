using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>Tests the shipped <c>content/combat_caps.json</c> values against the numbers the design docs authorise.</summary>
/// <remarks>
/// <c>SlayIdleRepeat.Core.Tests</c> references only <c>SlayIdleRepeat.Core</c> and has no JSON
/// reader, so it can't see this document; this suite is where the numbers get checked. Every value
/// is read with <see cref="ContentSnapshot.ReadDouble"/>, which throws on null, so a quietly nulled
/// cap fails here rather than reading as zero.
/// </remarks>
public sealed class CombatCapsDataTests
{
    private const string Document = "content/combat_caps.json";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    [Fact]
    public void The_document_is_in_the_snapshot_and_under_content_rather_than_tuning()
    {
        var snapshot = Data();

        snapshot.DocumentPaths.ShouldContain(Document);

        snapshot.DocumentPaths
            .Count(p => p.StartsWith("tuning/", StringComparison.Ordinal))
            .ShouldBe(16, "21 §3.1 catalogues exactly sixteen tuning files; combat_caps.json is not a seventeenth");
    }

    [Theory]
    [InlineData("CRIT", 0.75)]
    [InlineData("LIFESTEAL", 0.40)]
    [InlineData("DODGE", 0.50)]
    [InlineData("BLOCK", 0.60)]
    [InlineData("PEN", 0.70)]
    public void The_five_caps_are_the_numbers_05_section_1_states(string stat, double cap)
    {
        Data().ReadDouble($"{Document}#/caps/{stat}").ShouldBe(cap);
    }

    /// <summary>
    /// Exactly five caps — `16` D46 moved <c>DR_PCT</c> out of the ratio-cap set. A missing cap
    /// would read as "uncapped", which is legitimate for the other stats — indistinguishable from
    /// an accidental deletion.
    /// </summary>
    [Fact]
    public void No_stat_carries_a_cap_05_section_1_does_not_state()
    {
        var caps = Data().Read($"{Document}#/caps");

        caps.MemberNames.Where(name => !name.StartsWith('_')).ShouldBe(
            ["BLOCK", "CRIT", "DODGE", "LIFESTEAL", "PEN"],
            ignoreOrder: true);
    }

    /// <summary>
    /// `16` D46: the authored 0.6 reduction cap survives as a floor of 0.4 on the damage-taken
    /// multiplier (1.0 − 0.6 = 0.4), and it is the only floor authored.
    /// </summary>
    [Fact]
    public void DR_PCTs_bound_is_the_re_expressed_damage_taken_floor()
    {
        Data().ReadDouble($"{Document}#/floors/DR_PCT").ShouldBe(0.4);

        Data().Read($"{Document}#/floors").MemberNames
            .Where(name => !name.StartsWith('_'))
            .ShouldBe(["DR_PCT"], "no other stat authors a floor, and none is invented");
    }

    [Theory]
    [InlineData("MAX_HP", 250, 45)]
    [InlineData("ATK", 30, 6)]
    [InlineData("DEF", 15, 3)]
    public void The_three_level_scaled_base_curves_are_05_section_2s(string stat, double baseValue, double perLevel)
    {
        var data = Data();

        data.ReadDouble($"{Document}#/heroBaseStats/stats/{stat}/base").ShouldBe(baseValue);
        data.ReadDouble($"{Document}#/heroBaseStats/stats/{stat}/perLevel").ShouldBe(perLevel);
    }

    [Theory]
    [InlineData("ASPD", 1.00)]
    [InlineData("CRIT", 0.05)]
    [InlineData("CDMG", 0.50)]
    [InlineData("LIFESTEAL", 0.00)]
    [InlineData("DODGE", 0.02)]
    [InlineData("BLOCK", 0.00)]
    [InlineData("PEN", 0.00)]
    [InlineData("DMG_PCT", 1.00)]
    [InlineData("DR_PCT", 1.00)]
    [InlineData("HEAL_PCT", 1.00)]
    [InlineData("THORNS", 0.00)]
    public void The_eleven_level_independent_base_stats_are_05_section_2s(string stat, double baseValue)
    {
        var data = Data();

        data.ReadDouble($"{Document}#/heroBaseStats/stats/{stat}/base").ShouldBe(baseValue);
        data.ReadDouble($"{Document}#/heroBaseStats/stats/{stat}/perLevel").ShouldBe(
            0.0, $"05 §2 states {stat} as a single constant, so its curve has no slope");
    }

    /// <summary>HEAL_PCT is a multiplier on healing received, so its default must be 1.0, not 0.</summary>
    [Fact]
    public void HEAL_PCT_is_authored_as_one_and_not_as_zero()
    {
        Data().ReadDouble($"{Document}#/heroBaseStats/stats/HEAL_PCT/base").ShouldBe(1.0);
        Data().ReadDouble($"{Document}#/heroBaseStats/stats/HEAL_PCT/base").ShouldNotBe(0.0);
    }

    /// <summary>Every actor carries a complete 14-stat block; an unstated stat is a bug, not a zero.</summary>
    [Fact]
    public void Every_one_of_the_fourteen_combat_stats_has_a_base_row()
    {
        var stats = Data().Read($"{Document}#/heroBaseStats/stats");

        stats.MemberNames.ShouldBe(
            [
                "ASPD", "ATK", "BLOCK", "CDMG", "CRIT", "DEF", "DMG_PCT", "DODGE",
                "DR_PCT", "HEAL_PCT", "LIFESTEAL", "MAX_HP", "PEN", "THORNS",
            ],
            ignoreOrder: true);

        stats.MemberNames.Count.ShouldBe(14);
    }

    [Fact]
    public void The_legend_level_range_is_the_one_05_section_2_authors()
    {
        Data().ReadInt32($"{Document}#/heroBaseStats/legendLevelMin").ShouldBe(1);
        Data().ReadInt32($"{Document}#/heroBaseStats/legendLevelMax").ShouldBe(200);
    }

    [Fact]
    public void wardCapPct_is_the_number_05_section_4_1_states()
    {
        Data().ReadDouble($"{Document}#/wardCapPct").ShouldBe(1.0);
    }

    /// <summary>pvpMaxFightSeconds is a PvP-specific override of the simulator's 90 s PvE default.</summary>
    [Fact]
    public void pvpMaxFightSeconds_is_the_number_11_section_4_3_states()
    {
        Data().ReadDouble($"{Document}#/pvpMaxFightSeconds").ShouldBe(60.0);
    }

    /// <summary>The 90 s PvE default is deliberately absent from this file — it isn't authorised as a tunable.</summary>
    [Fact]
    public void The_90_second_PvE_default_is_deliberately_not_in_the_file()
    {
        var root = Data().Read(Document);

        root.MemberNames.ShouldBe(
            [
                "$schema", "_doc", "_status", "caps", "floors", "heroBaseStats",
                "wardCapPct", "pvpMaxFightSeconds", "mitigation",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void The_mitigation_dials_are_the_numbers_05_section_4_states()
    {
        Data().ReadDouble($"{Document}#/mitigation/flatConstant").ShouldBe(120.0);
        Data().ReadDouble($"{Document}#/mitigation/perLevelConstant").ShouldBe(20.0);
    }

    /// <summary>
    /// The same two dials also live in <c>tuning/power_model.json</c>; if the copies disagree, the
    /// power model predicts a mitigation the fight does not actually produce.
    /// </summary>
    [Fact]
    public void The_mitigation_dials_agree_with_the_power_models_copy()
    {
        var data = Data();

        data.ReadDouble($"{Document}#/mitigation/flatConstant")
            .ShouldBe(data.ReadDouble("tuning/power_model.json#/mitigation/flatConstant"));
        data.ReadDouble($"{Document}#/mitigation/perLevelConstant")
            .ShouldBe(data.ReadDouble("tuning/power_model.json#/mitigation/perLevelConstant"));
    }

    /// <summary>The mirror is enforced: editing one copy without the other fails the content build.</summary>
    [Fact]
    public void A_single_edit_to_one_copy_of_the_mitigation_dials_fails_the_build()
    {
        var edited = RepoData.SourceWithEdit(Document, "\"flatConstant\": 120", "\"flatConstant\": 130");

        var issues = ContentLoader.Load(edited).Issues;

        issues.ShouldContain(
            i => i.Location == $"{Document}#/mitigation/flatConstant",
            "the declared rule names the combat-caps side of the mirror, not the power model's");
    }

    [Fact]
    public void The_document_is_governed_by_its_own_schema()
    {
        Data().DocumentPaths.ShouldContain(Document);

        ContentLayout.SchemaFor(Document).ShouldBe("schema/combat_caps.schema.json");

        ContentLoader.SchemasAwaitingContent.ShouldNotContain("schema/combat_caps.schema.json");
        ContentLoader.VocabularySchemas.ShouldNotContain("schema/combat_caps.schema.json");
    }
}
