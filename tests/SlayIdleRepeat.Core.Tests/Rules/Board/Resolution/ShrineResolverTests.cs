using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Handlers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

// Namespace is SlayIdleRepeat.Core.Tests.Rules.Board, not ...Rules.Board.Resolution: a child
// namespace named `Resolution` would shadow SlayIdleRepeat.Core.Rules.Board.Resolution, so a test
// written there could not name the very resolver it is testing.

/// <summary>
/// The shrine's heal and draw accounting through <c>GameRules.Apply</c>. A shrine resolves over TWO
/// commands now — <c>RESOLVE_TILE</c> acknowledges, <c>SHRINE_CHOOSE</c> draws and applies — so the
/// draw accounting is asserted against the second of them. The offer's rows themselves are asserted
/// at the public seam in <see cref="ShrineViewTests"/>.
/// </summary>
public sealed class ShrineResolverTests
{
    private static CommandResult Choose(WorldSlice state, int optionIndex = 0) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new ShrineChooseCommand(optionIndex), TileWorlds.Context);

    /// <summary>
    /// 🔒 <c>RESOLVE_TILE</c> spends NO shrine draw, which is what lets the screen and the choose
    /// command read the same offer off the same committed position.
    /// </summary>
    [Fact]
    public void Acknowledging_a_shrine_spends_no_draw_and_leaves_it_pending()
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            TileWorlds.OnTile(TileKind.Shrine, currentHp: 50),
            new ResolveTileCommand(),
            TileWorlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.StreamPosition(RngStreams.Shrine).ShouldBe(
            0UL, "the offer is drawn by SHRINE_CHOOSE, not by the acknowledgement.");
        result.NewState.Run.HasPendingTile.ShouldBeTrue(
            "the player has not chosen yet, so the tile is not finished.");
    }

    /// <summary>With no cleansable curse, a shrine takes exactly two draws (its two distinct options).</summary>
    /// <remarks>
    /// Paired with the cleanse case below: inverting the cleanse condition makes this one draw and
    /// that one two, so neither can be satisfied by the other's behaviour.
    /// </remarks>
    [Fact]
    public void A_shrine_with_no_cleansable_curse_takes_two_draws()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Shrine, currentHp: 50));

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.StreamPosition(RngStreams.Shrine).ShouldBe(2UL);
    }

    /// <summary>
    /// The cleanse branch takes exactly one draw, because slot 2 is decided rather than drawn — and
    /// it is now reachable from a real command, because the run holds a curse list.
    /// </summary>
    /// <remarks>
    /// Drawing-and-discarding instead of skipping would desynchronise the stream from a client that
    /// also skips it, which is why the count and not just the outcome is asserted.
    /// </remarks>
    [Fact]
    public void A_shrine_with_a_cleansable_curse_takes_one_draw_and_cleanses()
    {
        var cursed = TileWorlds.OnTile(TileKind.Shrine, currentHp: 50, curses: ["CUR_FRACTURED"]);

        var result = Choose(cursed, optionIndex: 1);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Curses.ShouldBeEmpty("slot 2 was the Cleanse.");
        result.NewState.Run.StreamPosition(RngStreams.Shrine).ShouldBe(
            1UL, "the cleanse branch spends no second draw.");
    }

    /// <summary>A shrine offers two options and applies exactly one — it never heals twice.</summary>
    /// <remarks>
    /// Regression test for a real bug: the resolver used to apply the immediate heal of BOTH
    /// drawn rows, so <c>SHR_HEAL</c> (40%) beside <c>SHR_HP</c> (18%) healed 58% of the bar.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_shrine_applies_exactly_one_of_its_two_offers(int optionIndex)
    {
        for (var seed = 1UL; seed <= 200UL; seed++)
        {
            var result = Choose(
                TileWorlds.OnTile(TileKind.Shrine, currentHp: 10, runSeed: seed), optionIndex);

            result.NewState.Run!.CurrentHp.ShouldBeLessThanOrEqualTo(
                50,
                "SHR_HEAL's 40% of a 100 Max HP bar is the largest single option, so a shrine that " +
                "left more than 50 applied more than one of its two offers");
        }
    }

    /// <summary>Exactly one buff is recorded, whichever slot was taken.</summary>
    /// <remarks>
    /// The heal assertion above cannot see this: eight of the ten pool rows heal nothing at all, so
    /// a resolver that recorded both rows' buffs would leave the HP assertion perfectly green while
    /// handing the run a permanent stat move it never chose.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_shrine_records_exactly_one_taken_buff(int optionIndex)
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Shrine, currentHp: 50), optionIndex);

        result.NewState.Run!.ShrineBuffs.Count.ShouldBe(1);
    }

    [Fact]
    public void An_immediate_heal_never_exceeds_max_hp()
    {
        for (var seed = 1UL; seed <= 60UL; seed++)
        {
            var result = Choose(TileWorlds.OnTile(TileKind.Shrine, currentHp: 95, runSeed: seed));

            result.NewState.Run!.CurrentHp.ShouldBeLessThanOrEqualTo(100);
            result.NewState.Run.CurrentHp.ShouldBeGreaterThanOrEqualTo(95, "a shrine never hurts");
        }
    }
}
