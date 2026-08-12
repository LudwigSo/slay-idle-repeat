using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 The shipped <c>content/combat_caps.json</c> against `05` §1–2, §4, §4.1 and `11` §4.3 —
/// the transcription, asserted where the real file can actually be read.
/// </summary>
/// <remarks>
/// <para>
/// <c>SlayIdleRepeat.Core.Tests</c> owns the <em>behaviour</em> of the reader and the aggregation;
/// it references <c>SlayIdleRepeat.Core</c> and nothing else, so it has no JSON reader and cannot
/// see this document. This suite is where the document's numbers meet the design sections that
/// authorise them, one assertion per number with its section named — <c>game-data/README.md</c>:
/// <em>"a number nobody can trace to a section is a number nobody will defend."</em>
/// </para>
/// <para>
/// 🔒 Every value is read with <see cref="ContentSnapshot.ReadDouble"/>, which throws on a
/// <c>null</c>. A cap that was quietly nulled would fail here rather than reading as zero.
/// </para>
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

    /// <summary>`05` §1's Notes column, every cap, verbatim.</summary>
    [Theory]
    [InlineData("CRIT", 0.75)]
    [InlineData("LIFESTEAL", 0.40)]
    [InlineData("DODGE", 0.50)]
    [InlineData("BLOCK", 0.60)]
    [InlineData("PEN", 0.70)]
    [InlineData("DR_PCT", 0.60)]
    public void The_six_caps_are_the_numbers_05_section_1_states(string stat, double cap)
    {
        Data().ReadDouble($"{Document}#/caps/{stat}").ShouldBe(cap);
    }

    /// <summary>
    /// 🔒 Exactly six. A seventh would be a cap `05` §1 does not state, and a missing one would read
    /// as "uncapped" — which is a legitimate state for the other eight stats and therefore
    /// indistinguishable from a deletion.
    /// </summary>
    [Fact]
    public void No_stat_carries_a_cap_05_section_1_does_not_state()
    {
        var caps = Data().Read($"{Document}#/caps");

        caps.MemberNames.Where(name => !name.StartsWith('_')).ShouldBe(
            ["BLOCK", "CRIT", "DODGE", "DR_PCT", "LIFESTEAL", "PEN"],
            ignoreOrder: true);
    }

    /// <summary>`05` §2's three level-scaled curves.</summary>
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

    /// <summary>`05` §2's eleven constants, each with <c>perLevel</c> 0.</summary>
    [Theory]
    [InlineData("ASPD", 1.00)]
    [InlineData("CRIT", 0.05)]
    [InlineData("CDMG", 0.50)]
    [InlineData("LIFESTEAL", 0.00)]
    [InlineData("DODGE", 0.02)]
    [InlineData("BLOCK", 0.00)]
    [InlineData("PEN", 0.00)]
    [InlineData("DMG_PCT", 0.00)]
    [InlineData("DR_PCT", 0.00)]
    [InlineData("HEAL_PCT", 1.00)]
    [InlineData("THORNS", 0.00)]
    public void The_eleven_level_independent_base_stats_are_05_section_2s(string stat, double baseValue)
    {
        var data = Data();

        data.ReadDouble($"{Document}#/heroBaseStats/stats/{stat}/base").ShouldBe(baseValue);
        data.ReadDouble($"{Document}#/heroBaseStats/stats/{stat}/perLevel").ShouldBe(
            0.0, $"05 §2 states {stat} as a single constant, so its curve has no slope");
    }

    /// <summary>
    /// 🔒 The row a zero would silently break. `05` §2: <em>"HEAL% = 1.00 — a multiplier on ALL
    /// healing received; base 1.0, so lifesteal and heals work with no modifiers."</em>
    /// </summary>
    [Fact]
    public void HEAL_PCT_is_authored_as_one_and_not_as_zero()
    {
        Data().ReadDouble($"{Document}#/heroBaseStats/stats/HEAL_PCT/base").ShouldBe(1.0);
        Data().ReadDouble($"{Document}#/heroBaseStats/stats/HEAL_PCT/base").ShouldNotBe(0.0);
    }

    /// <summary>
    /// 🔒 All fourteen. `05` §2: <em>"every actor — hero and enemy alike — carries a complete
    /// 14-stat block with these defaults. An unstated stat is a bug, not a zero."</em>
    /// </summary>
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

    /// <summary>`05` §4.1 — the ward pool cap, which that section says lives in this file.</summary>
    [Fact]
    public void wardCapPct_is_the_number_05_section_4_1_states()
    {
        Data().ReadDouble($"{Document}#/wardCapPct").ShouldBe(1.0);
    }

    /// <summary>
    /// `11` §4.3 — <em>"a PvP-specific override of the simulator's 90 s default (`05` §3), set as
    /// <c>pvpMaxFightSeconds</c> in <c>data/combat_caps.json</c>."</em>
    /// </summary>
    [Fact]
    public void pvpMaxFightSeconds_is_the_number_11_section_4_3_states()
    {
        Data().ReadDouble($"{Document}#/pvpMaxFightSeconds").ShouldBe(60.0);
    }

    /// <summary>
    /// 🔒 The 90 s PvE default is deliberately absent. `05` §3's table carries no 📐 marker, so
    /// nothing authorises it as a tunable and a key for it would be a number nobody agreed
    /// (`16` R6).
    /// </summary>
    [Fact]
    public void The_90_second_PvE_default_is_deliberately_not_in_the_file()
    {
        var root = Data().Read(Document);

        root.MemberNames.ShouldBe(
            [
                "$schema", "_doc", "_status", "caps", "heroBaseStats",
                "wardCapPct", "pvpMaxFightSeconds", "mitigation",
            ],
            ignoreOrder: true);
    }

    /// <summary>`05` §4 — <em>"the two most important balance dials in the game."</em></summary>
    [Fact]
    public void The_mitigation_dials_are_the_numbers_05_section_4_states()
    {
        Data().ReadDouble($"{Document}#/mitigation/flatConstant").ShouldBe(120.0);
        Data().ReadDouble($"{Document}#/mitigation/perLevelConstant").ShouldBe(20.0);
    }

    /// <summary>
    /// 🔒 The same two dials are `29` §2.3's <c>MitigationVsReference</c> and live in
    /// <c>tuning/power_model.json</c>. If the two copies ever disagree, the power model predicts a
    /// mitigation the fight does not produce and `05` §9's assertion A10 stops being falsifiable.
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

    /// <summary>
    /// 🔒 And the mirror is enforced, not merely true today. A single edit to one copy fails the
    /// content build — the rule that stops the simulator and the model that grades it drifting apart.
    /// </summary>
    [Fact]
    public void A_single_edit_to_one_copy_of_the_mitigation_dials_fails_the_build()
    {
        var edited = RepoData.SourceWithEdit(Document, "\"flatConstant\": 120", "\"flatConstant\": 130");

        var issues = ContentLoader.Load(edited).Issues;

        issues.ShouldContain(
            i => i.Location == $"{Document}#/mitigation/flatConstant",
            "the declared rule names the combat-caps side of the mirror, not the power model's");
    }

    /// <summary>
    /// The file has a schema, and the schema is what closes `05` §1–2's 📐 baseline entry. A file
    /// without one would be unvalidated content, which `14` §6 forbids.
    /// </summary>
    [Fact]
    public void The_document_is_governed_by_its_own_schema()
    {
        Data().DocumentPaths.ShouldContain(Document);

        ContentLayout.SchemaFor(Document).ShouldBe("schema/combat_caps.schema.json");

        ContentLoader.SchemasAwaitingContent.ShouldNotContain("schema/combat_caps.schema.json");
        ContentLoader.VocabularySchemas.ShouldNotContain("schema/combat_caps.schema.json");
    }
}
