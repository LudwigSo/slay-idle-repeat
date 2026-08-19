using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>The seven damage and healing ops, each asserted on the number it hands the engine.</summary>
/// <remarks>
/// Internal seam: the pre-pipeline number is what no <c>CombatEvent</c> carries — an op that routes
/// correctly but multiplies by the wrong basis (a <c>DAMAGE</c> multiplying by ATK before
/// <c>ResolveAttack</c>) squares ATK in every fight while passing any "did it route" test. The
/// pipeline itself is pinned publicly in <c>Rules/Combat</c>.
/// </remarks>
public sealed class DamageAndHealingOpTests
{
    // ───────────────────────────────────────────────────────────── DAMAGE

    /// <summary>The op's <c>value</c> is itself the AttackMultiplier for the resolved attack.</summary>
    [Fact]
    public void DAMAGE_hands_ResolveAttack_the_ops_value_as_the_AttackMultiplier_and_never_multiplies_by_ATK()
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("EN_1", 1);
        var bench = new OpTestBench().WithStat(hero, StatId.ATK, 250.0);

        var cleave = OpFixtures.Effect("PK_CLEAVE_T1", EffectOp.DAMAGE, 0.40, EffectTarget.CURRENT_TARGET);

        var evaluation = EffectTestBattle.Context(hero, hero, enemy) with { CurrentTarget = enemy };
        DamageAndHealingOps.Damage(cleave, bench.Context(evaluation));

        bench.OnlyAmount("ResolveAttack").ShouldBe(
            0.40,
            "05 §4 step 2 is raw = attacker.ATK x AttackMultiplier — the pipeline applies the 250, " +
            "so multiplying here would make it 100 and square ATK in the fight");
    }

    /// <summary><c>PK_CLEAVE</c> splash: one resolved attack per living enemy.</summary>
    [Fact]
    public void DAMAGE_resolves_one_attack_per_target_the_18_5_token_names()
    {
        var hero = EffectTestBattle.Hero();
        var primary = EffectTestBattle.Enemy("EN_PRIMARY", 1);
        var other = EffectTestBattle.Enemy("EN_OTHER", 2);
        var bench = new OpTestBench();

        var cleave = OpFixtures.Effect("PK_CLEAVE_T1", EffectOp.DAMAGE, 0.40, EffectTarget.OTHER_ENEMIES);

        var evaluation = EffectTestBattle.Context(hero, hero, primary, other) with { CurrentTarget = primary };
        DamageAndHealingOps.Damage(cleave, bench.Context(evaluation));

        bench.Calls.ShouldBe(["ResolveAttack(HERO->EN_OTHER, 0.4, PK_CLEAVE_T1)"], Case.Sensitive);
    }

    /// <summary>
    /// <c>DAMAGE</c> reports the HP actually lost (post-absorption), summed over its targets — not
    /// the pre-absorption basis. The two differ on any warded target.
    /// </summary>
    [Fact]
    public void DAMAGE_reports_the_HP_actually_lost_summed_over_its_targets()
    {
        var hero = EffectTestBattle.Hero();
        var first = EffectTestBattle.Enemy("EN_1", 1);
        var second = EffectTestBattle.Enemy("EN_2", 2);
        var bench = new OpTestBench().WithAttackOutcome(basis: 120.0, hpLost: 90.0);

        var cleave = OpFixtures.Effect("PK_CLEAVE_T1", EffectOp.DAMAGE, 0.40, EffectTarget.ALL_ENEMIES);

        var totals = DamageAndHealingOps.Damage(
            cleave, bench.Context(EffectTestBattle.Context(hero, hero, first, second)));

        totals.HpLost.ShouldBe(180.0, "two hits at 90 HP lost each — step 9, after ward absorption");

        // Basis is the pre-absorption number, published separately: a HEAL_LEECH fed HpLost would
        // heal nothing off a warded target.
        totals.Basis.ShouldBe(240.0, "two hits at a pre-absorption basis of 120 each");
    }

    /// <summary>Every op rounds to 4 dp at each accumulation point: per target, and again over the total.</summary>
    /// <remarks>
    /// <c>0.12345 × 3.0</c> is <c>0.37035</c>, which rounds to <c>0.3704</c> — and two of them sum to
    /// <c>0.7408</c>, not the <c>0.7407</c> an unrounded accumulation would give.
    /// </remarks>
    [Fact]
    public void An_ops_number_is_rounded_to_4_dp_per_target_and_again_over_the_total()
    {
        var boss = EffectTestBattle.Enemy("BOSS_X", 1);
        var first = EffectTestBattle.Hero() with { Id = "HERO_A", Index = 0 };
        var second = EffectTestBattle.Hero() with { Id = "HERO_B", Index = 2 };
        var bench = new OpTestBench().WithStat(boss, StatId.ATK, 3.0);

        var tick = OpFixtures.Effect("BOSS_X_ROT", EffectOp.DAMAGE_TRUE, 0.12345, EffectTarget.ALL_ENEMIES);

        DamageAndHealingOps.DamageTrue(
                               tick, bench.Context(EffectTestBattle.Context(boss, boss, first, second)))
                           .ShouldBe(0.7408, "0.37035 rounds to 0.3704 per target; unrounded the sum is 0.7407");

        bench.Amounts.Select(a => a.Amount).ShouldBe([0.3704, 0.3704]);
    }

    /// <summary>
    /// <c>DAMAGE</c> admits no value mode but <c>ATK_MULT</c> — turning a flat amount into a
    /// multiplier would mean dividing by the attacker's ATK, which is undefined behaviour.
    /// </summary>
    [Fact]
    public void DAMAGE_refuses_a_value_mode_05_4_2_does_not_authorise()
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("EN_1", 1);
        var bench = new OpTestBench();

        var flat = OpFixtures.Effect("PK_BAD", EffectOp.DAMAGE, 40.0, EffectTarget.CURRENT_TARGET) with
        {
            ValueMode = ValueMode.FLAT,
        };

        var evaluation = EffectTestBattle.Context(hero, hero, enemy) with { CurrentTarget = enemy };

        var thrown = Should.Throw<EffectContextException>(
            () => DamageAndHealingOps.Damage(flat, bench.Context(evaluation)));

        thrown.Token.ShouldBe("PK_BAD");
        thrown.Message.ShouldContain("does not admit valueMode FLAT", Case.Sensitive);
        bench.Calls.ShouldBeEmpty("nothing is resolved when the mode is refused");
    }

    // ───────────────────────────────────────────────────────────── DAMAGE_TRUE

    /// <summary><c>DAMAGE_TRUE</c> reduces HP directly; with no <c>valueMode</c> it defaults to a multiple of source ATK.</summary>
    [Fact]
    public void DAMAGE_TRUE_is_an_amount_and_defaults_to_a_multiple_of_the_sources_ATK()
    {
        var boss = EffectTestBattle.Enemy("BOSS_SPOREQUEEN", 1);
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench().WithStat(boss, StatId.ATK, 200.0);

        var rot = OpFixtures.Effect("BOSS_SPOREQUEEN_ROT", EffectOp.DAMAGE_TRUE, 1.5, EffectTarget.ALL_ENEMIES);

        DamageAndHealingOps.DamageTrue(rot, bench.Context(EffectTestBattle.Context(boss, boss, hero)));

        bench.OnlyAmount("DealTrueDamage").ShouldBe(300.0);
        bench.Amounts.Single().Actor.ShouldBe("HERO");
    }

    // ───────────────────────────────────────────────────────────── DAMAGE_MAXHP_PCT

    /// <summary><c>TARGET_MAXHP_PCT</c> reads the target's max HP, not the source's.</summary>
    [Fact]
    public void DAMAGE_MAXHP_PCT_reads_the_TARGETS_max_HP_which_is_what_18_7_10_captions()
    {
        var volatileElite = EffectTestBattle.Enemy("EL_VOLATILE", 1);
        var hero = EffectTestBattle.Hero(currentHp: 900, maxHp: 2400);
        var bench = new OpTestBench();

        var explosion = OpFixtures.Effect(
            "EL_VOLATILE_EXPLODE", EffectOp.DAMAGE_MAXHP_PCT, 0.15, EffectTarget.ALL_ENEMIES) with
        {
            ValueMode = ValueMode.TARGET_MAXHP_PCT,
        };

        DamageAndHealingOps.DamageMaxHpPct(
            explosion, bench.Context(EffectTestBattle.Context(volatileElite, volatileElite, hero)));

        bench.OnlyAmount("DealMaxHpPctDamage").ShouldBe(360.0, "0.15 x 2400 Max HP");
    }

    /// <summary>
    /// <c>DAMAGE_MAXHP_PCT</c>'s own default is <c>TARGET_MAXHP_PCT</c>, overriding the blanket
    /// <c>ATK_MULT</c> default other ops share.
    /// </summary>
    [Fact]
    public void DAMAGE_MAXHP_PCT_with_no_valueMode_is_a_percentage_of_max_HP_not_a_multiple_of_ATK()
    {
        var hero = EffectTestBattle.Hero(currentHp: 900, maxHp: 2400);
        var bench = new OpTestBench().WithStat(hero, StatId.ATK, 500.0);

        var bloodPrice = OpFixtures.Effect(
            "CP_BLOOD_PRICE_COST", EffectOp.DAMAGE_MAXHP_PCT, 0.03, EffectTarget.SELF) with
        {
            Tags = ["drawback"],
        };

        DamageAndHealingOps.DamageMaxHpPct(bloodPrice, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.OnlyAmount("DealMaxHpPctDamage").ShouldBe(
            72.0, "0.03 x 2400 Max HP — under ATK_MULT it would be 15, which 06's row does not say");
    }

    /// <summary>A <c>drawback</c>-tagged self-inflicted cost reports a ward bypass, so wards cannot silently delete it.</summary>
    [Fact]
    public void A_drawback_tagged_self_inflicted_cost_reports_the_05_4_1_ward_bypass()
    {
        var hero = EffectTestBattle.Hero(maxHp: 1000);
        var bench = new OpTestBench();

        var bloodPrice = OpFixtures.Effect(
            "CP_BLOOD_PRICE_COST", EffectOp.DAMAGE_MAXHP_PCT, 0.03, EffectTarget.SELF) with
        {
            Tags = ["drawback"],
        };

        DamageAndHealingOps.DamageMaxHpPct(bloodPrice, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.WardBypasses.ShouldBe([true]);
    }

    /// <summary>
    /// The same tag pointed at an enemy is not a self-inflicted cost — a blanket tag-keyed bypass
    /// would hand every cursed perk ward penetration nobody authored.
    /// </summary>
    [Fact]
    public void A_drawback_tag_on_an_offensive_clause_does_not_bypass_the_targets_wards()
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("EN_1", 1, maxHp: 1000);
        var bench = new OpTestBench();

        var offensive = OpFixtures.Effect(
            "PK_BAD_DRAWBACK", EffectOp.DAMAGE_MAXHP_PCT, 0.10, EffectTarget.ALL_ENEMIES) with
        {
            Tags = ["drawback"],
        };

        DamageAndHealingOps.DamageMaxHpPct(
            offensive, bench.Context(EffectTestBattle.Context(hero, hero, enemy)));

        bench.WardBypasses.ShouldBe([false]);
        bench.OnlyAmount("DealMaxHpPctDamage").ShouldBe(100.0);
    }

    // ───────────────────────────────────────────────────────────── HEAL / HEAL_LEECH

    /// <summary><c>HEAL</c> goes through <c>Heal()</c>, at its authored unit.</summary>
    [Fact]
    public void HEAL_hands_Heal_a_pre_HEAL_PCT_amount()
    {
        var hero = EffectTestBattle.Hero(currentHp: 300, maxHp: 1200);
        var bench = new OpTestBench();

        var heal = OpFixtures.Effect("PK_MEND", EffectOp.HEAL, 0.25, EffectTarget.SELF) with
        {
            ValueMode = ValueMode.SELF_MAXHP_PCT,
        };

        DamageAndHealingOps.Heal(heal, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.OnlyAmount("Heal").ShouldBe(300.0, "0.25 x 1200; 05 §4.3 applies HEAL% and the Max-HP clip");
    }

    /// <summary>
    /// <c>HEAL_LEECH</c> is a % of the pre-absorption damage basis, so a lifesteal attacker still
    /// heals off a fully-warded hit.
    /// </summary>
    [Fact]
    public void HEAL_LEECH_reads_the_pre_absorption_damage_basis()
    {
        var hero = EffectTestBattle.Hero(currentHp: 100, maxHp: 1000);
        var bench = new OpTestBench();

        var leech = OpFixtures.Effect("PK_LEECH", EffectOp.HEAL_LEECH, 0.20, EffectTarget.SELF);

        DamageAndHealingOps.HealLeech(
            leech, bench.Context(EffectTestBattle.Context(hero, hero), damageDealt: 450.0));

        bench.OnlyAmount("Heal").ShouldBe(90.0, "0.20 x the 450 basis");
    }

    /// <summary>
    /// Outside a damage context there is no basis, and 0 would wrongly read as "the hit was fully
    /// absorbed" — so this fails loudly instead.
    /// </summary>
    [Fact]
    public void HEAL_LEECH_outside_a_damage_context_throws_rather_than_healing_nothing()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var leech = OpFixtures.Effect("PK_LEECH", EffectOp.HEAL_LEECH, 0.20, EffectTarget.SELF);

        var thrown = Should.Throw<EffectContextException>(
            () => DamageAndHealingOps.HealLeech(leech, bench.Context(EffectTestBattle.Context(hero, hero))));

        thrown.Message.ShouldContain("DAMAGE_DEALT_PCT", Case.Sensitive);
        bench.Calls.ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────────────────── SHIELD

    /// <summary><c>SHIELD</c> grants a ward of the size its value mode names.</summary>
    [Fact]
    public void SHIELD_grants_a_ward_of_the_size_its_value_mode_names()
    {
        var hero = EffectTestBattle.Hero(currentHp: 40, maxHp: 1600);
        var bench = new OpTestBench();

        var ward = OpFixtures.Effect("PK_UNBREAKABLE_T1_WARD", EffectOp.SHIELD, 0.25, EffectTarget.SELF) with
        {
            ValueMode = ValueMode.SELF_MAXHP_PCT,
        };

        DamageAndHealingOps.Shield(ward, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.OnlyAmount("GrantWard").ShouldBe(400.0);
        bench.SourceCaps.ShouldBe([null], "18 §2.2's sourceCapPct is absent on this one");
    }

    /// <summary>Overheal into a ward carries its per-instance <c>sourceCapPct</c> to the pool rather than applying it per grant.</summary>
    [Fact]
    public void SHIELD_from_OVERHEAL_AMOUNT_carries_its_sourceCapPct_to_the_ward_pool()
    {
        var hero = EffectTestBattle.Hero(maxHp: 1000);
        var bench = new OpTestBench();

        var transfusion = OpFixtures.Effect("PK_TRANSFUSION", EffectOp.SHIELD, 1.0, EffectTarget.SELF) with
        {
            ValueMode = ValueMode.OVERHEAL_AMOUNT,
            SourceCapPct = 0.20,
        };

        DamageAndHealingOps.Shield(
            transfusion, bench.Context(EffectTestBattle.Context(hero, hero), overhealAmount: 137.5));

        bench.OnlyAmount("GrantWard").ShouldBe(137.5, "1.0 x the clipped excess");
        bench.SourceCaps.ShouldBe([0.20],
            "18 §2.2 caps the total UNBROKEN ward of the instance, a running total only the pool holds");
    }

    /// <summary><c>HEAL_AMOUNT</c> / <c>OVERHEAL_AMOUNT</c> exist only inside <c>ON_HEAL</c> contexts.</summary>
    [Fact]
    public void An_ON_HEAL_only_value_mode_outside_an_ON_HEAL_context_throws()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var transfusion = OpFixtures.Effect("PK_TRANSFUSION", EffectOp.SHIELD, 1.0, EffectTarget.SELF) with
        {
            ValueMode = ValueMode.OVERHEAL_AMOUNT,
        };

        var thrown = Should.Throw<EffectContextException>(
            () => DamageAndHealingOps.Shield(transfusion, bench.Context(EffectTestBattle.Context(hero, hero))));

        thrown.Message.ShouldContain("ON_HEAL", Case.Sensitive);
        bench.Calls.ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────────────────── REFLECT

    /// <summary><c>REFLECT</c> adds to <c>THORN</c> as a fraction, never as a multiple of ATK.</summary>
    [Fact]
    public void REFLECT_adds_its_value_to_THORN_as_a_fraction_and_never_as_a_multiple_of_ATK()
    {
        var elite = EffectTestBattle.Enemy("EL_REFLECTIVE", 1);
        var bench = new OpTestBench().WithStat(elite, StatId.ATK, 900.0);

        var reflect = OpFixtures.Effect("EL_REFLECTIVE_THORNS", EffectOp.REFLECT, 0.25, EffectTarget.SELF) with
        {
            Duration = new EffectDuration { Scope = DurationScope.BATTLE },
        };

        DamageAndHealingOps.Reflect(reflect, bench.Context(EffectTestBattle.Context(elite, elite)));

        bench.OnlyAmount("AddThorns").ShouldBe(
            0.25, "under 18 §2.2's blanket ATK_MULT default this would be 225 — a thorns FRACTION of 225");

        bench.OnlyLifetime("AddThorns").Duration!.Scope.ShouldBe(DurationScope.BATTLE);
    }

    /// <summary>An op that resolves against an empty enemy set does nothing, and that is not a failure.</summary>
    /// <remarks>A token whose subject is absent throws; a token whose set is empty resolves to the empty set.</remarks>
    [Fact]
    public void An_op_whose_target_set_is_empty_resolves_to_nothing_without_failing()
    {
        var hero = EffectTestBattle.Hero();
        var dead = EffectTestBattle.Enemy("EN_DEAD", 1) with { IsAlive = false };
        var bench = new OpTestBench();

        var cleave = OpFixtures.Effect("PK_CLEAVE_T1", EffectOp.DAMAGE, 0.40, EffectTarget.ALL_ENEMIES);

        DamageAndHealingOps.Damage(cleave, bench.Context(EffectTestBattle.Context(hero, hero, dead)))
                           .ShouldBe(new DamageAndHealingOps.DamageTotals(0.0, 0.0));

        bench.Calls.ShouldBeEmpty();
    }
}
