using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary><see cref="WorldSlice"/>: the aggregates one command may touch.</summary>
public sealed class WorldSliceTests
{
    /// <summary>A slice always names a player: <c>Run</c> is modelled as a child of <c>Player</c>, never a peer.</summary>
    [Fact]
    public void A_slice_without_a_player_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new WorldSlice(null!, null))
            .ParamName.ShouldBe("Player");
    }

    /// <summary>
    /// The guard runs on the <c>with</c> path too — it lives in the <c>init</c> accessor, since a
    /// property initialiser runs only in the primary constructor and would leave the copy path unguarded.
    /// </summary>
    [Fact]
    public void The_player_guard_survives_a_with_expression()
    {
        var slice = new WorldSlice(Worlds.NewPlayer(), null);

        Should.Throw<ArgumentNullException>(() => slice with { Player = null! })
            .ParamName.ShouldBe("Player");
    }

    /// <summary>A <c>with</c> that swaps the run in or out is the ordinary, legal use.</summary>
    [Fact]
    public void A_with_expression_can_add_or_remove_the_run()
    {
        var outside = new WorldSlice(Worlds.NewPlayer(), null);
        var run = Worlds.NewRun();

        (outside with { Run = run }).Run.ShouldBeSameAs(run);
        (outside with { Run = run } with { Run = null }).Run.ShouldBeNull();
    }

    /// <summary>
    /// The slice holds references: constructing one does not copy the aggregates.
    /// <c>GameRules.Apply</c> is what clones, on the way in — the assumption every immutability
    /// assertion in <c>GameRulesStateTests</c> rests on.
    /// </summary>
    [Fact]
    public void A_slice_holds_references_and_does_not_copy()
    {
        var run = Worlds.NewRun(RunSnapshots.With(gold: 5L));
        var slice = new WorldSlice(Worlds.NewPlayer(), run);

        run.MoveCurrency(Core.Primitives.CurrencyId.GOLD, 3L, "fixture_grant");

        slice.Run!.Gold.ShouldBe(8L);
    }

    /// <summary>A slice may not pair one player with another player's run.</summary>
    [Fact]
    public void A_slice_refuses_a_run_belonging_to_another_player()
    {
        var somebodyElsesRun = Worlds.NewRun(RunSnapshots.With(playerId: new PlayerId("PLAYER_SOMEBODY_ELSE")));

        var thrown = Should.Throw<ArgumentException>(
            () => new WorldSlice(Worlds.NewPlayer(), somebodyElsesRun));

        thrown.ParamName.ShouldBe(nameof(WorldSlice.Run));
        thrown.Message.ShouldContain(
            "PLAYER_SOMEBODY_ELSE",
            Case.Sensitive,
            "the message names WHOSE run it is — a caller debugging a mis-loaded slice needs the id, " +
            "not just the fact that two ids differed.");
    }

    /// <summary>...and the ownership guard runs on the <c>with</c> path too, not only the constructor.</summary>
    [Fact]
    public void The_ownership_guard_survives_a_with_expression()
    {
        var slice = Worlds.OutsideARun();
        var somebodyElsesRun = Worlds.NewRun(RunSnapshots.With(playerId: new PlayerId("PLAYER_SOMEBODY_ELSE")));

        Should.Throw<ArgumentException>(() => slice with { Run = somebodyElsesRun });
    }
}
