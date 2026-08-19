using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;
using SlayIdleRepeat.Core.Tests.Handlers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

// Namespace is SlayIdleRepeat.Core.Tests.Rules.Board, not ...Rules.Board.Resolution: a child
// namespace named `Resolution` would shadow SlayIdleRepeat.Core.Rules.Board.Resolution, so a test
// written there could not name the very resolver it is testing.

/// <summary>
/// The shrine's heal and draw accounting through <c>GameRules.Apply</c> on <c>RESOLVE_TILE</c>.
/// The offer's rows themselves are asserted at the public seam in <see cref="ShrineViewTests"/>.
/// </summary>
public sealed class ShrineResolverTests
{
    private static CommandResult Resolve(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new ResolveTileCommand(), TileWorlds.Context);

    /// <summary>With no cleansable curse, a shrine takes exactly two draws (its two distinct options).</summary>
    /// <remarks>
    /// Paired with the cleanse case below: inverting the cleanse condition makes this one draw and
    /// that one two, so neither can be satisfied by the other's behaviour.
    /// </remarks>
    [Fact]
    public void A_shrine_with_no_cleansable_curse_takes_two_draws()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Shrine, currentHp: 50));

        result.NewState.Run!.StreamPosition(RngStreams.Shrine).ShouldBe(2UL);
    }

    /// <summary>The cleanse branch takes exactly one draw, because slot 2 is decided rather than drawn.</summary>
    /// <remarks>
    /// Internal seam by necessity: <c>Run</c> holds no curse list yet, so the handler always passes
    /// <c>hasCleansableCurse: false</c> and no command can reach this branch. Drawing-and-discarding
    /// instead of skipping would desync the RNG stream from a client that also skips it.
    /// </remarks>
    [Fact]
    public void A_shrine_with_a_cleansable_curse_takes_one_draw_and_offers_a_cleanse()
    {
        var scope = new RunRngScope(TileWorlds.Seed, new Dictionary<string, ulong>(StringComparer.Ordinal));
        var input = new HandlerInput(
            TileWorlds.OnTile(TileKind.Shrine, currentHp: 50), TileWorlds.Context, scope);

        var offer = ShrineResolver.Resolve(input, hasCleansableCurse: true);

        offer.IsCleanse.ShouldBeTrue();
        offer.SecondBuffId.ShouldBeNull("slot 2 is a Cleanse, so no second buff was drawn");
        scope.FinalPositions()[RngStreams.Shrine].ShouldBe(1UL);
    }

    /// <summary>A shrine offers two options and applies exactly one — it never heals twice.</summary>
    /// <remarks>
    /// Regression test for a real bug: the resolver used to apply the immediate heal of BOTH
    /// drawn rows, so <c>SHR_HEAL</c> (40%) beside <c>SHR_HP</c> (18%) healed 58% of the bar.
    /// </remarks>
    [Fact]
    public void A_shrine_applies_exactly_one_of_its_two_offers()
    {
        for (var seed = 1UL; seed <= 200UL; seed++)
        {
            var result = Resolve(TileWorlds.OnTile(TileKind.Shrine, currentHp: 10, runSeed: seed));

            result.NewState.Run!.CurrentHp.ShouldBeLessThanOrEqualTo(
                50,
                "SHR_HEAL's 40% of a 100 Max HP bar is the largest single option, so a shrine that " +
                "left more than 50 applied more than one of its two offers");
        }
    }

    [Fact]
    public void An_immediate_heal_never_exceeds_max_hp()
    {
        for (var seed = 1UL; seed <= 60UL; seed++)
        {
            var result = Resolve(TileWorlds.OnTile(TileKind.Shrine, currentHp: 95, runSeed: seed));

            result.NewState.Run!.CurrentHp.ShouldBeLessThanOrEqualTo(100);
            result.NewState.Run!.CurrentHp.ShouldBeGreaterThanOrEqualTo(95, "a shrine never hurts");
        }
    }
}
