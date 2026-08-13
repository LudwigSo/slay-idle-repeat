using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §4.1 — <see cref="WorldSlice"/>: the aggregates one command may touch, and the two `30`
/// §4.1 names it deliberately does not carry in M1.
/// </summary>
public sealed class WorldSliceTests
{
    /// <summary>A slice carries the aggregates it was handed.</summary>
    [Fact]
    public void A_slice_carries_the_aggregates_it_was_handed()
    {
        var player = Worlds.NewPlayer();
        var run = Worlds.NewRun();

        var slice = new WorldSlice(player, run);

        slice.Player.ShouldBeSameAs(player);
        slice.Run.ShouldBeSameAs(run);
    }

    /// <summary>
    /// The run is <c>null</c> outside a run, which is `30` §4.1's own annotation and the normal
    /// state for 30 of the 49 commands.
    /// </summary>
    [Fact]
    public void The_run_is_null_outside_a_run()
    {
        new WorldSlice(Worlds.NewPlayer(), null).Run.ShouldBeNull();
    }

    /// <summary>
    /// 🔒 A slice always names a player: `30` §4 models <c>Run</c> as a <b>child</b> of
    /// <c>Player</c>, so there is no command that touches a run and no player.
    /// </summary>
    [Fact]
    public void A_slice_without_a_player_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new WorldSlice(null!, null))
            .ParamName.ShouldBe("Player");
    }

    /// <summary>
    /// 🔒 The guard runs on the <c>with</c> path too — it lives in the <c>init</c> accessor, not in
    /// a property initialiser.
    /// </summary>
    /// <remarks>
    /// An initialiser runs only in the primary constructor; the synthesized copy constructor copies
    /// backing fields and then calls the plain <c>init</c> setters, so a guard written as an
    /// initialiser would let <c>slice with { Player = null! }</c> produce a slice with a hole. The
    /// same measurement <c>GameContext</c> recorded.
    /// </remarks>
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
    /// ⚠️ The slice holds <b>references</b>: constructing one does not copy the aggregates.
    /// <c>GameRules.Apply</c> is what clones, on the way in, so `30` §2.1's P4 holds for the
    /// caller's slice.
    /// </summary>
    /// <remarks>
    /// Stated as a test rather than only as a comment because it is the assumption every P4
    /// assertion in <c>GameRulesStateTests</c> rests on: if the slice copied, those tests would pass
    /// for a reason that had nothing to do with <c>Apply</c>.
    /// </remarks>
    [Fact]
    public void A_slice_holds_references_and_does_not_copy()
    {
        var run = Worlds.NewRun(RunSnapshots.With(gold: 5L));
        var slice = new WorldSlice(Worlds.NewPlayer(), run);

        run.MoveCurrency(Core.Primitives.CurrencyId.GOLD, 3L, "fixture_grant");

        slice.Run!.Gold.ShouldBe(8L);
    }

    /// <summary>
    /// 🔒 Milestone assumption <b>A4</b> — the M1 slice is <c>(Player, Run?)</c> and nothing else.
    /// `30` §4.1's <c>GuildView?</c> and <c>GhostSnapshot?</c> are M14's and M12's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pin is by <b>identity</b>, not by count: a slice that grew a third member of some other
    /// name would satisfy a count of two after one of these was renamed away.
    /// </para>
    /// <para>
    /// ⚠️ <b>When a third member lands, this test is the reminder — not the obstacle.</b> A
    /// <c>WorldSlice</c> is not a persisted snapshot, so adding a nullable member costs no
    /// <c>SchemaVersion</c> bump; the two absent names are registered in
    /// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>, which is what fails the build on the
    /// day each becomes writable.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_M1_slice_is_the_player_and_the_run_and_nothing_else()
    {
        typeof(WorldSlice)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ShouldBe(new[] { nameof(WorldSlice.Player), nameof(WorldSlice.Run) }, ignoreOrder: true);
    }

    /// <summary>
    /// 🔒 `30` §4's <b>child-not-peer</b> modelling: a slice may not pair one player with another
    /// player's run.
    /// </summary>
    /// <remarks>
    /// ⚠️ Until the M1 review this was described in <c>WorldSlice</c>'s doc comment and enforced
    /// nowhere, so <c>Apply(new WorldSlice(playerA, runOfPlayerB), …)</c> was a legal public call
    /// that moved Gold, wrote HP, folded RNG positions and stamped a run belonging to somebody else.
    /// No architecture rule of any shape could have caught it —
    /// <c>Apply_is_the_only_public_mutation</c> quantifies over <c>Core/Model/</c> and
    /// <c>WorldSlice</c> is deliberately in the root — which is why it is a domain invariant here
    /// rather than a rule over there.
    /// </remarks>
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

    /// <summary>…and the guard runs on the <c>with</c> path, which is where a slice is most easily
    /// given a foreign run.</summary>
    /// <remarks>
    /// The property initialiser runs only in the primary constructor; the synthesized copy
    /// constructor copies backing fields and then calls the plain <c>init</c> setters. This is the
    /// same hazard M1-07 shipped and review caught on <c>GameContext</c> — three null guards
    /// bypassed by one <c>with</c> expression.
    /// </remarks>
    [Fact]
    public void The_ownership_guard_survives_a_with_expression()
    {
        var slice = Worlds.OutsideARun();
        var somebodyElsesRun = Worlds.NewRun(RunSnapshots.With(playerId: new PlayerId("PLAYER_SOMEBODY_ELSE")));

        Should.Throw<ArgumentException>(() => slice with { Run = somebodyElsesRun });
    }
}
