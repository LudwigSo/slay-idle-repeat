using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary><c>content/combat_caps.json</c> as the simulator reads it.</summary>
public sealed class CombatCapsTests
{
    [Fact]
    public void The_document_is_the_one_05_section_1_1_and_11_section_4_3_name()
    {
        CombatCaps.Document.ShouldBe("content/combat_caps.json");

        CombatCaps.WardCapPctPointer.ShouldBe("content/combat_caps.json#/wardCapPct");
        CombatCaps.PvpMaxFightSecondsPointer.ShouldBe(
            "content/combat_caps.json#/pvpMaxFightSeconds");
        CombatCaps.CapPointer(StatId.CRIT).ShouldBe("content/combat_caps.json#/caps/CRIT");
        CombatCaps.HeroBasePointer(StatId.MAX_HP)
            .ShouldBe("content/combat_caps.json#/heroBaseStats/stats/MAX_HP/base");
        CombatCaps.HeroPerLevelPointer(StatId.MAX_HP)
            .ShouldBe("content/combat_caps.json#/heroBaseStats/stats/MAX_HP/perLevel");
    }

    /// <summary>
    /// The six capped stats are named in code, not discovered from whatever keys the file holds.
    /// </summary>
    /// <remarks>
    /// Absence means "uncapped" for the other eight stats, so a cap that vanished from the data
    /// would be indistinguishable from a stat that was never capped — the reader would happily
    /// produce an uncapped CRIT and report success. Naming the six turns that into a
    /// <c>MissingContentException</c>.
    /// </remarks>
    [Fact]
    public void The_six_capped_stats_are_declared_so_a_deleted_cap_is_not_read_as_uncapped()
    {
        CombatCaps.CappedStats.ShouldBe(
        [
            StatId.CRIT, StatId.LIFESTEAL, StatId.DODGE, StatId.BLOCK, StatId.PEN, StatId.DR_PCT,
        ]);

        var withoutCrit = StatFixtures.CombatCapsSnapshot(["caps", "CRIT"]);

        Should.Throw<MissingContentException>(() => CombatCaps.Read(withoutCrit));
    }

    [Fact]
    public void Reading_the_document_produces_every_constant_05_and_11_put_in_data()
    {
        var caps = CombatCaps.Read(StatFixtures.CombatCapsSnapshot());

        caps.Caps.Maximum(StatId.CRIT).ShouldBe(0.75);
        caps.Caps.Maximum(StatId.LIFESTEAL).ShouldBe(0.40);
        caps.Caps.Maximum(StatId.DODGE).ShouldBe(0.50);
        caps.Caps.Maximum(StatId.BLOCK).ShouldBe(0.60);
        caps.Caps.Maximum(StatId.PEN).ShouldBe(0.70);
        caps.Caps.Maximum(StatId.DR_PCT).ShouldBe(0.60);

        caps.WardCapPct.ShouldBe(1.0, "05 §4.1 — wardCapPct");
        caps.PvpMaxFightSeconds.ShouldBe(60.0, "11 §4.3 — the duel duration cap");
        caps.Mitigation.Flat.ShouldBe(120.0, "05 §4");
        caps.Mitigation.PerLevel.ShouldBe(20.0, "05 §4");

        caps.HeroBase.MinimumLevel.ShouldBe(1);
        caps.HeroBase.MaximumLevel.ShouldBe(200);
        caps.HeroBase.At(60)[StatId.MAX_HP].ShouldBe(2950.0);
        caps.HeroBase.At(1)[StatId.HEAL_PCT].ShouldBe(1.0);
    }

    /// <summary>
    /// 🔒 <c>game-data/README.md</c>: <em>"<c>null</c> means the design docs do not authorise a value
    /// here. It is never a legitimate runtime value."</em> A <c>wardCapPct</c> that quietly defaulted
    /// would either delete every shield in the game or uncap the pool.
    /// </summary>
    [Fact]
    public void An_unauthorised_value_throws_rather_than_becoming_a_default()
    {
        var withoutWardCap = StatFixtures.CombatCapsSnapshot(["wardCapPct"]);

        Should.Throw<UnauthorisedTunableException>(() => CombatCaps.Read(withoutWardCap));
    }

    [Fact]
    public void A_hero_base_row_missing_from_the_data_throws_rather_than_defaulting_to_zero()
    {
        var withoutHealPct = StatFixtures.CombatCapsSnapshot(["heroBaseStats", "HEAL_PCT"]);

        Should.Throw<MissingContentException>(() => CombatCaps.Read(withoutHealPct));
    }

    [Fact]
    public void The_mitigation_constants_render_as_05_section_4s_formula()
    {
        new MitigationConstants(120, 20).ToString()
            .ShouldBe("effDef / (effDef + 120 + 20 * attackerLevel)");
    }
}
