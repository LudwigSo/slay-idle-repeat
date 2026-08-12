using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §2.1 <b>P4</b> (immutable) and <b>P1</b> (pure) — what <c>Apply</c> does to the slice it
/// is handed, and what it must never do to it.
/// </summary>
public sealed class GameRulesStateTests
{
    // ------------------------------------------------------------------ P4 · immutability

    /// <summary>
    /// 🔒 <b>P4</b> — an <b>accepted</b> command leaves the caller's slice untouched. The aggregates
    /// in <c>NewState</c> are different objects, and the ones the caller passed in still hold their
    /// original values.
    /// </summary>
    /// <remarks>
    /// The handler here moves Gold, which is a real mutation through a real internal mutator — not a
    /// no-op that would make any implementation pass. Asserting the caller's balance is what
    /// distinguishes "the slice was copied" from "the same objects came back".
    /// </remarks>
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
    /// 🔒 <b>P4</b> — a <b>rejected</b> command returns the caller's own slice, unchanged, even when
    /// the handler mutated state before the rule refused.
    /// </summary>
    /// <remarks>
    /// This is the half that makes P4 worth having: `30` §2.1 sells it as making <em>"replay,
    /// rollback, speculation and the client's optimistic prediction trivial rather than
    /// dangerous"</em>, and what the Application layer actually needs is to keep the state it loaded
    /// across a refusal without reasoning about how far a rejected handler got. The handler below
    /// gets a long way.
    /// </remarks>
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
    /// 🔒 <b>P1</b> — the same slice, command and context produce the same answer. The clone is what
    /// makes this assertable twice over one fixture at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ A weak-looking property that is doing real work here: without the clone the first call
    /// would have moved the input's Gold to 100, and the second would answer 160. Determinism over
    /// the <em>draws</em> is pinned separately in <c>GameRulesRngTests</c>.
    /// </para>
    /// <para>
    /// 🔒 Compared through `14` §16.6's <c>stateHash</c> rather than by comparing the two snapshot
    /// records. A snapshot carries <c>IReadOnlyDictionary</c> components, and a record's synthesized
    /// equality compares those with <c>EqualityComparer&lt;T&gt;.Default</c> — i.e. by
    /// <b>reference</b> — so two separately rehydrated aggregates holding identical state compare
    /// <em>unequal</em>. The hash is the thing the client, the parity test (`14` §13) and the
    /// reconnect check actually compare, and it reads the values.
    /// </para>
    /// </remarks>
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

    /// <summary>`14` §16.6's state hash over a result's slice.</summary>
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

    // ------------------------------------------------------------------ 14 §16.3 · the TTL anchors

    /// <summary>
    /// 🔒 `14` §16.3 — an accepted <b>run</b> command advances both anchors: the player's and the
    /// run's.
    /// </summary>
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
    /// 🔒 `14` §16.3 — an accepted <b>meta</b> command advances the player's anchor and leaves the
    /// run's where it was.
    /// </summary>
    /// <remarks>
    /// This is the reason M1-05 put a second <c>LastAppliedAtUtc</c> on <c>Run</c> at all: the run
    /// TTL is <em>sliding, measured from the last accepted command <b>to that run</b></em>, and
    /// sliding it off the player's anchor would keep a run alive because its owner opened the shop.
    /// A test that only checked the player's would pass under exactly that bug.
    /// </remarks>
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
    /// 🔒 A <b>refused</b> command advances neither anchor: it changed nothing, so it must not
    /// extend a TTL either — otherwise a client could hold a run open indefinitely by sending
    /// commands it knows will be refused.
    /// </summary>
    [Fact]
    public void A_refused_command_advances_no_timestamp()
    {
        var state = Worlds.InARun();

        SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.RunTable((_, _) => HandlerResult.Reject(RejectionReason.COOLDOWN_ACTIVE)),
            state,
            new Worlds.RunFixtureCommand(),
            Worlds.Context);

        state.Player.LastAppliedAtUtc.ShouldBe(PlayerSnapshots.Midmorning);
        state.Run!.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning);
    }

    // ------------------------------------------------------------------ round-trip defects

    /// <summary>
    /// 🔒 An aggregate that cannot rebuild itself from its own snapshot is a <b>defect</b>, raised
    /// where it happened rather than one command later inside a persistence adapter.
    /// </summary>
    /// <remarks>
    /// Driven through the content snapshot, which is the one input to the clone a test can make
    /// legitimately wrong: <c>Player.Rehydrate</c> validates Legend Level against `07` §1.1's range,
    /// read from <c>tuning/progression.json</c>, so a context whose tuning puts the range out of
    /// reach of the player it is handed makes the round trip fail for a real, described reason.
    /// </remarks>
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
