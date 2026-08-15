using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>Per-actor flow state: the charges, saves and multipliers combat resolution reads.</summary>
public sealed class CombatFlowStateTests
{
    /// <summary>Base attack multiplier is 1.0 and resets after every resolved attack.</summary>
    [Fact]
    public void The_attack_multiplier_is_1_with_nothing_armed_and_returns_to_1_after_a_charge_is_spent()
    {
        var flow = new CombatFlowState();

        flow.ConsumeAttackMultiplier().ShouldBe(1.0);

        flow.GrantAttackMultiplier(3.0, charges: 1, "PK_OPENER");

        flow.ConsumeAttackMultiplier().ShouldBe(3.0);
        flow.ConsumeAttackMultiplier().ShouldBe(1.0);
    }

    /// <summary>Charges are consumed in ascending effect-id order, not arming order.</summary>
    [Fact]
    public void Every_armed_charge_applies_to_one_swing_in_ascending_effect_id_order()
    {
        var flow = new CombatFlowState();

        // Armed in the wrong order on purpose: the sort is the rule, not the arming order.
        flow.GrantAttackMultiplier(0.6, charges: 1, "PK_GAMBLER");
        flow.GrantAttackMultiplier(3.0, charges: 1, "PK_OPENER");

        // Both grants apply to the same swing: 0.6 * 3.0 = 1.8.
        flow.ConsumeAttackMultiplier().ShouldBe(1.8);
        flow.ConsumeAttackMultiplier().ShouldBe(1.0);
    }

    /// <summary>A multi-charge grant survives its first swing and is spent one at a time.</summary>
    [Fact]
    public void A_multi_charge_grant_lasts_for_that_many_swings()
    {
        var flow = new CombatFlowState();
        flow.GrantAttackMultiplier(2.0, charges: 3, "A");

        flow.ConsumeAttackMultiplier().ShouldBe(2.0);
        flow.ConsumeAttackMultiplier().ShouldBe(2.0);
        flow.ConsumeAttackMultiplier().ShouldBe(2.0);
        flow.ConsumeAttackMultiplier().ShouldBe(1.0);
    }

    /// <summary><c>FORCE_CRIT_NEXT</c> charges are spent one swing at a time.</summary>
    [Fact]
    public void Forced_crits_are_spent_one_at_a_time()
    {
        var flow = new CombatFlowState();
        flow.GrantForcedCrits(2);

        flow.ForcedCritCharges.ShouldBe(2);
        flow.ConsumeForcedCrit().ShouldBeTrue();
        flow.ConsumeForcedCrit().ShouldBeTrue();
        flow.ConsumeForcedCrit().ShouldBeFalse();
        flow.ForcedCritCharges.ShouldBe(0);
    }

    /// <summary>Damage-taken multipliers accumulate as a product rather than replacing.</summary>
    [Fact]
    public void Damage_taken_multipliers_are_a_product_and_not_a_last_writer_wins()
    {
        var flow = new CombatFlowState();

        flow.DamageTakenMultiplier().ShouldBe(1.0);

        flow.AddDamageTakenMultiplier(0.8, "PK_STALWART");
        flow.AddDamageTakenMultiplier(1.5, "BOSS_RIMEHOLD_CORE");

        flow.DamageTakenMultiplier().ShouldBe(1.2);
    }

    /// <summary>Anti-loop rule: a death save fires at most its authored <c>once</c> count per battle.</summary>
    [Fact]
    public void A_once_death_save_fires_exactly_once_per_battle()
    {
        var flow = new CombatFlowState();
        flow.ArmDeathSave(new DeathSave(1.0, IsRevive: false, "PK_LAST_STAND", FiresOnce: true));

        flow.ConsumeDeathSave(revive: false)!.Value.Hp.ShouldBe(1.0);
        flow.ConsumeDeathSave(revive: false).ShouldBeNull();
        flow.DeathSaveFirings("PK_LAST_STAND").ShouldBe(1);
    }

    /// <summary>A save with no authored <c>once</c> is not limited — the rule is about <c>once</c>.</summary>
    [Fact]
    public void A_save_without_once_is_not_limited()
    {
        var flow = new CombatFlowState();
        flow.ArmDeathSave(new DeathSave(1.0, IsRevive: false, "CP_ENDLESS", FiresOnce: false));

        flow.ConsumeDeathSave(revive: false).ShouldNotBeNull();
        flow.ConsumeDeathSave(revive: false).ShouldNotBeNull();
        flow.DeathSaveFirings("CP_ENDLESS").ShouldBe(2);
    }

    /// <summary>
    /// A <c>SURVIVE_LETHAL</c> is not a <c>REVIVE</c>: it fires no <c>ON_REVIVE</c> because the
    /// actor never died, so the two are drawn from separately.
    /// </summary>
    [Fact]
    public void A_survive_lethal_is_not_offered_where_a_revive_is_asked_for()
    {
        var flow = new CombatFlowState();
        flow.ArmDeathSave(new DeathSave(1.0, IsRevive: false, "PK_LAST_STAND", FiresOnce: true));

        flow.ConsumeDeathSave(revive: true).ShouldBeNull();
        flow.ConsumeDeathSave(revive: false).ShouldNotBeNull();
    }

    /// <summary><c>HIGHEST_PCT_BONUS</c>, with an exact tie falling to the stat table order.</summary>
    [Fact]
    public void The_highest_percent_bucket_is_named_and_an_exact_tie_falls_to_the_05_1_table_order()
    {
        var flow = new CombatFlowState();

        flow.HighestPercentBonusStat().ShouldBeNull();

        flow.AddPercentBucket(StatId.DEF, 0.10);
        flow.AddPercentBucket(StatId.ATK, 0.25);

        flow.HighestPercentBonusStat().ShouldBe(StatId.ATK);

        // Tied at 0.25 — ATK precedes DEF in table order, and the answer must not depend on which
        // was written first, which a Dictionary walk would.
        flow.AddPercentBucket(StatId.DEF, 0.15);
        flow.PercentBuckets[StatId.DEF].ShouldBe(0.25);
        flow.HighestPercentBonusStat().ShouldBe(StatId.ATK);
    }

    /// <summary>Buckets from two writes accumulate rather than replacing.</summary>
    [Fact]
    public void Percent_buckets_accumulate()
    {
        var flow = new CombatFlowState();

        flow.AddPercentBucket(StatId.ATK, 0.10);
        flow.AddPercentBucket(StatId.ATK, 0.05);

        flow.PercentBuckets[StatId.ATK].ShouldBe(0.15);
    }
}
