using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Values;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Values;

/// <summary>
/// 🔒 `18` §2.2 — the eight <c>valueMode</c>s: what an effect's <c>value</c> is a multiple of.
/// </summary>
/// <remarks>
/// M2-06 owns the evaluator; M2-03 owns each op's <em>use</em> of the result. So every case here is
/// stated as "this mode over these subjects is this number", never as "this op deals this damage".
/// </remarks>
public sealed class ValueModeEvaluatorTests
{
    // ───────────────────────────────────────────────────────────── the eight modes

    /// <summary>`18` §2.2: <em>"<c>value</c> is a multiple of the source's ATK"</em> — the default.</summary>
    [Fact]
    public void ATK_MULT_multiplies_the_sources_ATK()
    {
        Resolve(ValueMode.ATK_MULT, 2.0, Full()).ShouldBe(
            600.0, "18 §7.7's PET_STORMFANG active is value 2.0 against a 300 ATK source");
    }

    /// <summary>`18` §2.2's <c>FLAT</c> — an absolute amount, and the one mode `18` §9.1 puts on a stat op.</summary>
    [Fact]
    public void FLAT_is_the_value_itself()
    {
        Resolve(ValueMode.FLAT, 1.0, Full()).ShouldBe(
            1.0, "18 §9.1's CP_GLASS_HEART sets MAX_HP to a flat 1");
    }

    /// <summary>`18` §7.10's Ossify — <c>SHIELD 0.20 SELF_MAXHP_PCT</c>.</summary>
    [Fact]
    public void SELF_MAXHP_PCT_is_a_fraction_of_the_sources_Max_HP()
    {
        Resolve(ValueMode.SELF_MAXHP_PCT, 0.20, Full()).ShouldBe(
            160.0, "18 §7.10: Ossify wards 20% of the Ossuary King's 800 Max HP");
    }

    /// <summary>`18` §7.10's Volatile elite — <c>DAMAGE_MAXHP_PCT 0.15 TARGET_MAXHP_PCT</c>.</summary>
    [Fact]
    public void TARGET_MAXHP_PCT_is_a_fraction_of_the_targets_Max_HP()
    {
        Resolve(ValueMode.TARGET_MAXHP_PCT, 0.15, Full()).ShouldBe(
            75.0, "18 §7.10: the Volatile explosion is 15% of the hero's 500 Max HP");
    }

    /// <summary>`18` §2.2's <c>TARGET_MISSING_HP_PCT</c> — Max HP less current HP.</summary>
    [Fact]
    public void TARGET_MISSING_HP_PCT_is_a_fraction_of_what_the_target_has_lost()
    {
        Resolve(ValueMode.TARGET_MISSING_HP_PCT, 0.25, Full()).ShouldBe(
            50.0, "the target is at 300 of 500, so 200 missing, and a quarter of that is 50");
    }

    /// <summary>
    /// 🔒 <c>TARGET_MISSING_HP_PCT</c> floors at zero, because "missing HP" and `18` §4's
    /// <c>SELF_MISSING_HP_PCT</c> are the same quantity and §4 types that <c>0..1</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Nothing pinned the floor in either direction. <c>CurrentHp &gt; MaxHp</c> is reachable — a Max
    /// HP <em>decrease</em> from a buff expiring, or <c>CP_GLASS_HEART</c>'s re-base — and unfloored the
    /// mode returns a negative amount, so an execute effect would <b>heal</b> the target it was meant to
    /// finish. The DSL states "missing HP" in two places and they must not disagree about one actor in
    /// one tick.
    /// </remarks>
    [Fact]
    public void TARGET_MISSING_HP_PCT_floors_at_zero_when_the_target_is_over_its_Max_HP()
    {
        var overHealed = EffectTestBattle.Hero(currentHp: 600, maxHp: 500);

        var resolved = Resolve(
            ValueMode.TARGET_MISSING_HP_PCT, 0.25, Full() with { Target = overHealed });

        resolved.ShouldBe(0.0, "a target that has lost no HP has lost zero, not a negative amount");

        resolved.ShouldNotBe(
            -25.0, "-25 is 0.25 x (500 - 600) — an execute effect healing the target it should finish");
    }

    /// <summary>`18` §2.2's <c>DAMAGE_DEALT_PCT</c> — <c>HEAL_LEECH</c>'s basis.</summary>
    [Fact]
    public void DAMAGE_DEALT_PCT_is_a_fraction_of_the_damage_just_dealt()
    {
        Resolve(ValueMode.DAMAGE_DEALT_PCT, 0.10, Full()).ShouldBe(12.0, "10% of the 120 just dealt");
    }

    /// <summary>
    /// `05` §4.3 / `18` §2.2 — <c>HEAL_AMOUNT</c> is <em>"the full amount actually healed"</em>.
    /// </summary>
    [Fact]
    public void HEAL_AMOUNT_is_the_amount_actually_healed()
    {
        Resolve(ValueMode.HEAL_AMOUNT, 1.0, Full()).ShouldBe(80.0);
    }

    /// <summary>
    /// `18` §2.2's <c>PK_TRANSFUSION</c> — <em>"overheal converts into a shield"</em>.
    /// <c>OVERHEAL_AMOUNT</c> is <em>"the clipped excess"</em> (`05` §4.3).
    /// </summary>
    [Fact]
    public void OVERHEAL_AMOUNT_is_the_clipped_excess()
    {
        Resolve(ValueMode.OVERHEAL_AMOUNT, 1.0, Full()).ShouldBe(
            30.0, "18 §2.2: PK_TRANSFUSION shields value 1.0 x the overheal");
    }

    /// <summary>
    /// 🔒 <b>Every mode is handled, and each answers with its own subject.</b> S3 — a mode reaching an
    /// unhandled arm must fail rather than fall through.
    /// </summary>
    /// <remarks>
    /// ⚠️ An expected value per mode rather than <c>Should.NotThrow</c>, which a single <c>default</c> arm
    /// answering all eight satisfies — proven by stubbing <c>Resolve</c> as <c>=&gt; 0.0</c>, at which
    /// point the loop went green while every per-mode fact went red. At <c>value = 1.0</c> the eight
    /// answers are just the eight subjects, each pinned individually above and all distinct, so no
    /// <c>default</c> arm of any shape survives.
    /// </remarks>
    [Fact]
    public void Every_18_2_2_value_mode_is_handled()
    {
        var expected = new Dictionary<ValueMode, double>
        {
            [ValueMode.ATK_MULT] = 300.0,
            [ValueMode.FLAT] = 1.0,
            [ValueMode.SELF_MAXHP_PCT] = 800.0,
            [ValueMode.TARGET_MAXHP_PCT] = 500.0,
            [ValueMode.TARGET_MISSING_HP_PCT] = 200.0,
            [ValueMode.DAMAGE_DEALT_PCT] = 120.0,
            [ValueMode.HEAL_AMOUNT] = 80.0,
            [ValueMode.OVERHEAL_AMOUNT] = 30.0,
        };

        var modes = Enum.GetValues<ValueMode>();

        modes.Length.ShouldBe(8, "18 §2.2 lists eight value modes");
        expected.Keys.Order().ShouldBe(
            modes.Order(), "a ninth mode is unhandled here until this table answers for it");

        foreach (var mode in modes)
        {
            Resolve(mode, 1.0, Full()).ShouldBe(
                expected[mode], $"18 §2.2's {mode} reached no handler of its own in ValueModeEvaluator");
        }
    }

    // ───────────────────────────────────────────── an absent SUBJECT throws, and says which

    /// <summary>
    /// 🔒 <c>HEAL_AMOUNT</c> and <c>OVERHEAL_AMOUNT</c> <em>"exist only inside <c>ON_HEAL</c>
    /// contexts"</em> (`18` §2.2, `05` §4.3). Outside one the subject is absent, and the layer's
    /// uniform rule applies: <b>throw</b>, naming the mode (S2).
    /// </summary>
    /// <remarks>
    /// A silent zero is byte-identical to a legitimate zero heal — <c>Heal()</c> on a full-HP target
    /// heals 0 and overheals the lot — so the quiet answer is the one that can never go red.
    /// </remarks>
    [Theory]
    [InlineData(ValueMode.HEAL_AMOUNT)]
    [InlineData(ValueMode.OVERHEAL_AMOUNT)]
    public void A_heal_mode_outside_an_ON_HEAL_context_throws_and_names_itself(ValueMode mode)
    {
        var thrown = Should.Throw<EffectContextException>(
            () => Resolve(mode, 1.0, new ValueModeSubjects { Source = Boss(), Target = Hero() }));

        thrown.Token.ShouldBe(mode.ToString());
        thrown.Message.ShouldContain(EffectContextException.Marker, Case.Sensitive);
        thrown.Message.ShouldContain("ON_HEAL", Case.Sensitive);
        thrown.Message.ShouldContain("05 §4.3");
    }

    /// <summary>
    /// Every other mode has a subject too, and each one throws by name when the context does not
    /// carry it. Pins <em>which</em> mode failed, not merely that something did (S2).
    /// </summary>
    [Theory]
    [InlineData(ValueMode.ATK_MULT)]
    [InlineData(ValueMode.SELF_MAXHP_PCT)]
    [InlineData(ValueMode.TARGET_MAXHP_PCT)]
    [InlineData(ValueMode.TARGET_MISSING_HP_PCT)]
    [InlineData(ValueMode.DAMAGE_DEALT_PCT)]
    public void A_mode_whose_subject_is_absent_throws_and_names_itself(ValueMode mode)
    {
        var thrown = Should.Throw<EffectContextException>(
            () => Resolve(mode, 1.0, default));

        thrown.Token.ShouldBe(mode.ToString());
        thrown.Message.ShouldContain("PK_UNDER_TEST", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <c>FLAT</c> is the one mode with no subject at all, so it resolves against an empty
    /// bundle. Without this the rule above would read as "every mode needs a context", which is
    /// wrong and would make `18` §9.1's <c>CP_GLASS_HEART</c> unresolvable at aggregation time.
    /// </summary>
    [Fact]
    public void FLAT_needs_no_subject()
    {
        Resolve(ValueMode.FLAT, 1.0, default).ShouldBe(1.0);
    }

    // ───────────────────────────────────────────── the documented default

    /// <summary>
    /// `18` §2.2: <em>"<c>valueMode</c>: <c>ATK_MULT</c> (default)"</em> — stated once, here, so the
    /// forty-three ops do not each restate it.
    /// </summary>
    /// <remarks>
    /// ⚠️ The constant alone cannot fail for any implementation bug — it is a literal compared to a
    /// literal. What makes the claim testable is resolving <em>through</em> it: the default has to
    /// behave as <c>ATK_MULT</c>, not merely spell it.
    /// </remarks>
    [Fact]
    public void The_documented_default_for_the_damage_and_healing_ops_is_ATK_MULT()
    {
        ValueModeEvaluator.DamageAndHealingDefault.ShouldBe(ValueMode.ATK_MULT);

        Resolve(ValueModeEvaluator.DamageAndHealingDefault, 2.0, Full()).ShouldBe(
            600.0, "an op that authors no valueMode is a multiple of the source's 300 ATK");
    }

    // ───────────────────────────────────────────── fixtures

    private static double Resolve(ValueMode mode, double value, ValueModeSubjects subjects) =>
        ValueModeEvaluator.Resolve(mode, value, subjects, "PK_UNDER_TEST");

    private static EffectTestActor Boss() =>
        EffectTestBattle.Enemy("BOSS_OSSUARY_KING", 1, currentHp: 800, maxHp: 800) with { IsBoss = true };

    private static EffectTestActor Hero() =>
        EffectTestBattle.Hero(currentHp: 300, maxHp: 500);

    /// <summary>Every subject present — the bundle a full <c>ON_HEAL</c> cascade would carry.</summary>
    private static ValueModeSubjects Full() =>
        new()
        {
            Source = Boss(),
            Target = Hero(),
            SourceAttack = 300.0,
            DamageDealt = 120.0,
            HealAmount = 80.0,
            OverhealAmount = 30.0,
        };
}
