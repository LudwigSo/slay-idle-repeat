using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Experiments;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// The two harness experiments: the overrides really change the fight, they never touch
/// <c>game-data/</c>, and an override that matched nothing throws instead of quietly running the
/// baseline twice.
/// </summary>
[Collection(WallClockSensitive.Name)]
public sealed class ExperimentOverrideTests
{
    private static string Shipped => BossDocumentOverrides.ReadShipped(GameDataLoader.DataRoot);

    [Fact]
    public void Removing_the_Thornmaw_RAGE_removes_both_the_effect_and_the_phase_mechanic()
    {
        var edited = BossDocumentOverrides.WithoutEffect(
            Shipped, BalanceExperiments.ThornmawScriptId, BalanceExperiments.ThornmawRageEffectId);

        // Read back through the catalogue, not by string matching: asserts the engine sees a
        // different script.
        var entry = BossCatalogue.Read(Override(edited)).Of(BalanceExperiments.ThornmawScriptId);

        entry.Effects.Keys.ShouldNotContain(BalanceExperiments.ThornmawRageEffectId);
        entry.Script.Phases
            .SelectMany(p => p.Mechanics)
            .ShouldNotContain(m => m.EffectId == BalanceExperiments.ThornmawRageEffectId);

        // Negative control — the shipped document still carries both halves.
        var shipped = BossCatalogue.Read(ShippedHarness.Content)
            .Of(BalanceExperiments.ThornmawScriptId);
        shipped.Effects.Keys.ShouldContain(BalanceExperiments.ThornmawRageEffectId);
        shipped.Script.Phases
            .SelectMany(p => p.Mechanics)
            .ShouldContain(m => m.EffectId == BalanceExperiments.ThornmawRageEffectId);

        entry.Effects.Count.ShouldBe(shipped.Effects.Count - 1);
    }

    [Fact]
    public void An_override_that_matches_nothing_throws_rather_than_running_the_baseline_twice()
    {
        // An A/B whose two arms are the same run reports a difference of exactly zero, which reads
        // like a finding — so a matchless override must throw instead.
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

        // A second probe with its own count floor: ShouldAllBe passes on an empty collection, so an
        // override that dropped every summoner would satisfy "every summoner carries 0.35" vacuously.
        var high = BossRoster.Read(Override(BossDocumentOverrides.WithAddsPowerFraction(Shipped, 0.35)));
        high.Summoners.Count.ShouldBe(5);
        high.Summoners.ShouldAllBe(b => b.AddsPowerFraction == 0.35);

        // The shipped tree is untouched — an override never edits the canonical files.
        var untouched = BossRoster.Read(ShippedHarness.Content);
        untouched.Summoners.Count.ShouldBe(5);
        untouched.Summoners.ShouldAllBe(b => b.AddsPowerFraction == 0.3);
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
        // At par the hero dies in phase 1, so the A/B must be identical for a reason unrelated to
        // RAGE; at a multiple that reaches phase 3, the two arms must diverge.
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
        var runner = ShippedHarness.Runner;
        var tank = runner.Calibration.Archetype("ARCH_TANK_THORNS");

        runner.RunCell(1, Tier.NORMAL, tank.Id, tank.Stats, 8).MaxBossPhaseReached.ShouldBe(1);
        runner.RunCell(1, Tier.NORMAL, tank.Id, tank.Stats, 8, heroPowerMultiple: 2.6)
            .MaxBossPhaseReached.ShouldBe(3);
    }

    [Fact]
    public void The_adds_fraction_changes_a_fight_where_the_summon_actually_fires()
    {
        // BOSS_OSSUARY_KING's court fires on phase 1 entry, so its adds exist from tick 0 — the only
        // script where the fraction matters without first reaching a later phase.
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

        // Directional only — magnitude is what the experiment reports, not this case.
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
