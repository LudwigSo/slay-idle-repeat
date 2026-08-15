using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// <c>DomainEvent.Sequence</c> is the event's ordinal within one <c>CommandResult</c>'s list,
/// assigned by <c>GameRules.Apply</c> — never by a constructor or a caller. It is not the wire
/// <c>sequence</c> in <c>SlayIdleRepeat.Contracts</c>, which is the per-run/per-player command
/// counter on the request envelope.
/// </summary>
public sealed class GameRulesEventTests
{
    private static CurrencyChanged Unstamped(long delta, string reason) =>
        new(DomainEvent.UnstampedSequence, CurrencyId.GOLD, delta, reason);

    /// <summary>
    /// The ordinal runs 1, 2, 3 in production order and starts at 1, not 0 — the floor that keeps
    /// it distinguishable from <c>DomainEvent.UnstampedSequence</c> (0).
    /// </summary>
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

    /// <summary>The ordinal is per result, not a counter across commands: a second <c>Apply</c> starts again at 1.</summary>
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
    /// A handler that stamped its own <c>Sequence</c> is a defect and is refused rather than
    /// silently overwritten.
    /// </summary>
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

    /// <summary>The event list is handed out read-only; multiple independent consumers read the same list.</summary>
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
    /// The empty list is not written through either: <c>Apply</c> hands back its own shared empty
    /// list, never the one the handler happened to return — otherwise a handler still holding its
    /// list could append to it after <c>Apply</c> returned.
    /// </summary>
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
    /// The stamp is applied through <c>with</c> (reaching every subtype via the abstract record's
    /// virtual <c>&lt;Clone&gt;$</c>), and every other component survives it unchanged.
    /// </summary>
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
    /// A refused command carries no events, whatever its handler built before the rule refused —
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
