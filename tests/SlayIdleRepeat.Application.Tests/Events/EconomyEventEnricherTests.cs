using Shouldly;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Events;

/// <summary>
/// The economy log's enrichment: real domain events gain exactly the identity the domain deliberately
/// does not carry, serialised in the wire's one event dialect.
/// </summary>
public sealed class EconomyEventEnricherTests
{
    private static readonly PlayerId Player = new("PLAYER_log");
    private static readonly RunId Run = new("RUN_log");
    private static readonly CommandId Command = new("CMD_log");
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>Real events with real sequence ordinals — produced through <c>GameRules.Apply</c>, never constructed.</summary>
    private static IReadOnlyList<DomainEvent> RealEvents()
    {
        var (game, player) = Worlds.InARun();
        var result = game.Send(player, new RollDiceCommand());

        result.Accepted.ShouldBeTrue("the fixture command was refused: " + result.Rejection);
        result.Events.ShouldNotBeEmpty("a ROLL_DICE with no events would make every case here vacuous.");

        return result.Events;
    }

    [Fact]
    public void Every_event_becomes_one_row_in_order()
    {
        var events = RealEvents();

        var rows = EconomyEventEnricher.Enrich(Player, Run, Command, Now, events);

        rows.Count.ShouldBe(events.Count);
        // The row's sequence is the event's own ordinal — the append-only log replays in the order
        // the domain produced.
        rows.Select(r => r.Sequence).ToArray().ShouldBe(events.Select(e => e.Sequence).ToArray());
    }

    [Fact]
    public void A_row_carries_the_identity_the_event_does_not()
    {
        var row = EconomyEventEnricher.Enrich(Player, Run, Command, Now, RealEvents())[0];

        row.Player.ShouldBe(Player);
        row.Run.ShouldBe(Run);
        row.CommandId.ShouldBe(Command);
        row.OccurredAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void A_rows_type_and_payload_speak_the_wire_dialect()
    {
        var events = RealEvents();

        var row = EconomyEventEnricher.Enrich(Player, Run, Command, Now, events)[0];

        row.EventType.ShouldBe(events[0].GetType().Name);
        row.PayloadJson.ShouldBe(
            WireJson.RenderEvent(events[0]),
            "one event dialect exists (the response envelope's); a log that serialised events its "
            + "own way would be the second vocabulary the wire renderer exists to rule out.");
    }

    [Fact]
    public void A_meta_commands_rows_carry_no_run()
    {
        var row = EconomyEventEnricher.Enrich(Player, run: null, Command, Now, RealEvents())[0];

        row.Run.ShouldBeNull();
    }

    [Fact]
    public void No_events_is_no_rows()
    {
        EconomyEventEnricher.Enrich(Player, Run, Command, Now, Array.Empty<DomainEvent>())
            .ShouldBeEmpty();
    }

    [Fact]
    public void Null_events_are_a_null_argument_fault()
    {
        Should.Throw<ArgumentNullException>(
            () => EconomyEventEnricher.Enrich(Player, Run, Command, Now, null!));
    }
}

/// <summary>The wire's event rendering, exposed for the log: shape pinned once.</summary>
public sealed class WireJsonRenderEventTests
{
    [Fact]
    public void An_event_renders_as_its_type_discriminator_beside_camel_cased_members()
    {
        var (game, player) = Worlds.InARun();
        var result = game.Send(player, new RollDiceCommand());
        result.Accepted.ShouldBeTrue();
        result.Events.ShouldNotBeEmpty();
        var @event = result.Events[0];

        var rendered = WireJson.RenderEvent(@event);

        rendered.ShouldStartWith("{\"type\":\"" + @event.GetType().Name + "\"");
        rendered.ShouldContain("\"sequence\":", Case.Sensitive,
            "members ride camelCased beside the discriminator — the same shape the response "
            + "envelope's event list already has on the wire.");
    }

    [Fact]
    public void A_null_event_is_a_null_argument_fault()
    {
        Should.Throw<ArgumentNullException>(() => WireJson.RenderEvent(null!));
    }
}
