using Shouldly;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Events;

/// <summary>
/// The fan-out: every sink gets the batch, in registration order, and a sink that fails is collected
/// rather than allowed to fail a command that has already committed.
/// </summary>
public sealed class DomainEventDispatcherTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    /// <summary>One stamped event, enough to tell "delivered" from "delivered something else".</summary>
    private static IReadOnlyList<DomainEvent> Batch() =>
        [new CurrencyChanged(1, CurrencyId.GOLD, 5, "test")];

    [Fact]
    public async Task DispatchAsync_delivers_the_same_batch_to_every_sink_in_registration_order()
    {
        var order = new List<string>();
        var first = new RecordingSink("first", order);
        var second = new RecordingSink("second", order);
        var batch = Batch();

        await new DomainEventDispatcher([first, second]).DispatchAsync(batch, Cancel);

        order.ShouldBe(
            new[] { "first", "second" },
            Case.Sensitive,
            "sinks are delivered to in the order they were registered, so a durable log registered " +
            "ahead of a best-effort one keeps that priority.");
        first.Batches[0].ShouldBe(batch, "the first sink was handed something other than the batch.");
        second.Batches[0].ShouldBe(batch, "the second sink was handed something other than the batch.");
    }

    [Fact]
    public async Task DispatchAsync_reports_no_failures_when_every_sink_takes_the_batch()
    {
        var failures = await new DomainEventDispatcher([new RecordingSink(), new RecordingSink()])
            .DispatchAsync(Batch(), Cancel);

        failures.ShouldBeEmpty("nothing failed.");
    }

    [Fact]
    public async Task DispatchAsync_collects_a_sink_that_throws_instead_of_letting_it_escape()
    {
        var failures = await new DomainEventDispatcher([new ThrowingSink()]).DispatchAsync(Batch(), Cancel);

        failures.Count.ShouldBe(
            1,
            "a sink threw and nothing was reported. Dispatch runs after the commit, so an escaping " +
            "exception would report a command that provably happened as an error.");
        failures[0].Error.ShouldContain(ThrowingSink.Message, Case.Sensitive, "what the sink threw");
    }

    [Fact]
    public async Task DispatchAsync_collects_a_sink_whose_task_faults()
    {
        var failures = await new DomainEventDispatcher([new FaultingSink()]).DispatchAsync(Batch(), Cancel);

        failures.Count.ShouldBe(
            1, "a sink that returns a faulted task failed just as surely as one that threw outright.");
        failures[0].Error.ShouldContain(FaultingSink.Message, Case.Sensitive, "what the sink faulted with");
    }

    [Fact]
    public async Task DispatchAsync_still_delivers_to_the_sinks_registered_after_one_that_failed()
    {
        var survivor = new RecordingSink();

        await new DomainEventDispatcher([new ThrowingSink(), survivor]).DispatchAsync(Batch(), Cancel);

        survivor.Batches.Count.ShouldBe(
            1,
            "one broken transport silenced every sink behind it, so analytics going down would take the " +
            "durable log with it.");
    }

    [Fact]
    public async Task DispatchAsync_names_the_failing_sink_so_a_reader_knows_which_transport_is_down()
    {
        var failures = await new DomainEventDispatcher([new RecordingSink(), new ThrowingSink()])
            .DispatchAsync(Batch(), Cancel);

        failures[0].Sink.ShouldContain(
            nameof(ThrowingSink),
            Case.Sensitive,
            "a failure that does not say which sink produced it leaves every transport equally suspect.");
    }

    [Fact]
    public void Constructor_refuses_a_null_list_of_sinks()
    {
        Should.Throw<ArgumentNullException>(() => new DomainEventDispatcher(null!))
            .ParamName.ShouldBe("sinks", "the failure has to name the argument the caller got wrong.");
    }

    /// <summary>
    /// 🔒 The reason the guard above is not enough on its own. A null <em>element</em> is caught at
    /// construction because dispatch cannot report it: the delivery loop collects whatever a sink
    /// throws, and the collecting arm asks the sink for its own name — so a null in the list faults
    /// twice, the second time inside the handler, and escapes as an error about a command that has
    /// already committed. A wiring mistake has to fail where the wiring is done.
    /// </summary>
    [Fact]
    public void Constructor_refuses_a_list_holding_a_null_sink()
    {
        Should.Throw<ArgumentNullException>(
                () => new DomainEventDispatcher([new RecordingSink(), null!, new RecordingSink()]))
            .ParamName.ShouldBe("sinks", "the failure has to name the argument the caller got wrong.");
    }

    [Fact]
    public void Constructor_accepts_a_list_of_real_sinks()
    {
        // The negative control for the two refusals above: without it they would both still pass if
        // the constructor refused every list it was ever handed.
        Should.NotThrow(() => new DomainEventDispatcher([new RecordingSink(), new ThrowingSink()]));
        Should.NotThrow(() => new DomainEventDispatcher([]));
    }

    [Fact]
    public async Task DispatchAsync_delivers_an_empty_batch_without_reporting_a_failure()
    {
        var sink = new RecordingSink();

        var failures = await new DomainEventDispatcher([sink]).DispatchAsync([], Cancel);

        failures.ShouldBeEmpty("an accepted command that produced no events has not failed.");
        sink.Batches.Count.ShouldBe(
            1, "an empty batch is still one command's answer; skipping it makes 'no events' and 'no command' alike.");
    }

    /// <summary>
    /// 🔒 The sink is a service of this layer, not a port, and that placement is the decision this
    /// case exists to keep.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transports an implementation of it will eventually reach — analytics, telemetry, the
    /// durable economy log — are each a port of their own with an owner and a real adapter. This is
    /// the in-process fan-out that sits above them, so moving it under the port namespace would put
    /// it under the rule that every port carries a real adapter beside its fake, and the only way to
    /// satisfy that here is two fakes — which is exactly what an absent port is better than.
    /// </para>
    /// <para>
    /// A move would not pass quietly today: the port rule would fail. It would fail while pointing at
    /// a missing adapter rather than at the move, and adding the fakes is the obvious way to make that
    /// message go away. This says so where the move would be made.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_event_sink_is_a_service_of_this_layer_and_not_a_port()
    {
        var sink = typeof(IDomainEventSink);

        sink.Namespace.ShouldBe(
            "SlayIdleRepeat.Application.Services.Events",
            "the sink has moved. If it moved under the port namespace, read this case's remarks before " +
            "making the port rule green; if it moved elsewhere, this pin moves with it.");

        sink.Assembly
            .GetTypes()
            .Where(t => t.IsInterface && t.Namespace?.StartsWith(
                "SlayIdleRepeat.Application.Ports", StringComparison.Ordinal) == true)
            .Select(t => t.Name)
            .ShouldNotContain(
                nameof(IDomainEventSink),
                "a second interface of this name under the port namespace enrols the fan-out in the port " +
                "catalogue by the back door.");
    }
}
