using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// 🔒 `18` §2.2's seven damage and healing ops, each asserted on the <b>number</b> it hands the
/// `05` §4 engine — `18` §10 step 3.
/// </summary>
/// <remarks>
/// `05` §4.2's routing table is what these pin. The recurring failure they are written to catch is
/// the one nothing else would: an op that routes correctly and multiplies by the wrong basis. A
/// <c>DAMAGE</c> that multiplied its value by ATK before handing it to <c>ResolveAttack</c> would
/// pass a "did it route" test and square the attacker's attack power in every fight.
/// </remarks>
public sealed class DamageAndHealingOpTests
{
    // ───────────────────────────────────────────────────────────── DAMAGE

    /// <summary>
    /// 🔒 `05` §4.2 — <em>"the op's <c>value</c> <b>is</b> the AttackMultiplier for that resolved
    /// attack"</em>. `18` §7.10's <c>PK_CLEAVE</c> at 0.40 is a ×0.4 attack.
    /// </summary>
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

    /// <summary>`18` §7.10's <c>PK_CLEAVE</c> splash: one resolved attack per living enemy.</summary>
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
    /// 🔒 <c>DAMAGE</c> reports the HP actually lost — `05` §4 step 9's post-absorption number —
    /// summed over its targets, and <b>not</b> step 8's pre-absorption basis.
    /// </summary>
    /// <remarks>
    /// The two differ on any warded target, and the wrong one here would feed a wrong
    /// <c>DAMAGE_DEALT_PCT</c> to whatever leech read it. The bench answers a miss by default
    /// precisely so that a test which forgot to state an outcome cannot assert 0 and look meaningful.
    /// </remarks>
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

        // 🔒 05 §4 step 8's on-damage basis, published SEPARATELY. 05 §4.1: "a lifesteal attacker
        //    still heals off a fully-warded hit", so a HEAL_LEECH fed HpLost would heal nothing off
        //    a shielded target. The two fields exist so M2-04's wiring cannot be a guess.
        totals.Basis.ShouldBe(240.0, "two hits at a pre-absorption basis of 120 each");
    }

    /// <summary>
    /// 🔒 `05` §1.1 — every op rounds to 4 dp <b>at each accumulation point</b>: per target, and
    /// again over the total.
    /// </summary>
    /// <remarks>
    /// The other cases in this file all multiply to values that are exact in IEEE double, so none of
    /// them is load-bearing on the rounding. This one is: <c>0.12345 × 3.0</c> is <c>0.37035</c>,
    /// which rounds to <c>0.3704</c> — and two of them sum to <c>0.7408</c>, not to the
    /// <c>0.7407</c> an unrounded accumulation would give.
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
    /// 🔒 <c>DAMAGE</c> admits <b>no</b> value mode but <c>ATK_MULT</c>: `05` §4.2 authorises one
    /// reading, and turning a flat amount into a multiplier means dividing by the attacker's ATK,
    /// which no clause states.
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

    /// <summary>
    /// `05` §4.2 — <c>DAMAGE_TRUE</c> reduces HP directly, so its value is an <b>amount</b>: on
    /// §2.2's stated <c>ATK_MULT</c> default, <c>1.5 × 200 ATK = 300</c>.
    /// </summary>
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

    /// <summary>
    /// 🔒 `18` §7.10's Volatile elite — <em>"explodes on death for 15% of <b>hero</b> Max HP"</em>,
    /// authored as <c>TARGET_MAXHP_PCT</c> on an enemy actor targeting <c>ALL_ENEMIES</c>.
    /// </summary>
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
    /// 🔒 The op's own §2.2 row wins over §2.2's blanket <c>ATK_MULT</c> default: the default here is
    /// <c>TARGET_MAXHP_PCT</c>, which is the only reading under which §7.5's <c>CP_BLOOD_PRICE</c>
    /// — <em>"lose 3% Max HP after every battle"</em>, authored with no <c>valueMode</c> — works.
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

    /// <summary>
    /// 🔒 R12 / `05` §4.1 bypass class (b) — a <c>drawback</c>-tagged self-inflicted cost reports the
    /// bypass, so <em>"wards must not silently delete perk drawbacks"</em> holds.
    /// </summary>
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
    /// 🔒 The same tag pointed at an <b>enemy</b> is not a self-inflicted cost. `05` §4.1's class (b)
    /// is <em>"self-inflicted costs"</em>; a blanket tag-keyed bypass would hand every cursed perk
    /// ward penetration nobody authored.
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

    /// <summary>`05` §4.2 — <c>HEAL</c> goes through <c>Heal()</c>, at its authored unit.</summary>
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
    /// 🔒 `18` §2.2 — <c>HEAL_LEECH</c> is <em>"a % of damage just dealt"</em>, and `05` §4 step 8 /
    /// §4.1 make that the <b>pre-absorption</b> basis: <em>"a lifesteal attacker still heals off a
    /// fully-warded hit"</em>.
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
    /// 🔒 Outside a damage context there is no basis, and 0 would spell "the hit was fully absorbed" —
    /// which `05` §4.1 says a leech still heals off. Steering S6: fail loudly.
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

    /// <summary>
    /// `18` §2.2 / §7.4 — <c>PK_UNBREAKABLE</c>'s second clause: a ward of 25% of the holder's Max HP.
    /// </summary>
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

    /// <summary>
    /// 🔒 `18` §2.2's <c>PK_TRANSFUSION</c> — overheal into a ward, with its per-instance
    /// <c>sourceCapPct</c> travelling to the pool rather than being applied per grant.
    /// </summary>
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

    /// <summary>
    /// 🔒 `18` §2.2 — <c>HEAL_AMOUNT</c> / <c>OVERHEAL_AMOUNT</c> <em>"exist only inside
    /// <c>ON_HEAL</c> contexts"</em>. 0 would silently delete <c>PK_TRANSFUSION</c>'s shield.
    /// </summary>
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

    /// <summary>
    /// 🔒 R4 / `05` §4.2 — <c>REFLECT</c> <em>"adds to <c>THORN</c> for its duration"</em>, and
    /// `05` §1 types <c>THORN</c> as a fraction. The Reflective elite's 25%.
    /// </summary>
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

        // 🔒 05 §4.2: "adds to THORN FOR ITS DURATION" — the second half of the row.
        bench.OnlyLifetime("AddThorns").Duration!.Scope.ShouldBe(DurationScope.BATTLE);
    }

    /// <summary>An op that resolves against an empty enemy set does nothing, and that is not a failure.</summary>
    /// <remarks>
    /// M2-05's uniform rule: a token whose <em>subject</em> is absent throws; a token whose
    /// <em>set</em> is empty resolves to the empty set. The last enemy dying mid-tick is the case.
    /// </remarks>
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
