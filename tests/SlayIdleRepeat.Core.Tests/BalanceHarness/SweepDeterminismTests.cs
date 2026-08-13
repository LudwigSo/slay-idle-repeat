using Shouldly;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Sweep;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔒 The sweep is reproducible: a <c>(cell, fightIndex)</c> names one fight, whatever the degree of
/// parallelism, whatever the fight count, and whatever order the cells ran in.
/// </summary>
public sealed class SweepDeterminismTests
{
    [Fact]
    public void The_cell_seed_is_the_games_own_Hash64_over_the_cell()
    {
        // Pinned against a restatement of the derivation, not against a magic number: the point is
        // that the harness uses SlayIdleRepeat.Core.Rng and not a private mixer, so the expected value
        // is written the way the documents write it.
        SweepSeeds.CellSeed(3, Tier.HEROIC, "ARCH_DOT")
            .ShouldBe(Hash64.Of(SweepSeeds.CellStream, 3, Tier.HEROIC, "ARCH_DOT"));

        SweepSeeds.FightSeed(3, Tier.HEROIC, "ARCH_DOT", 7)
            .ShouldBe(SeedDerivation.BattleSeed(SweepSeeds.CellSeed(3, Tier.HEROIC, "ARCH_DOT"), 7));
    }

    [Fact]
    public void Every_part_of_the_cell_key_changes_the_seed()
    {
        // Four probes, because a derivation that ignored one component would still pass a single one.
        var baseline = SweepSeeds.CellSeed(3, Tier.HEROIC, "ARCH_DOT");

        SweepSeeds.CellSeed(4, Tier.HEROIC, "ARCH_DOT").ShouldNotBe(baseline);
        SweepSeeds.CellSeed(3, Tier.MYTHIC, "ARCH_DOT").ShouldNotBe(baseline);
        SweepSeeds.CellSeed(3, Tier.HEROIC, "ARCH_CRIT").ShouldNotBe(baseline);
        SweepSeeds.CellSeed(3, Tier.HEROIC, "ARCH_DOT").ShouldBe(baseline);
    }

    [Fact]
    public void Fight_seeds_are_distinct_within_a_cell_and_independent_of_how_many_fights_are_run()
    {
        var seed = SweepSeeds.CellSeed(1, Tier.NORMAL, "ARCH_CRIT");
        var seeds = Enumerable.Range(0, 500).Select(i => SweepSeeds.FightSeed(seed, i)).ToArray();

        seeds.Length.ShouldBe(500);
        seeds.Distinct().Count().ShouldBe(500);

        // 🔒 Fight k has the same seed in a 200-fight run as in a 10 000-fight run, which is what lets
        // the PR-tier subset be compared with the nightly sweep at all.
        SweepSeeds.FightSeed(seed, 199).ShouldBe(seeds[199]);
    }

    [Fact]
    public void The_same_cell_gives_the_same_LogHash_at_one_thread_and_at_many()
    {
        // 🔒 The claim that makes Parallel.For legitimate. Two runs of the same scope at very
        // different degrees of parallelism must agree fight for fight — not just on the clear rate,
        // which would pass for a simulator that had reordered every fight.
        var scope = new SweepScope([1, 2], [Tier.NORMAL], ["ARCH_CRIT", "ARCH_TANK_THORNS"], 12, 1);

        var sequential = ShippedHarness.Runner.Run(scope);
        var parallel = ShippedHarness.Runner.Run(scope with { MaxDegreeOfParallelism = 8 });

        sequential.Cells.Count.ShouldBe(4);
        parallel.Cells.Count.ShouldBe(4);

        for (var i = 0; i < sequential.Cells.Count; i++)
        {
            var a = sequential.Cells[i];
            var b = parallel.Cells[i];

            // The cells come back in scope order regardless of completion order.
            b.Key.ShouldBe(a.Key);
            b.Fights.Count.ShouldBe(12);

            for (var fight = 0; fight < a.Fights.Count; fight++)
            {
                b.Fights[fight].LogHash.ShouldBe(a.Fights[fight].LogHash);
                b.Fights[fight].DurationTicks.ShouldBe(a.Fights[fight].DurationTicks);
                b.Fights[fight].HeroWon.ShouldBe(a.Fights[fight].HeroWon);
            }
        }
    }

    [Fact]
    public void Different_fights_of_one_cell_are_actually_different_fights()
    {
        // The negative control for the case above: identical LogHashes across parallelism would also
        // be satisfied by a simulator that returned one constant fight. It does not.
        var cell = ShippedHarness.Runner.RunCell(
            1, Tier.NORMAL, "ARCH_TANK_THORNS",
            ShippedHarness.Runner.Calibration.Archetype("ARCH_TANK_THORNS").Stats,
            fights: 40,
            heroPowerMultiple: 2.6);

        cell.Fights.Count.ShouldBe(40);
        cell.Fights.Select(f => f.LogHash).Distinct().Count().ShouldBeGreaterThan(1);
        cell.Fights.Select(f => f.DurationTicks).Distinct().Count().ShouldBeGreaterThan(1);
    }

    [Fact]
    public void A_cell_runs_exactly_the_number_of_fights_it_was_asked_for()
    {
        // Steering S3 — a sweep that silently ran fewer fights must not pass. Two counts, because a
        // hard-coded one would satisfy a single probe.
        foreach (var fights in new[] { 3, 17 })
        {
            var cell = ShippedHarness.Runner.RunCell(
                2, Tier.HEROIC, "ARCH_PET",
                ShippedHarness.Runner.Calibration.Archetype("ARCH_PET").Stats,
                fights);

            cell.FightCount.ShouldBe(fights);
            cell.Fights.Count.ShouldBe(fights);
        }

        Should.Throw<ArgumentOutOfRangeException>(() => ShippedHarness.Runner.RunCell(
            2, Tier.HEROIC, "ARCH_PET",
            ShippedHarness.Runner.Calibration.Archetype("ARCH_PET").Stats,
            fights: 0));
    }

    [Fact]
    public void The_full_scope_is_one_hundred_and_twenty_cells_and_includes_the_Sporequeen()
    {
        // 🔒 Steering S3's subject-set floor for the sweep itself: 8 chapters × 3 tiers × 5 archetypes.
        // Asserted over the SCOPE the CLI builds from the authored catalogues, so a data change that
        // dropped a chapter fails here rather than quietly shrinking the nightly job.
        var runner = ShippedHarness.Runner;
        var scope = new SweepScope(
            runner.ParPower.Chapters,
            Tiers.All,
            runner.Calibration.Archetypes.Select(a => a.Id).ToArray(),
            SweepScope.DocumentedFightsPerCell,
            1);

        scope.Chapters.Count.ShouldBe(8);
        scope.Tiers.Count.ShouldBe(3);
        scope.ArchetypeIds.Count.ShouldBe(5);
        scope.CellCount.ShouldBe(120);
        scope.Fights.ShouldBe(10_000);

        // The bosses those cells actually fight — BOSS_SPOREQUEEN_VELL among them, BOSS_FTUE not.
        var bosses = scope.Chapters.Select(c => runner.Bosses.ForChapter(c).Id).ToArray();
        bosses.Length.ShouldBe(8);
        bosses.ShouldContain("BOSS_SPOREQUEEN_VELL");
        bosses.ShouldNotContain("BOSS_FTUE");
    }
}
