using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Guardrails;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Rules;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>Guardrail 5: the closed form, its ceiling, and the controls that show it discriminates.</summary>
[Collection(WallClockSensitive.Name)]
public sealed class MitigationGuardrailTests
{
    private static MitigationModel Model => new(MitigationDials.Read(ShippedHarness.Content));

    [Fact]
    public void The_closed_form_is_05_4_step_3_written_out()
    {
        // effDef / (effDef + 120 + 20 x attackerLevel).
        Model.Mitigation(100.0, 0.0, 10).ShouldBe(100.0 / 420.0, tolerance: 1e-12);

        // Attacker level is in the denominator, so the same wall mitigates less against a
        // higher-level attacker — a sign error would reverse this.
        Model.Mitigation(100.0, 0.0, 100).ShouldBeLessThan(Model.Mitigation(100.0, 0.0, 10));

        // PEN reduces the defender's DEF before the curve, so it lowers mitigation.
        Model.Mitigation(100.0, 0.5, 10).ShouldBe(50.0 / 370.0, tolerance: 1e-12);
        Model.Mitigation(100.0, 0.5, 10).ShouldBeLessThan(Model.Mitigation(100.0, 0.0, 10));
    }

    [Fact]
    public void Mitigation_is_strictly_increasing_in_DEF_which_is_what_makes_the_enumeration_exhaustive()
    {
        var model = Model;
        var values = new[] { 0.0, 10.0, 100.0, 1_000.0, 100_000.0 }
            .Select(def => model.Mitigation(def, 0.0, 40))
            .ToArray();

        values[0].ShouldBe(0.0);
        for (var i = 1; i < values.Length; i++)
        {
            values[i].ShouldBeGreaterThan(values[i - 1]);
        }

        values[^1].ShouldBeLessThan(1.0);
    }

    [Fact]
    public void The_ceiling_is_05_9_s_zero_point_eight_five()
    {
        MitigationModel.Ceiling.ShouldBe(0.85);
    }

    [Fact]
    public void A_DEF_that_reaches_a_given_mitigation_is_exact_in_both_directions()
    {
        var model = Model;

        // The inverse is what lets the discriminating control below name an exact breaching DEF
        // rather than search for one.
        foreach (var target in new[] { 0.50, 0.84, 0.85, 0.86, 0.99 })
        {
            var def = model.DefReaching(target, attackerPen: 0.0, attackerLevel: 40);
            model.Mitigation(def, 0.0, 40).ShouldBe(target, tolerance: 1e-9);
        }

        // And with penetration in play, so the (1 - PEN) division is exercised too.
        var withPen = model.DefReaching(0.86, attackerPen: 0.25, attackerLevel: 40);
        model.Mitigation(withPen, 0.25, 40).ShouldBe(0.86, tolerance: 1e-9);

        Should.Throw<ArgumentOutOfRangeException>(() => model.DefReaching(1.0, 0.0, 40));
        Should.Throw<ArgumentOutOfRangeException>(() => model.DefReaching(-0.1, 0.0, 40));
    }

    [Fact]
    public void A_DEF_just_over_and_just_under_the_ceiling_is_reported_correctly()
    {
        var model = Model;

        var over = model.DefReaching(0.8501, attackerPen: 0.0, attackerLevel: 40);
        var under = model.DefReaching(0.8499, attackerPen: 0.0, attackerLevel: 40);

        model.Mitigation(over, 0.0, 40).ShouldBeGreaterThan(MitigationModel.Ceiling);
        model.Mitigation(under, 0.0, 40).ShouldBeLessThan(MitigationModel.Ceiling);
        over.ShouldBeGreaterThan(under);
    }

    [Fact]
    public void The_guardrail_passes_when_every_derivable_DEF_sits_under_the_ceiling()
    {
        // The whole game breaches (see the case below), so the pass path is exercised over a subject
        // set the harness derives the same way but at a par power low enough to stay under: one
        // chapter's worth of enemies at a tiny par. Without this, "guardrail 5 fails" could equally
        // mean "guardrail 5 always fails".
        var result = Evaluate(smallParPower: true);

        result.Verdict.ShouldBe(GuardrailVerdict.Pass);
        result.SubjectCount.ShouldBeGreaterThan(1000);
    }

    [Fact]
    public void The_guardrail_fires_on_the_shipped_game_and_names_where()
    {
        var result = Evaluate(smallParPower: false);

        result.Verdict.ShouldBe(GuardrailVerdict.Fail);
        result.SubjectCount.ShouldBeGreaterThan(100_000);

        // Which subject is the maximum, not merely that a maximum was printed — the named corner is
        // the one the authored model forces, and a maximum found anywhere else means the enumeration
        // or the ordering moved.
        result.Summary.ShouldContain("maximum reached is", Case.Sensitive);
        result.Summary.ShouldContain(
            "vs ARMORED Elite WARDEN at stage3 node 41", Case.Sensitive,
            "the worst reachable wall the authored coefficients allow");

        // Not asserted here: that the details name a hero-side subject too. Details carry only the
        // worst sample per (chapter, tier) and an enemy wall wins every one of those groups, so a
        // hero-side line never appears there — both directions are pinned by subject count instead,
        // in Both_directions_of_the_guardrail_are_present_in_the_subject_set below.
        string.Join("\n", result.Details).ShouldContain("BREACH", Case.Sensitive);
    }

    [Fact]
    public void Both_directions_of_the_guardrail_are_present_in_the_subject_set()
    {
        // Guardrail 5 is two assertions. A one-directional implementation would still produce a large
        // subject count and a plausible maximum, so the count itself is the probe.
        var enemies = EnemyModel.Read(ShippedHarness.Content);
        var bosses = BossRoster.Read(ShippedHarness.Content);
        var parPower = ParPowerTable.Read(ShippedHarness.Content);

        var enemyDefs = MitigationGuardrail.ReachableEnemyDefs(enemies, bosses, 1, 1000.0);

        // 42 spine nodes × 8 archetypes × (plain, elite, armoured elite) + 1 boss.
        enemyDefs.Count.ShouldBe((42 * 8 * 3) + 1);

        // One hero, in one (chapter, tier). Evaluate produces an enemy-side subject only where a hero
        // exists, so the count is exactly the enemy walls of chapter 1 plus the ONE hero-side subject.
        // A one-directional implementation lands on enemyDefs.Count or on 1, never on their sum.
        var oneHero = new[] { new ParHeroDef(1, Tier.NORMAL, "ARCH_CRIT", 60.0, 0.1) };
        var result = MitigationGuardrail.Evaluate(Model, enemies, bosses, parPower, oneHero);

        result.SubjectCount.ShouldBe(enemyDefs.Count + 1);
    }

    private static GuardrailResult Evaluate(bool smallParPower)
    {
        var runner = ShippedHarness.Runner;
        var heroes = new List<ParHeroDef>();

        foreach (var chapter in runner.ParPower.Chapters)
        {
            foreach (var tier in Tiers.All)
            {
                foreach (var archetype in runner.Calibration.Archetypes)
                {
                    var scaled = runner.ParHero(chapter, tier, archetype.Stats);
                    heroes.Add(ParHeroDef.From(chapter, tier, archetype.Id, scaled.Stats));
                }
            }
        }

        return MitigationGuardrail.Evaluate(
            Model,
            runner.Enemies,
            runner.Bosses,
            smallParPower ? TinyPar() : runner.ParPower,
            smallParPower ? heroes.Where(h => h.Chapter == 1 && h.Tier == Tier.NORMAL).ToArray() : heroes);
    }

    /// <summary>
    /// The authored par table with every cell scaled down, so the derived DEFs land under the ceiling.
    /// Built through <c>GameDataLoader.LoadWith</c> rather than a fake table, so the reader, the
    /// derivation and the guardrail under test are all the shipped ones.
    /// </summary>
    private static ParPowerTable TinyPar()
    {
        var text = File.ReadAllText(Path.Combine(GameDataLoader.DataRoot, ParPowerTable.Document));
        foreach (var (from, to) in new[]
                 {
                     ("\"NORMAL\": 1000", "\"NORMAL\": 10"),
                     ("\"HEROIC\": 4000", "\"HEROIC\": 40"),
                     ("\"MYTHIC\": 16000", "\"MYTHIC\": 160"),
                 })
        {
            text = text.Replace(from, to, StringComparison.Ordinal);
        }

        return ParPowerTable.Read(GameDataLoader.LoadWith(
            GameDataLoader.DataRoot,
            new Dictionary<string, string>(StringComparer.Ordinal) { [ParPowerTable.Document] = text }));
    }
}
