using Shouldly;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// The view the hero build hands out, and its agreement with the aggregation behind it.
/// </summary>
/// <remarks>
/// The build is now the entry point an outside assembly reaches the hero through, so it publishes
/// three facts — the capped block, the post-multiplier Max HP and the effects it valued but could
/// not apply — that used to be readable only off the internal aggregate. Every one of them is a
/// second spelling of a number the aggregation already produced, which is precisely the kind of
/// surface that drifts: these cases pin the two spellings together.
/// </remarks>
public sealed class HeroBuildSurfaceTests
{
    /// <summary>The published block is the aggregation's own, not a copy of it.</summary>
    [Fact]
    public void The_published_stat_block_is_the_aggregations_own()
    {
        var build = Geared();

        build.Stats.ShouldBeSameAs(
            build.Aggregated.Final,
            "a copy is a second thing to keep in step with the pipeline");
    }

    /// <summary>The published Max HP is the aggregation's post-multiplier figure.</summary>
    /// <remarks>
    /// Not the capped <c>MAX_HP</c> stat: the two are different numbers whenever a multiplicative
    /// source is in play, and the fight's health pool is the post-multiplier one. Reading the stat
    /// here would publish the smaller of the two under the name of the larger.
    /// </remarks>
    [Fact]
    public void The_published_max_hp_is_the_post_multiplier_figure()
    {
        var build = Geared();

        build.MaxHp.ShouldBe(build.Aggregated.PostMultiplierMaxHp);
    }

    /// <summary>The published unapplied list is the aggregation's skipped list.</summary>
    /// <remarks>
    /// The gold-gain and pet-aura affixes land here: the block holds fourteen combat stats and
    /// nothing else, so an affix naming a stat outside them is named rather than lost. A caller that
    /// could not read this would have no way to tell a valued-but-unapplied affix from one that was
    /// silently dropped.
    /// </remarks>
    [Fact]
    public void The_published_unapplied_list_is_the_aggregations_skipped_list()
    {
        var build = Geared();

        build.UnappliedEffects.ShouldBeSameAs(build.Aggregated.SkippedNonCombatStatEffects);
    }

    /// <summary>The snapshot door builds the same hero as the aggregate door.</summary>
    /// <remarks>
    /// 🔒 The client reaches the build through rows and the domain reaches it through aggregates, and
    /// the hero screen's numbers have to be the numbers the fight is run with. Compared over the
    /// effect ids as well as the block: two builds can agree on every stat while having collected
    /// different effects, and the effect list is what the fight is handed.
    /// </remarks>
    [Fact]
    public void The_snapshot_door_builds_the_same_hero_as_the_aggregate_door()
    {
        var playerRow = RunBattleWorlds.PlayerRow();
        var runRow = RunBattleWorlds.RunRow();

        var fromRows = HeroBuild.Of(playerRow, runRow, RunBattleWorlds.Content);
        var fromAggregates = HeroBuild.Of(
            RunBattleWorlds.Player(playerRow), RunBattleWorlds.Run(runRow), RunBattleWorlds.Content);

        fromRows.Stats.ShouldBe(fromAggregates.Stats);
        fromRows.Effects.Select(e => e.Id).ShouldBe(fromAggregates.Effects.Select(e => e.Id));
        fromRows.Equipped.Select(i => i.InstanceId).ShouldBe(
            fromAggregates.Equipped.Select(i => i.InstanceId));
    }

    /// <summary>Outside a run, the snapshot door reads the player's current loadout.</summary>
    /// <remarks>
    /// The control that keeps the case above from being satisfied by a door that ignores its run
    /// argument: passing no run has to reach a different loadout, and the fixture's player wears the
    /// same set the run froze — so this is stated over the argument that actually differs, the
    /// absence of a run, and asserts the build still resolves the six worn items.
    /// </remarks>
    [Fact]
    public void The_snapshot_door_outside_a_run_reads_the_players_own_loadout()
    {
        var build = HeroBuild.Of(RunBattleWorlds.PlayerRow(), run: null, RunBattleWorlds.Content);

        build.Equipped.Count.ShouldBe(RunBattleWorlds.Worn.Count);
    }

    /// <summary>A player row that does not rehydrate is refused at the door.</summary>
    [Fact]
    public void A_player_row_that_does_not_rehydrate_is_refused_at_the_public_door()
    {
        var broken = RunBattleWorlds.PlayerRow() with { LegendLevel = -1 };

        Should.Throw<ArgumentException>(
            () => HeroBuild.Of(broken, run: null, RunBattleWorlds.Content));
    }

    private static HeroBuild Geared() => HeroBuild.Of(
        RunBattleWorlds.LegendLevel, RunBattleWorlds.Worn, RunBattleWorlds.Content);
}
