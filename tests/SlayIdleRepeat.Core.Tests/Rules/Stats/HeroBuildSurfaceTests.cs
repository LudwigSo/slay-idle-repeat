using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using SlayIdleRepeat.Core.Tests.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// The view the hero build hands out, and its agreement with the aggregation behind it — each
/// published fact is a second spelling of a number the aggregation already produced, which is the
/// kind of surface that drifts.
/// </summary>
public sealed class HeroBuildSurfaceTests
{
    /// <summary>The published block agrees with the aggregation behind it.</summary>
    [Fact]
    public void The_published_stat_block_is_the_aggregations_own()
    {
        var build = Geared();

        build.Stats.ShouldBe(
            build.Aggregated.Final,
            "the hero screen's numbers have to be the numbers the fight is run with");
    }

    /// <summary>The published Max HP is the aggregation's post-multiplier figure — the fight's
    /// health pool, not the capped stat.</summary>
    [Fact]
    public void The_published_max_hp_is_the_post_multiplier_figure()
    {
        var build = Geared();

        build.MaxHp.ShouldBe(build.Aggregated.PostMultiplierMaxHp);

        // No authored gear writes a STAT_SET to MAX_HP, so for every build this seam can produce the
        // two readings coincide; the case below is where the separation is pinned.
        build.MaxHp.ShouldBe(
            build.Stats[Core.Content.Effects.StatId.MAX_HP],
            "no gear-only build diverges the two readings; if this ever fails, the case below is the " +
            "one that describes what the divergence means");
    }

    /// <summary>
    /// 🔒 The post-multiplier reading and the capped stat are genuinely different numbers the moment
    /// a <c>STAT_SET</c> lands on Max HP — the discriminator the build's own gear fixtures cannot
    /// supply, since no authored gear writes a <c>STAT_SET</c> to it.
    /// </summary>
    [Fact]
    public void The_post_multiplier_reading_is_not_the_capped_stat_once_a_stat_set_lands()
    {
        var baseStats = CombatCaps.Read(RunBattleWorlds.Content).HeroBase.At(RunBattleWorlds.LegendLevel);

        var aggregated = StatAggregation.Aggregate(
            baseStats,
            [
                new EffectDefinition
                {
                    Id = "TEST_MAX_HP_SET",
                    Op = EffectOp.STAT_SET,
                    Stat = StatSelector.Of(Core.Content.Effects.StatId.MAX_HP),
                    Trigger = EffectDefaults.Always,
                    Value = 1.0,
                },
            ],
            CombatCaps.Read(RunBattleWorlds.Content).Caps,
            StatAggregationSeams.Strict);

        aggregated.PostMultiplierMaxHp.ShouldBe(
            baseStats[Core.Content.Effects.StatId.MAX_HP],
            "the reading is frozen before step 8, so a STAT_SET cannot reach it");

        aggregated.Final[Core.Content.Effects.StatId.MAX_HP].ShouldBe(
            1.0, "while the published stat is the set value");
    }

    /// <summary>
    /// A gold-gain affix is NAMED in the published unapplied list, not silently dropped: the ring's
    /// <c>GOLD_PCT</c> has no slot in the fourteen-stat block, and a caller must be able to tell a
    /// valued-but-unapplied affix from one the pipeline lost.
    /// </summary>
    [Fact]
    public void An_affix_outside_the_fourteen_combat_stats_is_named_rather_than_lost()
    {
        var worn = RunBattleWorlds.Worn
            .Where(item => item.Slot != GearSlot.RING)
            .Append(Inventories.Item(
                "worn_gold_ring",
                GearFamily.BAND,
                Rarity.SS,
                enhanceLevel: 5,
                affixes: [new GearAffixRoll("AFX_GOLD_GAIN", 0.2)]))
            .ToArray();

        var build = HeroBuild.Of(RunBattleWorlds.LegendLevel, worn, RunBattleWorlds.Content);

        build.UnappliedEffects.ShouldContain(
            id => id.Contains("AFX_GOLD_GAIN", StringComparison.Ordinal),
            "a valued-but-unapplied affix has to be reportable by id, or a caller cannot tell it from " +
            "one the pipeline silently lost");

        Geared().UnappliedEffects.ShouldBeEmpty(
            "the control: the fixture set rolls nothing outside the fourteen, so the assertion above " +
            "is about the gold ring and not about every build reporting something");
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

    /// <summary>
    /// 🔒 Inside a run the door reads the loadout the RUN froze. Stated over a player and a run that
    /// disagree, in both crossings — every other fixture dresses the two identically, so a door
    /// reading <c>player.Loadout</c> in both branches satisfies all of them.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Inside_a_run_the_door_reads_the_loadout_the_run_froze(bool playerGeared, bool runGeared)
    {
        var build = HeroBuild.Of(
            RunBattleWorlds.PlayerRow(playerGeared),
            RunBattleWorlds.RunRow(geared: runGeared),
            RunBattleWorlds.Content);

        build.Equipped.Count.ShouldBe(
            runGeared ? RunBattleWorlds.Worn.Count : 0,
            "a run fights the loadout it was started with, whatever the player is wearing now");
    }

    /// <summary>Outside a run, the same door reads the player's current loadout instead.</summary>
    /// <remarks>
    /// The other half of the pair above: with no run to freeze one, the player's own loadout is the
    /// only answer — and asked for in both dressings, so it cannot be satisfied by a constant.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_snapshot_door_outside_a_run_reads_the_players_own_loadout(bool geared)
    {
        var build = HeroBuild.Of(RunBattleWorlds.PlayerRow(geared), run: null, RunBattleWorlds.Content);

        build.Equipped.Count.ShouldBe(geared ? RunBattleWorlds.Worn.Count : 0);
    }

    /// <summary>A player row that does not rehydrate is refused at the door.</summary>
    /// <remarks>
    /// The parameter name is asserted too: the run door throws the same exception type, so a bare
    /// type check would be satisfied by a fault about the other argument.
    /// </remarks>
    [Fact]
    public void A_player_row_that_does_not_rehydrate_is_refused_at_the_public_door()
    {
        var broken = RunBattleWorlds.PlayerRow() with { LegendLevel = -1 };

        var refused = Should.Throw<ArgumentException>(
            () => HeroBuild.Of(broken, run: null, RunBattleWorlds.Content));

        refused.ParamName.ShouldBe("player");
        refused.Message.ShouldContain("does not rehydrate", Case.Insensitive);
    }

    private static HeroBuild Geared() => HeroBuild.Of(
        RunBattleWorlds.LegendLevel, RunBattleWorlds.Worn, RunBattleWorlds.Content);
}
