using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// The harness reads the authored inputs, and never resolves the ones it is forbidden to. Every
/// subject set is floored before anything is asserted over it, so a reader that silently returned a
/// smaller set would not leave downstream assertions green over a quietly shrunk sweep.
/// </summary>
[Collection(WallClockSensitive.Name)]
public sealed class HarnessContentTests
{
    [Fact]
    public void The_par_table_holds_the_twenty_four_authored_cells()
    {
        var table = ParPowerTable.Read(ShippedHarness.Content);

        table.Chapters.Count.ShouldBe(8);
        table.Chapters.ShouldBe(new[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var cells = table.Chapters.SelectMany(c => Tiers.All.Select(t => table.Power(c, t))).ToArray();
        cells.Length.ShouldBe(24);
        cells.ShouldAllBe(power => power > 0.0);
    }

    [Fact]
    public void The_par_cells_are_read_from_the_file_and_not_recomputed_from_the_default_fill()
    {
        // Every cell is independently editable; the formula is a default fill, not a constraint. This
        // edits one cell in memory and requires the reader to follow the edit, not the formula.
        var edited = ParPowerTable.Read(GameDataLoader.LoadWith(
            GameDataLoader.DataRoot,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ParPowerTable.Document] = File
                    .ReadAllText(Path.Combine(GameDataLoader.DataRoot, ParPowerTable.Document))
                    .Replace(
                        "{ \"chapter\": 3, \"NORMAL\": 4000",
                        "{ \"chapter\": 3, \"NORMAL\": 4321",
                        StringComparison.Ordinal),
            }));

        edited.Power(3, Tier.NORMAL).ShouldBe(4321.0);

        // Negative control: the shipped file is untouched and still reads 4000.
        ParPowerTable.Read(ShippedHarness.Content).Power(3, Tier.NORMAL).ShouldBe(4000.0);
    }

    [Fact]
    public void The_clear_rate_band_is_the_authored_one()
    {
        var table = ParPowerTable.Read(ShippedHarness.Content);

        table.ClearRateTarget.ShouldBe(0.70);
        table.ClearRateMin.ShouldBe(0.62);
        table.ClearRateMax.ShouldBe(0.78);
    }

    [Fact]
    public void The_five_build_archetypes_are_read_with_complete_fourteen_stat_lines()
    {
        var calibration = CalibrationBuilds.Read(ShippedHarness.Content);

        calibration.Archetypes.Count.ShouldBe(5);
        calibration.Archetypes.Select(a => a.Id).ShouldBe(new[]
        {
            "ARCH_CRIT", "ARCH_TANK_THORNS", "ARCH_DOT", "ARCH_LIFESTEAL", "ARCH_PET",
        });

        foreach (var archetype in calibration.Archetypes)
        {
            // HEAL_PCT is 1.0-based rather than 0.
            StatIds.Combat.Count.ShouldBe(14);
            foreach (var stat in StatIds.Combat)
            {
                double.IsFinite(archetype.Stats[stat]).ShouldBeTrue();
            }

            archetype.Stats[StatId.HEAL_PCT].ShouldBeGreaterThanOrEqualTo(1.0);
        }
    }

    [Fact]
    public void Only_ARCH_TANK_THORNS_holds_THORNS_above_zero()
    {
        // Not enough that the stats parse — they must be the authored values, and differ between archetypes.
        var calibration = CalibrationBuilds.Read(ShippedHarness.Content);

        var withThorns = calibration.Archetypes
            .Where(a => a.Stats[StatId.THORNS] > 0.0)
            .Select(a => a.Id)
            .ToArray();

        withThorns.ShouldBe(new[] { "ARCH_TANK_THORNS" });
        calibration.Archetype("ARCH_TANK_THORNS").Stats[StatId.THORNS].ShouldBe(0.3);
    }

    [Fact]
    public void The_reference_par_build_is_the_authored_level_ten_line()
    {
        var calibration = CalibrationBuilds.Read(ShippedHarness.Content);

        calibration.ReferenceParBuildLevel.ShouldBe(10);
        calibration.ReferenceParBuildTargetPower.ShouldBe(1000.0);
        calibration.ReferenceParBuild[StatId.MAX_HP].ShouldBe(1120.0);
        calibration.ReferenceParBuild[StatId.ATK].ShouldBe(145.0);
        calibration.ReferenceParBuild[StatId.DEF].ShouldBe(74.0);
        calibration.ReferenceParBuild[StatId.HEAL_PCT].ShouldBe(1.0);
    }

    [Fact]
    public void The_scaling_rule_is_read_rather_than_restated()
    {
        var calibration = CalibrationBuilds.Read(ShippedHarness.Content);

        calibration.ScaledStats.ShouldBe(new[] { StatId.MAX_HP, StatId.ATK, StatId.DEF });
        calibration.BisectionTolerance.ShouldBe(0.001);
        calibration.ScalarDecimalPlaces.ShouldBe(4);
        calibration.DefaultLevel.ShouldBe(40);
    }

    [Fact]
    public void The_archetype_reader_exposes_no_perk_or_pet_member_at_all()
    {
        // The reader must not even look at perks or pets — enforced by the type carrying nowhere to put them.
        var members = typeof(BuildArchetype)
            .GetProperties()
            .Select(p => p.Name)
            .Where(name => !string.Equals(name, "EqualityContract", StringComparison.Ordinal))
            .ToArray();

        members.Length.ShouldBe(2);
        members.ShouldBe(new[] { "Id", "Stats" }, ignoreOrder: true);
    }

    [Fact]
    public void The_enemy_level_table_is_the_authored_base_plus_tier_bonus()
    {
        var enemies = EnemyModel.Read(ShippedHarness.Content);

        enemies.Level(1, Tier.NORMAL).ShouldBe(10);
        enemies.Level(1, Tier.HEROIC).ShouldBe(20);
        enemies.Level(1, Tier.MYTHIC).ShouldBe(30);
        enemies.Level(8, Tier.NORMAL).ShouldBe(80);
        enemies.Level(8, Tier.MYTHIC).ShouldBe(100);

        // The bonus is per tier and the base is per chapter — a reader that transposed them would
        // still pass chapter 1 Normal.
        enemies.Level(5, Tier.HEROIC).ShouldBe(50);
    }

    [Fact]
    public void The_enemy_derivation_coefficients_are_the_authored_ones()
    {
        var enemies = EnemyModel.Read(ShippedHarness.Content);

        enemies.HpPerPower.ShouldBe(0.6);
        enemies.AtkPerPower.ShouldBe(0.045);
        enemies.DefPerPower.ShouldBe(0.03);
        enemies.BaseAspd.ShouldBe(1.0);
        enemies.FixedPen.ShouldBe(0.0);
        enemies.ElitePowerMultiplier.ShouldBe(2.2);
        enemies.ArmouredDefMultiplier.ShouldBe(1.8);

        enemies.Archetypes.Count.ShouldBe(8);
        enemies.Archetypes.Select(a => a.Id).ShouldBe(new[]
        {
            "GRUNT", "SWARM", "BRUTE", "SKIRMISHER", "WARDEN", "CASTER", "LEECH", "REAVER",
        });

        enemies.Archetypes.Max(a => a.DefCoef).ShouldBe(2.2);
        enemies.Archetypes.Single(a => a.Id == "WARDEN").DefCoef.ShouldBe(2.2);
    }

    [Fact]
    public void Enemy_DEF_is_power_times_defPerPower_times_defCoef()
    {
        var enemies = EnemyModel.Read(ShippedHarness.Content);

        enemies.Def(1000.0, 1.0).ShouldBe(30.0);
        enemies.Def(5434.0, 0.8).ShouldBe(130.416);

        // Negative control: the two multipliers are not interchangeable.
        enemies.Def(5434.0, 0.8).ShouldNotBe(enemies.Def(5434.0 * 0.8, 0.03));
    }

    [Fact]
    public void The_eight_campaign_bosses_are_swept_and_BOSS_FTUE_is_excluded()
    {
        var bosses = BossRoster.Read(ShippedHarness.Content);

        bosses.All.Count.ShouldBe(9);
        bosses.Campaign.Count.ShouldBe(8);
        bosses.Campaign.Select(b => b.Id).ShouldBe(new[]
        {
            "BOSS_THORNMAW", "BOSS_GULGROT", "BOSS_OSSUARY_KING", "BOSS_CINDERMAW",
            "BOSS_RIMEHOLD", "BOSS_COGITATOR_PRIME", "BOSS_SPOREQUEEN_VELL", "BOSS_DICELORD",
        });

        // Excluded because it states no chapter, not because of its name.
        bosses.Excluded.Count.ShouldBe(1);
        bosses.Excluded.Single().Id.ShouldBe(BossRoster.FtueScriptId);
        bosses.Excluded.Single().Chapter.ShouldBeNull();
        bosses.Campaign.ShouldAllBe(b => b.Chapter != null);

        bosses.ForChapter(7).Id.ShouldBe("BOSS_SPOREQUEEN_VELL");
        bosses.ForChapter(1).Id.ShouldBe("BOSS_THORNMAW");
    }

    [Fact]
    public void The_five_summoning_bosses_all_carry_the_authored_midpoint()
    {
        var bosses = BossRoster.Read(ShippedHarness.Content);

        bosses.Summoners.Count.ShouldBe(5);
        bosses.Summoners.Select(b => b.Id).ShouldBe(new[]
        {
            "BOSS_THORNMAW", "BOSS_OSSUARY_KING", "BOSS_RIMEHOLD",
            "BOSS_COGITATOR_PRIME", "BOSS_SPOREQUEEN_VELL",
        });

        bosses.Summoners.ShouldAllBe(b => b.AddsPowerFraction == 0.3);

        // Negative control: the other four carry no fraction, so the assertion isn't vacuous.
        bosses.All.Count(b => b.AddsPowerFraction is null).ShouldBe(4);
    }

    [Fact]
    public void The_mitigation_dials_agree_between_the_two_documents_that_author_them()
    {
        // The pair is authored twice, in two documents, and a build rule mirrors them. If the mirror
        // ever stopped running, guardrail 5 would be measuring the wrong copy and still look green.
        var combat = MitigationDials.Read(ShippedHarness.Content);
        var powerModel = MitigationDials.ReadPowerModelCopy(ShippedHarness.Content);

        combat.FlatConstant.ShouldBe(120.0);
        combat.PerLevelConstant.ShouldBe(20.0);
        combat.ShouldBe(powerModel);
    }
}
