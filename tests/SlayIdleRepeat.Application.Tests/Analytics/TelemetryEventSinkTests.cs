using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Analytics;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Analytics;

/// <summary>
/// The sink over the telemetry port: one counter measurement per domain event, tagged with the
/// event's type — what puts "the domain is emitting" on a dashboard.
/// </summary>
public sealed class TelemetryEventSinkTests
{
    /// <summary>A port double that keeps every measurement.</summary>
    private sealed class CapturingTelemetryPort : ITelemetryPort
    {
        internal List<(string Name, double Value, (string Key, string Value)[] Tags)> Metrics { get; } = [];

        public void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null)
        {
        }

        public IDisposable BeginSpan(string name) => new Scope();

        public void RecordMetric(string name, double value, params (string Key, string Value)[] tags) =>
            Metrics.Add((name, value, tags));

        private sealed class Scope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private static DispatchedEvents Batch(params DomainEvent[] events)
    {
        var (game, player) = Worlds.InARun();

        return new DispatchedEvents(player, new RollDiceCommand(), game.State(player), events);
    }

    [Fact]
    public async Task ReceiveAsync_counts_each_domain_event_by_its_type()
    {
        var batch = Batch(
            new CurrencyChanged(1, CurrencyId.GOLD, 5, "test"),
            new CurrencyChanged(2, CurrencyId.GOLD, -2, "spend"),
            new DiceRolled(3, 4));
        var port = new CapturingTelemetryPort();

        await new TelemetryEventSink(port).ReceiveAsync(batch, Worlds.Cancel);

        port.Metrics.Count.ShouldBe(3, "one measurement per event, so the counter IS the event count.");
        port.Metrics.ShouldAllBe(
            m => m.Name == "domain_events", "every measurement lands on the one counter.");
        port.Metrics.ShouldAllBe(m => m.Value == 1d, "each event counts once.");
        port.Metrics.Count(m => m.Tags.Contains(("type", "CurrencyChanged"))).ShouldBe(
            2, "two currency movements, each tagged with its own type.");
        port.Metrics.Count(m => m.Tags.Contains(("type", "DiceRolled"))).ShouldBe(
            1, "one roll, tagged with its own type.");
    }

    [Fact]
    public async Task ReceiveAsync_records_nothing_for_an_eventless_batch()
    {
        var port = new CapturingTelemetryPort();

        await new TelemetryEventSink(port).ReceiveAsync(Batch(), Worlds.Cancel);

        port.Metrics.ShouldBeEmpty("no events, no measurements — a zero row would be an invention.");
    }

    [Fact]
    public async Task ReceiveAsync_refuses_a_null_batch()
    {
        var sink = new TelemetryEventSink(new CapturingTelemetryPort());

        await Should.ThrowAsync<ArgumentNullException>(() => sink.ReceiveAsync(null!, Worlds.Cancel));
    }

    [Fact]
    public void Constructor_refuses_a_null_port()
    {
        Should.Throw<ArgumentNullException>(() => new TelemetryEventSink(null!));
    }
}
