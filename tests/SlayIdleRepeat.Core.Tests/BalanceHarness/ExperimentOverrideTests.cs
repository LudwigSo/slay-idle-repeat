using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Experiments;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔒 `21` §3.2's two experiments — the overrides really change the fight, they never touch
/// <c>game-data/</c>, and an override that matched nothing throws instead of quietly running the
/// baseline twice.
/// </summary>
public sealed class ExperimentOverrideTests
{
    private static string Shipped => BossDocumentOverrides.ReadShipped(GameDataLoader.DataRoot);

    [Fact]
    public void Removing_the_Thornmaw_RAGE_removes_both_the_effect_and_the_phase_mechanic()
    {
        var edited = BossDocumentOverrides.WithoutEffect(
            Shipped, BalanceExperiments.ThornmawScriptId, BalanceExperiments.ThornmawRageEffectId);

        // Read back through the SHIPPED catalogue, not by string matching: the assertion is that the
        // engine sees a different script, which is what the experiment depends on.
        var entry = BossCatalogue.Read(Override(edited)).Of(BalanceExperiments.ThornmawScriptId);

        entry.Effects.Keys.ShouldNotContain(BalanceExperiments.ThornmawRageEffectId);
        entry.Script.Phases
            .SelectMany(p => p.Mechanics)
            .ShouldNotContain(m => m.EffectId == BalanceExperiments.ThornmawRageEffectId);

        // The negative control — the shipped document still carries both halves, so the case above is
        // about the override and not about the effect never having been there.
        var shipped = BossCatalogue.Read(ShippedHarness.Content)
            .Of(BalanceExperiments.ThornmawScriptId);
        shipped.Effects.Keys.ShouldContain(BalanceExperiments.ThornmawRageEffectId);
        shipped.Script.Phases
            .SelectMany(p => p.Mechanics)
            .ShouldContain(m => m.EffectId == BalanceExperiments.ThornmawRageEffectId);

        // And nothing else about Thornmaw changed: the other effects survive.
        entry.Effects.Count.ShouldBe(shipped.Effects.Count - 1);
    }

    [Fact]
    public void An_override_that_matches_nothing_throws_rather_than_running_the_baseline_twice()
    {
        // 🔴 An A/B whose two arms are the same run reports a difference of exactly zero, which reads
        // like a finding. Three shapes: an unknown effect, an unknown script, and a document with no
        // summoners at all.
        Should.Throw<InvalidOperationException>(() => BossDocumentOverrides.WithoutEffect(
            Shipped, BalanceExperiments.ThornmawScriptId, "BOSS_THORNMAW_P3_NOT_A_REAL_EFFECT"));

        Should.Throw<InvalidOperationException>(() => BossDocumentOverrides.WithoutEffect(
            Shipped, "BOSS_NOT_A_REAL_SCRIPT", BalanceExperiments.ThornmawRageEffectId));

        Should.Throw<InvalidOperationException>(() => BossDocumentOverrides.WithAddsPowerFraction(
            """{ "scripts": [ { "id": "BOSS_X", "coefficients": {} } ] }""", 0.25));
    }

    [Fact]
    public void The_adds_fraction_override_moves_every_summoner_and_no_one_else()
    {
        BossDocumentOverrides.CountSummoners(Shipped).ShouldBe(5);

        var edited = BossDocumentOverrides.WithAddsPowerFraction(Shipped, 0.25);
        var roster = BossRoster.Read(Override(edited));

        roster.Summoners.Count.ShouldBe(5);
        roster.Summoners.ShouldAllBe(b => b.AddsPowerFraction == 0.25);
        roster.All.Count(b => b.AddsPowerFraction is null).ShouldBe(4);

        // Second probe at the other end of `17` §1's band, so a hard-coded 0.25 would not pass.
        var high = BossRoster.Read(Override(BossDocumentOverrides.WithAddsPowerFraction(Shipped, 0.35)));
        high.Summoners.ShouldAllBe(b => b.AddsPowerFraction == 0.35);

        // The shipped tree is untouched — `21` §3.3, an override never edits the canonical files.
        BossRoster.Read(ShippedHarness.Content).Summoners.ShouldAllBe(b => b.AddsPowerFraction == 0.3);
    }

    [Fact]
    public void An_override_changes_the_content_version_so_the_two_arms_are_distinguishable()
    {
        var edited = Override(BossDocumentOverrides.WithAddsPowerFraction(Shipped, 0.25));

        edited.Version.ShouldNotBe(ShippedHarness.Content.Version);
    }

    [Fact]
    public void The_RAGE_removal_actually_changes_the_fight_once_phase_three_is_reached()
    {
        // 🔴 The discriminating control for the experiment itself. `17` §1's phases are HP bands, so
        // at par the hero dies in phase 1 and the A/B is identical for a reason that has nothing to do
        // with the RAGE. Run at a multiple that reaches phase 3, the two arms must diverge — and at
        // par they must NOT, which is the second shape and the one that names the cause.
        var runner = ShippedHarness.Runner;
        var tank = runner.Calibration.Archetype("ARCH_TANK_THORNS");
        var withoutRage = Override(BossDocumentOverrides.WithoutEffect(
            Shipped, BalanceExperiments.ThornmawScriptId, BalanceExperiments.ThornmawRageEffectId));

        var shippedAtPar = runner.RunCell(1, Tier.NORMAL, tank.Id, tank.Stats, 24);
        var editedAtPar = runner.RunCell(1, Tier.NORMAL, tank.Id, tank.Stats, 24, withoutRage);

        shippedAtPar.MaxBossPhaseReached.ShouldBe(1);
        editedAtPar.MaxBossPhaseReached.ShouldBe(1);
        editedAtPar.Fights.Select(f => f.LogHash).ShouldBe(shippedAtPar.Fights.Select(f => f.LogHash));

        var shippedHigh = runner.RunCell(1, Tier.NORMAL, tank.Id, tank.Stats, 24, heroPowerMultiple: 2.6);
        var editedHigh = runner.RunCell(
            1, Tier.NORMAL, tank.Id, tank.Stats, 24, withoutRage, heroPowerMultiple: 2.6);

        shippedHigh.MaxBossPhaseReached.ShouldBe(3);
        editedHigh.MaxBossPhaseReached.ShouldBe(3);
        editedHigh.Fights.Select(f => f.LogHash)
            .ShouldNotBe(shippedHigh.Fights.Select(f => f.LogHash));
    }

    [Fact]
    public void The_boss_phase_reached_is_read_off_the_log_and_is_not_a_constant()
    {
        // The probe for FightOutcome.MaxBossPhase itself: it must be 1 for a hero that dies early and
        // 3 for one that grinds the boss down, or the two experiments' "it never fired" explanation
        // would be unfalsifiable.
        var runner = ShippedHarness.Runner;
        var tank = runner.Calibration.Archetype("ARCH_TANK_THORNS");

        runner.RunCell(1, Tier.NORMAL, tank.Id, tank.Stats, 8).MaxBossPhaseReached.ShouldBe(1);
        runner.RunCell(1, Tier.NORMAL, tank.Id, tank.Stats, 8, heroPowerMultiple: 2.6)
            .MaxBossPhaseReached.ShouldBe(3);
    }

    [Fact]
    public void The_adds_fraction_changes_a_fight_where_the_summon_actually_fires()
    {
        // 🔴 BOSS_OSSUARY_KING's court is ON_PHASE_ENTER phase 1, so it is the one summoner whose adds
        // exist from tick 0 — which makes it the only script where the fraction can be shown to matter
        // without first reaching a later phase.
        var runner = ShippedHarness.Runner;
        var archetype = runner.Calibration.Archetypes[0];

        var low = runner.RunCell(
            3, Tier.NORMAL, archetype.Id, archetype.Stats, 24,
            Override(BossDocumentOverrides.WithAddsPowerFraction(Shipped, 0.25)));
        var high = runner.RunCell(
            3, Tier.NORMAL, archetype.Id, archetype.Stats, 24,
            Override(BossDocumentOverrides.WithAddsPowerFraction(Shipped, 0.35)));

        low.BossId.ShouldBe("BOSS_OSSUARY_KING");
        high.Fights.Select(f => f.LogHash).ShouldNotBe(low.Fights.Select(f => f.LogHash));

        // Stronger adds kill the hero sooner. Directional, because the magnitude is what the
        // experiment reports and is not this case's business.
        Median(high).ShouldBeLessThanOrEqualTo(Median(low));
    }

    private static Core.Content.ContentSnapshot Override(string bossesJson) =>
        GameDataLoader.LoadWith(
            GameDataLoader.DataRoot,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BossDocumentOverrides.DocumentPath] = bossesJson,
            });

    private static double Median(SlayIdleRepeat.BalanceHarness.Sweep.CellResult cell)
    {
        var ticks = cell.Fights.Select(f => (double)f.DurationTicks).ToArray();
        Array.Sort(ticks);

        return SlayIdleRepeat.BalanceHarness.Sweep.CellResult.Percentile(ticks, 0.50);
    }
}
