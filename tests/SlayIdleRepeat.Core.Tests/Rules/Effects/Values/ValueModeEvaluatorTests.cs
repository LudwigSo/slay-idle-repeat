using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Values;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Values;

/// <summary>The eight amount <c>valueMode</c>s — what an effect's <c>value</c> is a multiple of — plus <c>NEGATE</c>, which denotes no amount at all.</summary>
/// <remarks>
/// This evaluator owns the value; each op owns its use of the result. So every case here is stated as
/// "this mode over these subjects is this number", never as "this op deals this damage".
/// </remarks>
public sealed class ValueModeEvaluatorTests
{
    // ───────────────────────────────────────────────────────── the eight amount modes

    /// <summary><c>value</c> is a multiple of the source's ATK — the default.</summary>
    [Fact]
    public void ATK_MULT_multiplies_the_sources_ATK()
    {
        Resolve(ValueMode.ATK_MULT, 2.0, Full()).ShouldBe(
            600.0, "18 §7.7's PET_STORMFANG active is value 2.0 against a 300 ATK source");
    }

    /// <summary><c>FLAT</c> — an absolute amount, and the one mode authored on a stat op.</summary>
    [Fact]
    public void FLAT_is_the_value_itself()
    {
        Resolve(ValueMode.FLAT, 1.0, Full()).ShouldBe(
            1.0, "18 §9.1's CP_GLASS_HEART sets MAX_HP to a flat 1");
    }

    /// <summary>Ossify — <c>SHIELD 0.20 SELF_MAXHP_PCT</c>.</summary>
    [Fact]
    public void SELF_MAXHP_PCT_is_a_fraction_of_the_sources_Max_HP()
    {
        Resolve(ValueMode.SELF_MAXHP_PCT, 0.20, Full()).ShouldBe(
            160.0, "18 §7.10: Ossify wards 20% of the Ossuary King's 800 Max HP");
    }

    /// <summary>The Volatile elite — <c>DAMAGE_MAXHP_PCT 0.15 TARGET_MAXHP_PCT</c>.</summary>
    [Fact]
    public void TARGET_MAXHP_PCT_is_a_fraction_of_the_targets_Max_HP()
    {
        Resolve(ValueMode.TARGET_MAXHP_PCT, 0.15, Full()).ShouldBe(
            75.0, "18 §7.10: the Volatile explosion is 15% of the hero's 500 Max HP");
    }

    /// <summary><c>TARGET_MISSING_HP_PCT</c> — Max HP less current HP.</summary>
    [Fact]
    public void TARGET_MISSING_HP_PCT_is_a_fraction_of_what_the_target_has_lost()
    {
        Resolve(ValueMode.TARGET_MISSING_HP_PCT, 0.25, Full()).ShouldBe(
            50.0, "the target is at 300 of 500, so 200 missing, and a quarter of that is 50");
    }

    /// <summary>
    /// <c>TARGET_MISSING_HP_PCT</c> floors at zero, because "missing HP" and <c>SELF_MISSING_HP_PCT</c>
    /// are the same quantity, typed <c>0..1</c>. <c>CurrentHp &gt; MaxHp</c> is reachable — a Max HP
    /// decrease from a buff expiring, or <c>CP_GLASS_HEART</c>'s re-base — and unfloored the mode
    /// returns a negative amount, so an execute effect would heal the target it was meant to finish.
    /// </summary>
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

    /// <summary><c>DAMAGE_DEALT_PCT</c> — <c>HEAL_LEECH</c>'s basis.</summary>
    [Fact]
    public void DAMAGE_DEALT_PCT_is_a_fraction_of_the_damage_just_dealt()
    {
        Resolve(ValueMode.DAMAGE_DEALT_PCT, 0.10, Full()).ShouldBe(12.0, "10% of the 120 just dealt");
    }

    /// <summary><c>HEAL_AMOUNT</c> is the full amount actually healed.</summary>
    [Fact]
    public void HEAL_AMOUNT_is_the_amount_actually_healed()
    {
        Resolve(ValueMode.HEAL_AMOUNT, 1.0, Full()).ShouldBe(80.0);
    }

    /// <summary><c>PK_TRANSFUSION</c> — overheal converts into a shield. <c>OVERHEAL_AMOUNT</c> is the clipped excess.</summary>
    [Fact]
    public void OVERHEAL_AMOUNT_is_the_clipped_excess()
    {
        Resolve(ValueMode.OVERHEAL_AMOUNT, 1.0, Full()).ShouldBe(
            30.0, "18 §2.2: PK_TRANSFUSION shields value 1.0 x the overheal");
    }

    /// <summary>
    /// Every mode is handled, and each answers with its own subject — a mode reaching an unhandled arm
    /// must fail rather than fall through. An expected value per mode rather than
    /// <c>Should.NotThrow</c>, which a single <c>default</c> arm answering all eight would also
    /// satisfy — proven by stubbing <c>Resolve</c> as <c>=&gt; 0.0</c>, at which point the loop went
    /// green while every per-mode fact went red. At <c>value = 1.0</c> the eight answers are just the
    /// eight subjects, each pinned individually above and all distinct, so no <c>default</c> arm of
    /// any shape survives.
    /// </summary>
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

        modes.Length.ShouldBe(9, "18 §2.2's eight amount modes plus NEGATE (16 D49)");
        expected.Keys.Append(ValueMode.NEGATE).Order().ShouldBe(
            modes.Order(), "a tenth mode is unhandled here until this table answers for it");

        foreach (var mode in expected.Keys)
        {
            Resolve(mode, 1.0, Full()).ShouldBe(
                expected[mode], $"18 §2.2's {mode} reached no handler of its own in ValueModeEvaluator");
        }
    }

    /// <summary>
    /// The ninth mode's own arm: NEGATE denotes no amount — SURVIVE_LETHAL consumes it before value
    /// resolution — so reaching this evaluator with it must fail by name, not answer a number some
    /// caller would spend. The message fragment pins WHICH arm refused: the catch-all "not one of
    /// 18 §2.2's" arm says nothing about an amount.
    /// </summary>
    [Fact]
    public void NEGATE_reaching_amount_resolution_throws_and_names_itself()
    {
        var thrown = Should.Throw<EffectContextException>(
            () => Resolve(ValueMode.NEGATE, 1.0, Full()));

        thrown.Token.ShouldBe(nameof(ValueMode.NEGATE));
        thrown.Message.ShouldContain("not an amount", Case.Sensitive);
        thrown.Message.ShouldContain("SURVIVE_LETHAL", Case.Sensitive);
    }

    // ───────────────────────────────────────────── an absent SUBJECT throws, and says which

    /// <summary>
    /// <c>HEAL_AMOUNT</c> and <c>OVERHEAL_AMOUNT</c> exist only inside <c>ON_HEAL</c> contexts.
    /// Outside one the subject is absent, and the layer's uniform rule applies: throw, naming the
    /// mode. A silent zero is byte-identical to a legitimate zero heal — <c>Heal()</c> on a full-HP
    /// target heals 0 and overheals the lot — so the quiet answer is the one that can never go red.
    /// </summary>
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
    /// <c>FLAT</c> is the one mode with no subject at all, so it resolves against an empty bundle.
    /// Without this the rule above would read as "every mode needs a context", which is wrong and
    /// would make <c>CP_GLASS_HEART</c> unresolvable at aggregation time.
    /// </summary>
    [Fact]
    public void FLAT_needs_no_subject()
    {
        Resolve(ValueMode.FLAT, 1.0, default).ShouldBe(1.0);
    }

    // ───────────────────────────────────────────── the documented default

    /// <summary>
    /// <c>valueMode</c> defaults to <c>ATK_MULT</c> — stated once, here, so the other ops do not each
    /// restate it. The constant alone cannot fail for any implementation bug — what makes the claim
    /// testable is resolving through it: the default has to behave as <c>ATK_MULT</c>, not merely
    /// spell it.
    /// </summary>
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
