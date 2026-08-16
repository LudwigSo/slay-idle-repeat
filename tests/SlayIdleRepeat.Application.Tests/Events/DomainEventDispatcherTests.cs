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
}
