using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>Immutable and pure: what <c>Apply</c> does to the slice it is handed, and what it must never do to it.</summary>
public sealed class GameRulesStateTests
{
    // ------------------------------------------------------------------ immutability

    /// <summary>
    /// An accepted command leaves the caller's slice untouched. The aggregates in <c>NewState</c>
    /// are different objects, and the ones the caller passed in still hold their original values.
    /// </summary>
    [Fact]
    public void The_input_slice_is_unchanged_by_an_accepted_command()
    {
        var state = Worlds.InARun(RunSnapshots.With(gold: 40L));

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, input) => HandlerResult.Accept(input.Run.MoveCurrency(CurrencyId.GOLD, 60L, "fixture_grant"))),
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(100L);

        state.Run!.Gold.ShouldBe(40L, "P4: WorldSlice in, NEW WorldSlice out — no in-place mutation of the input.");
        result.NewState.ShouldNotBeSameAs(state);
        result.NewState.Run.ShouldNotBeSameAs(state.Run);
        result.NewState.Player.ShouldNotBeSameAs(state.Player);
    }

    /// <summary>
    /// A rejected command returns the caller's own slice, unchanged, even when the handler mutated
    /// state before the rule refused. The handler below gets a long way.
    /// </summary>
    [Fact]
    public void The_input_slice_is_unchanged_by_a_rejected_command()
    {
        var state = Worlds.InARun(RunSnapshots.With(gold: 40L, currentHp: 90, maxHp: 100));

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, input) =>
            {
                input.Run.MoveCurrency(CurrencyId.GOLD, 500L, "fixture_grant_before_refusal");
                input.Run.SetHitPoints(10, 100);
                input.Run.MoveTo(7);

                return HandlerResult.Reject(RejectionReason.INSUFFICIENT_FUNDS);
            }),
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
        result.Events.ShouldBeEmpty();

        result.NewState.ShouldBeSameAs(state);
        state.Run!.Gold.ShouldBe(40L);
        state.Run.CurrentHp.ShouldBe(90);
        state.Run.Position.ShouldBe(0);
    }

    /// <summary>
    /// Pure: the same slice, command and context produce the same answer. Compared through the
    /// canonical state hash rather than the snapshot records, because a snapshot carries
    /// <c>IReadOnlyDictionary</c> components which a record compares by reference, so two
    /// separately rehydrated aggregates holding identical state would otherwise compare unequal.
    /// </summary>
    [Fact]
    public void Apply_is_deterministic_over_identical_inputs()
    {
        var state = Worlds.InARun(RunSnapshots.With(gold: 40L));
        var table = Worlds.RunTable((_, input) =>
            HandlerResult.Accept(input.Run.MoveCurrency(CurrencyId.GOLD, 60L, "fixture_grant")));

        var first = SlayIdleRepeat.Core.GameRules.Execute(table, state, new Worlds.RunFixtureCommand(), Worlds.Context);
        var second = SlayIdleRepeat.Core.GameRules.Execute(table, state, new Worlds.RunFixtureCommand(), Worlds.Context);

        second.NewState.Run!.Gold.ShouldBe(first.NewState.Run!.Gold);

        StateHash(second).ShouldBe(StateHash(first));
        StateHash(second).ShouldNotBe(
            CanonicalStateWriter.HashRunCommandState(state.Player.ToSnapshot(), state.Run!.ToSnapshot()),
            "the command did change something — a hash comparison that also held against the INPUT " +
            "would be true of an Apply that did nothing at all.");
    }

    /// <summary>The canonical state hash over a result's slice.</summary>
    private static string StateHash(CommandResult result) =>
        CanonicalStateWriter.HashRunCommandState(
            result.NewState.Player.ToSnapshot(), result.NewState.Run!.ToSnapshot());

    /// <summary>A meta command's slice is cloned too, run or no run.</summary>
    [Fact]
    public void A_meta_command_clones_the_player_as_well()
    {
        var state = Worlds.OutsideARun();

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, input) =>
                HandlerResult.Accept(input.Player.MoveCurrency(CurrencyId.CROWNS, 5L, "fixture_grant"))),
            state,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(5L);
        state.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(0L);
        result.NewState.Run.ShouldBeNull();
    }

    // ------------------------------------------------------------------ the TTL anchors

    /// <summary>An accepted run command advances both anchors: the player's and the run's.</summary>
    [Fact]
    public void An_accepted_run_command_advances_both_applied_timestamps()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Accept()),
            Worlds.InARun(),
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        result.NewState.Player.LastAppliedAtUtc.ShouldBe(Worlds.NowUtc);
        result.NewState.Run!.LastAppliedAtUtc.ShouldBe(Worlds.NowUtc);
    }

    /// <summary>
    /// An accepted meta command advances the player's anchor and leaves the run's where it was: the
    /// run TTL is sliding, measured from the last accepted command to that run, and sliding it off
    /// the player's anchor would keep a run alive because its owner opened the shop.
    /// </summary>
    [Fact]
    public void An_accepted_meta_command_does_not_slide_the_runs_TTL()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            Worlds.InARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.NewState.Player.LastAppliedAtUtc.ShouldBe(Worlds.NowUtc);
        result.NewState.Run!.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning);
    }

    /// <summary>
    /// A refused command advances neither anchor: otherwise a client could hold a run open
    /// indefinitely by sending commands it knows will be refused.
    /// </summary>
    [Fact]
    public void A_refused_command_advances_no_timestamp()
    {
        var state = Worlds.InARun();

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Reject(RejectionReason.COOLDOWN_ACTIVE)),
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        // Read off the RESULT, which is what the Application layer persists, rather than off the
        // fixture variable: the two are the same object here by P4, and saying it this way is what
        // makes the claim "the TTL the caller stores did not move" instead of the weaker "Apply did
        // not reach into its argument".
        result.NewState.Player.LastAppliedAtUtc.ShouldBe(PlayerSnapshots.Midmorning);
        result.NewState.Run!.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning);
    }

    // ------------------------------------------------------------------ round-trip defects

    /// <summary>
    /// An aggregate that cannot rebuild itself from its own snapshot is a defect, raised where it
    /// happened rather than one command later inside a persistence adapter. Driven through the
    /// content snapshot: a tuning context whose Legend Level range excludes the fixture player
    /// makes the round trip fail for a real, described reason.
    /// </summary>
    [Fact]
    public void An_aggregate_that_does_not_round_trip_is_a_defect()
    {
        var context = Worlds.Context with
        {
            Content = Tests.Content.ProgressionDocuments.With(
                legendLevelMin: Core.Content.ContentValue.Number(50),
                legendLevelMax: Core.Content.ContentValue.Number(200)),
        };

        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            context));

        thrown.Message.ShouldContain("does not round-trip", Case.Sensitive);
        thrown.Message.ShouldContain("LegendLevel", Case.Sensitive);
    }
}
