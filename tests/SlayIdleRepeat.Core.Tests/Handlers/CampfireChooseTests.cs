using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>CAMPFIRE_CHOOSE, driven through the production dispatch table by GameRules.Apply.</summary>
public sealed class CampfireChooseTests
{
    private static CommandResult Choose(WorldSlice state, int choiceIndex) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new CampfireChooseCommand(choiceIndex), TileWorlds.Context);

    [Fact]
    public void A_run_with_no_pending_tile_is_rejected()
    {
        var result = Choose(TileWorlds.OnNoTile(), 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Theory]
    [InlineData((int)TileKind.Event)]
    [InlineData((int)TileKind.Shrine)]
    [InlineData((int)TileKind.Empty)]
    public void A_pending_tile_of_another_kind_is_rejected(int kind)
    {
        var result = Choose(TileWorlds.OnTile((TileKind)kind), 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void Resting_heals_forty_percent_of_max_hp_and_clears()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 50), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(90);
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }

    [Fact]
    public void Resting_is_clamped_at_max_hp()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 80), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(100);
    }

    [Fact]
    public void Resting_at_full_health_is_accepted_and_changes_nothing()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 100), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(100);
    }

    /// <summary>Zero HP is a legal state, not a dead run.</summary>
    [Fact]
    public void Resting_at_zero_hit_points_still_heals()
    {
        Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 0), 0)
            .NewState.Run!.CurrentHp.ShouldBe(40);
    }

    [Fact]
    public void Resting_produces_no_events()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, gold: 250, currentHp: 50), 0);

        result.Events.ShouldBeEmpty();
        result.NewState.Run!.Gold.ShouldBe(250);
    }

    [Fact]
    public void Resting_consumes_no_rng_draw()
    {
        Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 50), 0)
            .NewState.Run!.RngStreamPositions.ShouldBeEmpty();
    }

    [Fact]
    public void Resting_twice_is_rejected()
    {
        var rested = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 20), 0).NewState;

        Choose(rested, 0).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// The unbuilt options (perk upgrade, reroll charge) are refused, not silently accepted: an
    /// accept would tell the player they had spent their rest on nothing.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void An_unbuilt_option_is_rejected(int choiceIndex)
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 50), choiceIndex);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>…and neither refusal consumes the campfire, so the player may still take the rest.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_refused_option_leaves_the_campfire_available(int choiceIndex)
    {
        var state = TileWorlds.OnTile(TileKind.Campfire, currentHp: 50);

        var refused = Choose(state, choiceIndex);

        refused.NewState.ShouldBeSameAs(state, "a rejection returns the caller's slice");

        Choose(refused.NewState, 0).NewState.Run!.CurrentHp.ShouldBe(90);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(1000)]
    public void An_out_of_range_choice_index_is_rejected(int choiceIndex)
    {
        Choose(TileWorlds.OnTile(TileKind.Campfire), choiceIndex)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void A_campfire_resolves_across_two_commands()
    {
        var arrived = TileWorlds.OnTile(TileKind.Campfire, currentHp: 30);

        var acknowledged = SlayIdleRepeat.Core.GameRules.Apply(
            arrived, new ResolveTileCommand(), TileWorlds.Context);

        acknowledged.Accepted.ShouldBeTrue();
        acknowledged.NewState.Run!.ToSnapshot().PendingTileKind
            .ShouldBe((int)TileKind.Campfire, "RESOLVE_TILE must not consume the campfire");

        var rested = Choose(acknowledged.NewState, 0);

        rested.Accepted.ShouldBeTrue();
        rested.NewState.Run!.CurrentHp.ShouldBe(70);
        rested.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }
}
