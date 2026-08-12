using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §4.3 — <c>Heal()</c>, its <c>HEAL%</c>, its overheal, and the <c>ON_HEAL</c> it fires.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The two readings are observed through the effect `18` §2.2 wrote them for.</b>
/// <c>PK_TRANSFUSION</c> is a <c>SHIELD</c> with <c>valueMode: OVERHEAL_AMOUNT</c>, and
/// <c>OpValueRules.Shield</c> admits both heal readings — so an <c>ON_HEAL</c> holding of that exact
/// shape converts each reading into a ward whose <c>Shield</c> event carries it.
/// </para>
/// <para>
/// 🔒 That is a stronger channel than a recording double, because of how the readings <em>fail</em>:
/// <c>OpValue</c> throws for <c>HEAL_AMOUNT</c> and <c>OVERHEAL_AMOUNT</c> when the context does not
/// carry one (`18` §2.2's <em>"exist only inside <c>ON_HEAL</c> contexts"</em>, steering S6). A
/// pipeline that fired <c>ON_HEAL</c> without threading the readings would make these cases throw
/// rather than record a zero.
/// </para>
/// </remarks>
public sealed class HealingTests
{
    private const double MaxHp = 1000.0;

    /// <summary>
    /// 🔒 `05` §4.3 — <c>healed = min(amount × target.HEALPct, …)</c>, with <c>HEAL%</c> the
    /// <b>recipient's</b> stat.
    /// </summary>
    /// <remarks>
    /// The healer's <c>HEAL_PCT</c> is 3.0 in every row and never appears in an expectation. That is
    /// the discriminating part: "the recipient's" is the half of `05` §4.3 a plausible
    /// implementation gets backwards, because <c>HEAL_LEECH</c> reads like the healer's stat.
    /// </remarks>
    [Theory]
    [InlineData(1.0, 100.0, 100.0)]
    [InlineData(1.35, 100.0, 135.0)]   // 05 §2: "+35% Healing Received" ⇒ x1.35
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
    /// 🔒 `05` §4.3 — the heal is clipped at <c>MaxHP − HP</c> and the excess is <c>overheal</c>,
    /// <em>"discarded unless an effect consumes it"</em>.
    /// </summary>
    /// <remarks>
    /// Three rows: no overheal, a split, and a pure overheal. The last is the one that matters —
    /// `05` §4.3 fires <c>ON_HEAL</c> unconditionally, and a heal into a full bar is exactly when
    /// <c>PK_TRANSFUSION</c>'s input is largest.
    /// </remarks>
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

        // Ascending effect-id order (`18` §8): EFF_A_HEAL_AMOUNT before EFF_B_OVERHEAL_AMOUNT.
        var grants = probe.EventsOf(CombatEventType.Shield);

        grants.Count.ShouldBe(2, "`05` §4.3 fires ON_HEAL for every heal, a pure overheal included");
        grants[0].Value.ShouldBe(expectedHealed, "HEAL_AMOUNT");
        grants[1].Value.ShouldBe(expectedOverheal, "OVERHEAL_AMOUNT");
    }

    /// <summary>
    /// 🔒 `05` §4.3 fires <c>ON_HEAL</c> <b>after the HP is applied</b> — <em>"which is what makes
    /// <c>HEAL_AMOUNT</c> and <c>OVERHEAL_AMOUNT</c> readable"</em> (<c>TriggerRegistry</c>).
    /// </summary>
    /// <remarks>
    /// The subject is the HP the trigger <em>observes</em>, not that it fired: an <c>ON_HEAL</c>
    /// resolved before the write would see the pre-heal bar, and every <c>SELF_HP_PCT</c> condition
    /// on a heal reaction would then gate on the wrong number. `18` §2.2's
    /// <c>TARGET_MISSING_HP_PCT</c> is the reading that separates them — 600 after the heal, 900
    /// before it.
    /// </remarks>
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

    /// <summary>
    /// 🔒 `05` §7 — a <c>Heal</c> carries <em>"the amount actually healed"</em>, not the amount
    /// asked for.
    /// </summary>
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
    /// ⚠️ <b>Errata against `05` §4.3</b> — a negative <c>HEAL%</c> heals nothing rather than
    /// dealing damage.
    /// </summary>
    /// <remarks>
    /// `05` §4.3 as written is <c>healed = min(amount × HEALPct, MaxHP − HP)</c> with no floor, and
    /// `05` §5's <c>SPORE</c> is <em>"−X% healing received, stacks to 4"</em> — four stacks past
    /// −25% each take the multiplier below zero, at which point the formula turns a heal into an HP
    /// <b>decrease</b> that emits no <c>Hit</c>, runs no phase check and is observed by nothing. The
    /// clamp is at the scaling, so the overheal a <c>PK_TRANSFUSION</c> reads is 0 rather than
    /// negative. Recorded rather than hidden.
    /// </remarks>
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
    /// 🔒 `05` §4 step 10's lifesteal routes through <c>Heal()</c> — so the <b>attacker's</b>
    /// <c>HEAL%</c> applies, and the heal fires <c>ON_HEAL</c>.
    /// </summary>
    /// <remarks>
    /// The two rows differ only in the attacker's <c>HEAL_PCT</c>. Lifesteal written straight onto
    /// HP would answer the same in both and would fire nothing — and <c>TriggerRegistry</c> lists
    /// lifesteal as one of the three sources of that moment.
    /// </remarks>
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

    // ══════════════════════════════════════════════════════ helpers

    /// <summary>A `05` §1 block with the named stats set and the `05` §2 bases for the rest.</summary>
    private static ActorStats Stats(double maxHp, params (StatId Stat, double Value)[] rest)
    {
        var values = new List<(StatId, double)>
        {
            (StatId.MAX_HP, maxHp),
            (StatId.ASPD, 1.0),
            (StatId.HEAL_PCT, 1.0),
        };

        values.AddRange(rest.Select(r => (r.Stat, r.Value)));

        return StatFixtures.Block(values.ToArray());
    }

    /// <summary>
    /// One <c>ON_HEAL</c> holding shaped like <c>PK_TRANSFUSION</c> — a <c>SHIELD</c> on
    /// <c>SELF</c> whose value mode is the reading being observed.
    /// </summary>
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
    /// <param name="recipientHealPct">`05` §4.3's <c>target.HEALPct</c>.</param>
    /// <param name="healerHealPct">The healer's, which `05` §4.3 never reads.</param>
    /// <param name="readings">
    /// Whether the recipient holds the two <c>ON_HEAL</c> probes that convert `05` §4.3's
    /// <c>healed</c> and <c>overheal</c> into <c>Shield</c> events.
    /// </param>
    /// <param name="missingHpProbe">
    /// Whether it holds the single <c>TARGET_MISSING_HP_PCT</c> probe instead, which reads the
    /// recipient's HP <em>at the moment the trigger fired</em>.
    /// </param>
    private static AttackProbe Fight(
        Action<AttackProbe> body,
        double recipientHealPct = 1.0,
        double healerHealPct = 1.0,
        bool readings = false,
        bool missingHpProbe = false)
    {
        var holdings = Array.Empty<HeldEffect>();

        if (readings)
        {
            holdings = new[]
            {
                OnHeal("EFF_A_HEAL_AMOUNT", ValueMode.HEAL_AMOUNT),
                OnHeal("EFF_B_OVERHEAL_AMOUNT", ValueMode.OVERHEAL_AMOUNT),
            };
        }
        else if (missingHpProbe)
        {
            holdings = new[] { OnHeal("EFF_A_MISSING", ValueMode.TARGET_MISSING_HP_PCT) };
        }

        return AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(Stats(MaxHp, (StatId.ATK, 100.0), (StatId.HEAL_PCT, healerHealPct)), 1),
                BattleTestBench.Enemy(
                    0,
                    Stats(MaxHp, (StatId.DEF, 120.0), (StatId.HEAL_PCT, recipientHealPct)),
                    effects: holdings),
            },
            body);
    }
}
