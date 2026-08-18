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

    // ------------------------------------------------------------------ the gate

    /// <summary>A run standing on no tile has no campfire to rest at.</summary>
    [Fact]
    public void A_run_with_no_pending_tile_is_rejected()
    {
        var result = Choose(TileWorlds.OnNoTile(), 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A pending tile of another kind is not a campfire either.</summary>
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

    // ------------------------------------------------------------------ the rest

    /// <summary>Resting heals 40% of Max HP and clears the tile.</summary>
    [Fact]
    public void Resting_heals_forty_percent_of_max_hp_and_clears()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 50), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(90);
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }

    /// <summary>…and the heal is clamped at Max HP rather than overhealing.</summary>
    [Fact]
    public void Resting_is_clamped_at_max_hp()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 80), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(100);
    }

    /// <summary>A rest at full health is legal and changes nothing.</summary>
    [Fact]
    public void Resting_at_full_health_is_accepted_and_changes_nothing()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 100), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(100);
    }

    /// <summary>A rest at zero HP still heals — zero is a legal state, not a dead run.</summary>
    [Fact]
    public void Resting_at_zero_hit_points_still_heals()
    {
        Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 0), 0)
            .NewState.Run!.CurrentHp.ShouldBe(40);
    }

    /// <summary>A rest moves no currency and produces no event.</summary>
    [Fact]
    public void Resting_produces_no_events()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, gold: 250, currentHp: 50), 0);

        result.Events.ShouldBeEmpty();
        result.NewState.Run!.Gold.ShouldBe(250);
    }

    /// <summary>The campfire draws nothing, so no RNG stream counter moves.</summary>
    [Fact]
    public void Resting_consumes_no_rng_draw()
    {
        Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 50), 0)
            .NewState.Run!.RngStreamPositions.ShouldBeEmpty();
    }

    /// <summary>…and resting twice is refused, because the first rest cleared the tile.</summary>
    [Fact]
    public void Resting_twice_is_rejected()
    {
        var rested = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 20), 0).NewState;

        Choose(rested, 0).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------ the two unbuilt options

    /// <summary>
    /// The perk-tier upgrade is refused, not silently accepted: Run holds no drafted perks yet, so
    /// there is nothing to upgrade, and an accept would tell the player they had spent their rest on it.
    /// </summary>
    [Fact]
    public void The_perk_upgrade_option_is_rejected_until_M3_06()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 50), 1);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>…and so is the reroll-charge option: no reroll-charge count is tracked anywhere yet.</summary>
    [Fact]
    public void The_reroll_charge_option_is_rejected()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Campfire, currentHp: 50), 2);

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

    /// <summary>
    /// 🔒 The two refusals CAMPFIRE_CHOOSE can produce are told apart by the STATE each is refused
    /// from, not by the code they share — both are ILLEGAL_STATE, and a screen reading the code
    /// alone would tell the player one thing for two different situations (steering S2).
    /// </summary>
    /// <remarks>
    /// The discriminator is what is still legal AFTER the refusal. At a campfire, choice 1 is
    /// refused because the option is not built and the rest is still there to take; at a shrine,
    /// choice 1 is refused because there is no campfire at all, and the rest is refused with it. A
    /// case asserting only "refused, ILLEGAL_STATE" in both places passes against a handler that has
    /// collapsed the two rules into one.
    /// </remarks>
    [Fact]
    public void The_tile_refusal_and_the_unbuilt_option_refusal_are_different_rules()
    {
        var atACampfire = TileWorlds.OnTile(TileKind.Campfire, currentHp: 50);
        var atAShrine = TileWorlds.OnTile(TileKind.Shrine, currentHp: 50);

        Choose(atACampfire, 1).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        Choose(atACampfire, 0).Accepted.ShouldBeTrue(
            "the rest was refused at a campfire whose only refusal was of an unbuilt option, so the " +
            "option rule and the tile rule are the same rule.");

        Choose(atAShrine, 1).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        Choose(atAShrine, 0).Accepted.ShouldBeFalse(
            "the rest was accepted at a SHRINE, so the refusal of choice 1 there came from the " +
            "unbuilt-option rule rather than from there being no campfire.");
        Choose(atAShrine, 0).Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>An index naming no option at all is refused too.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(1000)]
    public void An_out_of_range_choice_index_is_rejected(int choiceIndex)
    {
        Choose(TileWorlds.OnTile(TileKind.Campfire), choiceIndex)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------ the round trip

    /// <summary>
    /// The full loop: arrive at a campfire, RESOLVE_TILE acknowledges and leaves it pending,
    /// CAMPFIRE_CHOOSE rests and clears it.
    /// </summary>
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
