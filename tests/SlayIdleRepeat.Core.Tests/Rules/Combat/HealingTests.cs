using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary><c>Heal()</c>, its <c>HEAL%</c>, its overheal, and the <c>ON_HEAL</c> it fires.</summary>
/// <remarks>
/// Internal bench seam: each case sets the recipient's exact HP before an exactly-sized heal, which
/// no public entry point can stage mid-fight. The two readings are observed through a <c>SHIELD</c>
/// with <c>valueMode: OVERHEAL_AMOUNT</c>, so each reading becomes a ward whose <c>Shield</c> event
/// carries it — a stronger channel than a recording double, since <c>OpValue</c> throws when the
/// context carries no heal.
/// </remarks>
public sealed class HealingTests
{
    private const double MaxHp = 1000.0;

    /// <summary>
    /// <c>healed = min(amount × target.HEALPct, …)</c>, with <c>HEAL%</c> the recipient's stat. The
    /// healer's <c>HEAL_PCT</c> is 3.0 in every row and never appears in an expectation — the part a
    /// plausible implementation gets backwards, since <c>HEAL_LEECH</c> reads like the healer's stat.
    /// </summary>
    [Theory]
    [InlineData(1.0, 100.0, 100.0)]
    [InlineData(1.35, 100.0, 135.0)]   // "+35% Healing Received" => x1.35
    [InlineData(0.5, 100.0, 50.0)]
    public void Heal_applies_the_recipients_HEAL_PCT(double healPct, double amount, double expected) =>
        Fight(
            recipientHealPct: healPct,
            healerHealPct: 3.0,
            body: p =>
            {
                p.Enemy().SetCurrentHp(1.0);

                p.Pipeline.Heal(p.Enemy(), amount, "EFF_H");

                p.Enemy().CurrentHp.ShouldBe(1.0 + expected);
            });

    /// <summary>
    /// The heal is clipped at <c>MaxHP − HP</c> and the excess is overheal, discarded unless an
    /// effect consumes it. Three rows: no overheal, a split, and a pure overheal — the last matters
    /// because <c>ON_HEAL</c> fires unconditionally even into a full bar.
    /// </summary>
    [Theory]
    [InlineData(900.0, 100.0, 100.0, 0.0)]
    [InlineData(900.0, 250.0, 100.0, 150.0)]
    [InlineData(1000.0, 250.0, 0.0, 250.0)]
    public void The_heal_is_clipped_at_Max_HP_and_the_rest_is_overheal(
        double startingHp, double amount, double expectedHealed, double expectedOverheal)
    {
        var probe = Fight(
            readings: true,
            body: p =>
            {
                p.Enemy().SetCurrentHp(startingHp);

                p.Pipeline.Heal(p.Enemy(), amount, "EFF_H");

                p.Enemy().CurrentHp.ShouldBe(startingHp + expectedHealed);
            });

        // Ascending effect-id order: EFF_A_HEAL_AMOUNT before EFF_B_OVERHEAL_AMOUNT.
        var grants = probe.EventsOf(CombatEventType.Shield);

        grants.Count.ShouldBe(2, "`05` §4.3 fires ON_HEAL for every heal, a pure overheal included");
        grants[0].Value.ShouldBe(expectedHealed, "HEAL_AMOUNT");
        grants[1].Value.ShouldBe(expectedOverheal, "OVERHEAL_AMOUNT");
    }

    /// <summary>
    /// <c>ON_HEAL</c> fires after the HP is applied: an <c>ON_HEAL</c> resolved before the write
    /// would see the pre-heal bar, and every HP-fraction condition on a heal reaction would gate on
    /// the wrong number. <c>TARGET_MISSING_HP_PCT</c> is the reading that separates them — 600 after
    /// the heal, 900 before it.
    /// </summary>
    [Fact]
    public void ON_HEAL_is_fired_after_the_HP_is_applied()
    {
        var probe = Fight(
            missingHpProbe: true,
            body: p =>
            {
                p.Enemy().SetCurrentHp(100.0);

                p.Pipeline.Heal(p.Enemy(), 300.0, "EFF_H");
            });

        probe.EventsOf(CombatEventType.Shield).Single().Value.ShouldBe(
            600.0, "1000 - 400 after the heal; before it the reading would be 900");
    }

    /// <summary>A <c>Heal</c> carries the amount actually healed, not the amount asked for.</summary>
    [Fact]
    public void The_Heal_event_carries_what_was_actually_healed()
    {
        var probe = Fight(body: p =>
        {
            p.Enemy().SetCurrentHp(950.0);

            p.Pipeline.Heal(p.Enemy(), 400.0, "EFF_H");
        });

        probe.EventsOf(CombatEventType.Heal).Single().Value.ShouldBe(50.0);
    }

    /// <summary>
    /// A negative <c>HEAL%</c> heals nothing rather than dealing damage: an unclamped multiplier
    /// could turn a heal into an HP decrease that emits no <c>Hit</c> and is observed by nothing. The
    /// clamp is at the scaling, so an overheal reader gets 0 rather than a negative number.
    /// </summary>
    [Fact]
    public void A_negative_HEAL_PCT_heals_nothing_rather_than_dealing_damage()
    {
        var probe = Fight(
            recipientHealPct: -0.2,
            readings: true,
            body: p =>
            {
                p.Enemy().SetCurrentHp(500.0);

                p.Pipeline.Heal(p.Enemy(), 100.0, "EFF_H");

                p.Enemy().CurrentHp.ShouldBe(500.0);
            });

        var grants = probe.EventsOf(CombatEventType.Shield);

        grants[0].Value.ShouldBe(0.0, "HEAL_AMOUNT");
        grants[1].Value.ShouldBe(0.0, "OVERHEAL_AMOUNT is not negative either");
    }

    /// <summary>
    /// Lifesteal routes through <c>Heal()</c>, so the attacker's (as the heal's recipient)
    /// <c>HEAL%</c> applies and it fires <c>ON_HEAL</c>. Lifesteal written straight onto HP would
    /// answer the same in both rows and would fire nothing.
    /// </summary>
    [Theory]
    [InlineData(1.0, 21.54)]
    [InlineData(2.0, 43.08)]
    public void Lifesteal_routes_through_Heal_so_the_attackers_HEAL_PCT_applies(
        double healPct, double expected)
    {
        var probe = AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(
                    Stats(MaxHp, (StatId.ATK, 100.0), (StatId.LIFESTEAL, 0.4), (StatId.HEAL_PCT, healPct)),
                    1,
                    OnHeal("EFF_A_HEAL_AMOUNT", ValueMode.HEAL_AMOUNT)),
                BattleTestBench.Enemy(0, Stats(5000.0, (StatId.DEF, 120.0))),
            },
            p =>
            {
                p.Hero.SetCurrentHp(1.0);

                // 53.85 x 0.4 = 21.54, before the RECIPIENT's HEAL% — and here the recipient is the
                // attacker, which is what makes the two rows differ.
                p.Pipeline.ResolveAttack(p.Hero, p.Enemy(), 1.0, "EFF_X").Basis.ShouldBe(53.85);

                p.Hero.CurrentHp.ShouldBe(1.0 + expected);
            });

        probe.EventsOf(CombatEventType.Heal).Single().Value.ShouldBe(expected);
        probe.EventsOf(CombatEventType.Shield).Single().Value.ShouldBe(
            expected, "lifesteal is a heal, so it fired ON_HEAL with HEAL_AMOUNT in hand");
    }

    /// <summary>
    /// <c>HEAL_CEILING</c> bounds <c>Heal()</c> in place of Max HP: "you can no longer be healed
    /// above X% Max HP". The two ceiling rows differ in whether the recipient starts below the bar
    /// (heals to it and stops) or above it (heals nothing rather than damage down to it); the control
    /// holds no override and reaches full Max HP.
    /// </summary>
    [Theory]
    [InlineData(0.8, 700.0, 250.0, 100.0, 150.0)]  // ceiling 800: heals to the bar, rest overheals
    [InlineData(0.5, 900.0, 250.0, 0.0, 250.0)]    // ceiling 500, already above it: heals nothing
    [InlineData(null, 700.0, 250.0, 250.0, 0.0)]   // control: no override, Max HP 1000 is the bar
    public void A_HEAL_CEILING_bounds_the_heal_in_place_of_Max_HP(
        double? ceilingFraction,
        double startingHp,
        double amount,
        double expectedHealed,
        double expectedOverheal)
    {
        var probe = Fight(
            readings: true,
            healCeiling: ceilingFraction,
            body: p =>
            {
                p.Enemy().SetCurrentHp(startingHp);

                p.Pipeline.Heal(p.Enemy(), amount, "EFF_H");

                p.Enemy().CurrentHp.ShouldBe(startingHp + expectedHealed);
            });

        probe.EventsOf(CombatEventType.Heal).Single().Value.ShouldBe(expectedHealed);

        probe.EventsOf(CombatEventType.Shield)
            .Select(e => e.Value)
            .ShouldBe(new[] { expectedHealed, expectedOverheal });
    }

    /// <summary>
    /// The ceiling is a fraction of Max HP, so a Max HP that moves mid-fight moves the bar with it:
    /// the same 0.8 override answers 800 against a 1000 HP block and 400 against a 500 HP one.
    /// </summary>
    [Theory]
    [InlineData(1000.0, 800.0)]
    [InlineData(500.0, 400.0)]
    public void The_ceiling_is_a_fraction_of_the_live_Max_HP(double maxHp, double expectedBar) =>
        Fight(
            healCeiling: 0.8,
            recipientMaxHp: maxHp,
            body: p =>
            {
                p.Enemy().SetCurrentHp(1.0);

                p.Pipeline.Heal(p.Enemy(), maxHp * 10.0, "EFF_H");

                p.Enemy().CurrentHp.ShouldBe(expectedBar);
            });

    // ══════════════════════════════════════════════════════ helpers

    /// <summary>
    /// One untriggered <c>STAT_CAP_OVERRIDE</c> carrying <c>HEAL_CEILING</c>. Untriggered is what
    /// puts it in the standing set, the only route to the aggregate.
    /// </summary>
    private static HeldEffect HealCeiling(double fraction) =>
        new(new EffectDefinition
        {
            Id = "EFF_C_HEAL_CEILING",
            Op = EffectOp.STAT_CAP_OVERRIDE,
            Target = EffectTarget.SELF,
            Stat = StatSelector.Of(StatId.MAX_HP),
            CapKind = StatCapKind.HEAL_CEILING,
            Value = fraction,
        });

    /// <summary>
    /// See <see cref="AttackPipelineBench.Stats"/>, whose <c>HEAL_PCT = 1.0</c> default this file
    /// depends on more than any other.
    /// </summary>
    private static ActorStats Stats(double maxHp, params (StatId Stat, double Value)[] rest) =>
        AttackPipelineBench.Stats(maxHp, rest);

    /// <summary>A <c>SHIELD</c> on <c>SELF</c>, triggered by <c>ON_HEAL</c>, whose value mode is the reading being observed.</summary>
    private static HeldEffect OnHeal(string id, ValueMode mode) =>
        new(new EffectDefinition
        {
            Id = id,
            Op = EffectOp.SHIELD,
            Target = EffectTarget.SELF,
            Value = 1.0,
            ValueMode = mode,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_HEAL },
        });

    /// <summary>One probe fight whose enemy is the heal recipient.</summary>
    /// <param name="body">The probe.</param>
    /// <param name="recipientHealPct"><c>target.HEALPct</c>.</param>
    /// <param name="healerHealPct">The healer's, which the formula never reads.</param>
    /// <param name="readings">
    /// Whether the recipient holds the two <c>ON_HEAL</c> probes that convert <c>healed</c> and
    /// <c>overheal</c> into <c>Shield</c> events.
    /// </param>
    /// <param name="missingHpProbe">
    /// Whether it holds the single <c>TARGET_MISSING_HP_PCT</c> probe instead, read at the moment the
    /// trigger fired.
    /// </param>
    /// <param name="healCeiling">The <c>HEAL_CEILING</c> fraction, or <c>null</c> for the majority case.</param>
    /// <param name="recipientMaxHp">The <c>MAX_HP</c> the ceiling is a fraction of.</param>
    private static AttackProbe Fight(
        Action<AttackProbe> body,
        double recipientHealPct = 1.0,
        double healerHealPct = 1.0,
        bool readings = false,
        bool missingHpProbe = false,
        double? healCeiling = null,
        double recipientMaxHp = MaxHp)
    {
        var holdings = new List<HeldEffect>();

        if (readings)
        {
            holdings.Add(OnHeal("EFF_A_HEAL_AMOUNT", ValueMode.HEAL_AMOUNT));
            holdings.Add(OnHeal("EFF_B_OVERHEAL_AMOUNT", ValueMode.OVERHEAL_AMOUNT));
        }
        else if (missingHpProbe)
        {
            holdings.Add(OnHeal("EFF_A_MISSING", ValueMode.TARGET_MISSING_HP_PCT));
        }

        if (healCeiling is { } fraction)
        {
            holdings.Add(HealCeiling(fraction));
        }

        return AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(Stats(MaxHp, (StatId.ATK, 100.0), (StatId.HEAL_PCT, healerHealPct)), 1),
                BattleTestBench.Enemy(
                    0,
                    Stats(recipientMaxHp, (StatId.DEF, 120.0), (StatId.HEAL_PCT, recipientHealPct)),
                    effects: holdings.ToArray()),
            },
            body);
    }
}
