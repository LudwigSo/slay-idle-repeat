using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §7 — <c>DomainEvent.Sequence</c> is the event's <b>ordinal within one
/// <c>CommandResult</c>'s list</b>, and <c>GameRules.Apply</c> is what assigns it.
/// </summary>
/// <remarks>
/// The ruling M1-03 recorded when it authored <c>DomainEvent(int Sequence)</c>: assigned by
/// <c>Apply</c>, <b>never</b> by a constructor and never by a caller. ⚠️ It is not `14` §16.3's wire
/// <c>sequence</c>, which is the per-run/per-player <b>command</b> counter on the request envelope
/// and lives in <c>SlayIdleRepeat.Contracts</c>.
/// </remarks>
public sealed class GameRulesEventTests
{
    private static CurrencyChanged Unstamped(long delta, string reason) =>
        new(DomainEvent.UnstampedSequence, CurrencyId.GOLD, delta, reason);

    /// <summary>
    /// 🔒 The ordinal runs 1, 2, 3 in the order the handler produced the events — and it starts at
    /// <b>1</b>, not 0.
    /// </summary>
    /// <remarks>
    /// The floor is what makes <c>DomainEvent.UnstampedSequence</c> (which is 0) mean something:
    /// M1-03's remarks require <c>Apply</c> to be able to tell an unstamped event from a first one,
    /// and a 0-based stamp would make the two identical — at which point the refusal below could
    /// never fire on a genuinely-first event.
    /// </remarks>
    [Fact]
    public void Apply_stamps_each_event_with_its_ordinal_within_this_result()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept(
                Unstamped(1L, "first"),
                Unstamped(2L, "second"),
                Unstamped(3L, "third"))),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Events.Select(e => e.Sequence).ShouldBe(new[] { 1, 2, 3 });
        result.Events.Cast<CurrencyChanged>().Select(e => e.Reason)
            .ShouldBe(new[] { "first", "second", "third" });
    }

    /// <summary>
    /// 🔒 The ordinal is <b>per result</b>, not a counter that runs across commands: a second
    /// <c>Apply</c> starts again at 1.
    /// </summary>
    /// <remarks>
    /// This is the assertion that fails if <c>Sequence</c> is ever confused with `14` §16.3's wire
    /// <c>sequence</c>. Conflating the two would make the economy log unorderable within a command
    /// and the idempotency protocol wrong at the same time — two different numbers, one word.
    /// </remarks>
    [Fact]
    public void The_ordinal_restarts_for_every_command_and_is_not_the_wire_sequence()
    {
        var table = Worlds.MetaTable((_, _) => HandlerResult.Accept(Unstamped(1L, "only")));
        var state = Worlds.OutsideARun();

        var first = SlayIdleRepeat.Core.GameRules.Execute(table, state, new Worlds.MetaFixtureCommand(), Worlds.Context);
        var second = SlayIdleRepeat.Core.GameRules.Execute(
            table, first.NewState, new Worlds.MetaFixtureCommand(), Worlds.Context);

        first.Events.ShouldHaveSingleItem().Sequence.ShouldBe(1);
        second.Events.ShouldHaveSingleItem().Sequence.ShouldBe(1);
    }

    /// <summary>
    /// 🔒 A handler that stamped its own <c>Sequence</c> is a <b>defect</b>. It is refused rather
    /// than silently overwritten.
    /// </summary>
    /// <remarks>
    /// Overwriting would let the mistake pass unnoticed until the economy log (`14` §7.1) and the
    /// animation script (`14` §2.4) disagreed about the order of one command's effects — and by then
    /// the rows are in Postgres. The refusal names the event type, so a reader knows which producer.
    /// </remarks>
    [Fact]
    public void An_event_that_arrives_already_stamped_is_refused()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept(
                new CurrencyChanged(7, CurrencyId.GOLD, 1L, "stamped_by_the_handler"))),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldContain(nameof(CurrencyChanged), Case.Sensitive);
        thrown.Message.ShouldContain("already stamped", Case.Sensitive);
        thrown.Message.ShouldContain("UnstampedSequence", Case.Sensitive);
    }

    /// <summary>A hole in the event list is a defect: neither the log nor the animation can read it.</summary>
    [Fact]
    public void A_null_event_is_refused_and_says_where()
    {
        Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
                Worlds.MetaTable((_, _) => HandlerResult.Accept(Unstamped(1L, "first"), null!)),
                Worlds.OutsideARun(),
                new Worlds.MetaFixtureCommand(),
                Worlds.Context))
            .Message.ShouldContain("null event at position 1", Case.Sensitive);
    }

    /// <summary>
    /// A command that produced no events is accepted with an empty list — not a null, and not a
    /// rejection.
    /// </summary>
    [Fact]
    public void An_accepted_command_may_produce_no_events()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept()),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 The event list is handed out read-only. `14` §7.1 appends it to Postgres, `14` §2.4
    /// replays it and `28` D counts it; a consumer that could rewrite it changes what the other two
    /// see.
    /// </summary>
    [Fact]
    public void The_event_list_cannot_be_written_through()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept(Unstamped(1L, "only"))),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Events.ShouldNotBeAssignableTo<DomainEvent[]>(
            "an array handed out as IReadOnlyList<T> casts straight back to the array — the exact " +
            "hole 30 §11.2's exposed-collection check exists for, one layer out.");

        // ReadOnlyCollection<T> does implement IList<T>, explicitly, and throws on every write.
        // Asserting the WRITE throws rather than that the cast fails is the assertion that actually
        // describes the boundary; a type check would pass for any wrapper and prove nothing.
        var asList = (IList<DomainEvent>)result.Events;

        Should.Throw<NotSupportedException>(() => asList.Add(Unstamped(1L, "smuggled")));
        Should.Throw<NotSupportedException>(() => asList[0] = Unstamped(1L, "overwritten"));
        Should.Throw<NotSupportedException>(asList.Clear);
    }

    /// <summary>
    /// 🔒 …and the <b>empty</b> list is not written through either: <c>Apply</c> hands back its own
    /// shared empty list, never the one the handler happened to return.
    /// </summary>
    /// <remarks>
    /// The rule above only covers the path where a stamped array is built and wrapped. A handler
    /// that returned a <c>List&lt;DomainEvent&gt;</c> it still holds — the natural shape for one
    /// that builds events conditionally and this time built none — could otherwise append to
    /// <c>CommandResult.Events</c> after <c>Apply</c> had returned, and `14` §7.1's economy log
    /// would carry rows for a command that never produced them.
    /// </remarks>
    [Fact]
    public void An_empty_event_list_is_not_the_handlers_own_list()
    {
        var handlers = new List<DomainEvent>();

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept(handlers)),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Events.ShouldBeEmpty();
        result.Events.ShouldNotBeSameAs(handlers);

        handlers.Add(Unstamped(1L, "appended_after_Apply_returned"));

        result.Events.ShouldBeEmpty("the result's list is Apply's, not the handler's.");
    }

    /// <summary>
    /// 🔒 The stamp is applied through <c>with</c>, which reaches every subtype through the abstract
    /// record's virtual <c>&lt;Clone&gt;$</c> — and every other component survives it unchanged.
    /// </summary>
    /// <remarks>
    /// M1-03 verified the mechanism and deliberately left <c>Sequence</c> as <c>init</c> for this,
    /// while making <c>CurrencyChanged.Reason</c> get-only. This is the assertion that the
    /// <c>with</c> did not quietly drop the attribution `21` §8.3's <c>income_attribution.csv</c>
    /// groups by.
    /// </remarks>
    [Fact]
    public void Stamping_preserves_every_other_component()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, _) => HandlerResult.Accept(
                new CurrencyChanged(DomainEvent.UnstampedSequence, CurrencyId.MERGE_DUST, -25L, "fixture_spend"))),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        var stamped = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>();

        stamped.Sequence.ShouldBe(1);
        stamped.Id.ShouldBe(CurrencyId.MERGE_DUST);
        stamped.Delta.ShouldBe(-25L);
        stamped.Reason.ShouldBe("fixture_spend");
    }

    /// <summary>
    /// 🔒 A refused command carries no events, whatever its handler built before the rule refused —
    /// they belong to a state that was discarded with the rejection.
    /// </summary>
    [Fact]
    public void A_refused_command_carries_no_events()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, input) =>
            {
                input.Player.MoveCurrency(CurrencyId.CROWNS, 5L, "granted_before_the_refusal");
                return HandlerResult.Reject(RejectionReason.CAP_REACHED);
            }),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Events.ShouldBeEmpty();
    }
}
