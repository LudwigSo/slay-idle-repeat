using System.Diagnostics;
using System.Diagnostics.Metrics;
using Shouldly;
using SlayIdleRepeat.Adapters.Telemetry.OpenTelemetry;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// The OTel adapter's own behaviour, observed through the runtime's in-process listeners and never
/// a collector: spans are real activities, metrics are real measurements, and an exception marks
/// the span it happened under.
/// </summary>
public sealed class OpenTelemetryTelemetryTests
{
    /// <summary>A listener over the adapter's own source, recording starts and stops.</summary>
    private static ActivityListener Listening(List<Activity> started, List<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OpenTelemetryTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = started.Add,
            ActivityStopped = stopped.Add,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    [Fact]
    public void BeginSpan_starts_an_activity_on_the_adapters_source_and_dispose_ends_it()
    {
        var started = new List<Activity>();
        var stopped = new List<Activity>();
        using var listener = Listening(started, stopped);
        using var adapter = new OpenTelemetryTelemetry();

        var span = adapter.BeginSpan("apply_command");

        started.ShouldHaveSingleItem("one BeginSpan is one activity.")
            .OperationName.ShouldBe("apply_command", "the span carries the caller's name, verbatim.");
        stopped.ShouldBeEmpty("the span is still open — only disposal ends it.");

        span.Dispose();

        stopped.ShouldHaveSingleItem("disposing the scope is what ends the activity.")
            .OperationName.ShouldBe("apply_command");
    }

    [Fact]
    public void A_span_disposed_twice_ends_its_activity_once()
    {
        var started = new List<Activity>();
        var stopped = new List<Activity>();
        using var listener = Listening(started, stopped);
        using var adapter = new OpenTelemetryTelemetry();

        var span = adapter.BeginSpan("apply_command");

        span.Dispose();
        span.Dispose();

        stopped.Count.ShouldBe(
            1,
            "a second dispose must be a no-op — an activity stopped twice would report a second, " +
            "phantom duration to the exporter.");
    }

    [Fact]
    public void RecordMetric_publishes_the_measurement_with_its_tags_on_the_adapters_meter()
    {
        var measurements = new List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OpenTelemetryTelemetry.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();

        using var adapter = new OpenTelemetryTelemetry();

        adapter.RecordMetric("domain_events", 2.5, ("type", "DiceRolled"));

        var (instrument, value, tags) = measurements.ShouldHaveSingleItem(
            "one RecordMetric is one measurement.");

        instrument.ShouldBe("domain_events", "the instrument carries the metric's own name.");
        value.ShouldBe(2.5, "the measurement, exactly.");
        tags.ShouldContain(
            new KeyValuePair<string, object?>("type", "DiceRolled"),
            "the tag travels with the measurement, so the series can be sliced by it.");
    }

    [Fact]
    public void RecordException_marks_the_current_activity_as_failed_with_an_exception_event()
    {
        var started = new List<Activity>();
        var stopped = new List<Activity>();
        using var listener = Listening(started, stopped);
        using var adapter = new OpenTelemetryTelemetry();

        using (adapter.BeginSpan("failing_work"))
        {
            adapter.RecordException(
                new InvalidOperationException("boom"),
                new Dictionary<string, string> { ["where"] = "test" });
        }

        var activity = started.ShouldHaveSingleItem();

        activity.Status.ShouldBe(
            ActivityStatusCode.Error,
            "an exception under a span is that span failing — a green trace over a recorded " +
            "exception is a trace nobody can debug from.");
        activity.Events.ShouldContain(
            activityEvent => activityEvent.Name == "exception",
            "the OTel convention's exception event, so backends recognise it without translation.");
    }

    [Fact]
    public void RecordException_without_an_open_span_still_returns_quietly()
    {
        using var adapter = new OpenTelemetryTelemetry();

        Should.NotThrow(
            () => adapter.RecordException(new InvalidOperationException("boom")),
            "not every exception happens under a span, and telemetry never throws for backend or " +
            "context it does not have.");
    }

    [Fact]
    public void RecordException_refuses_a_null_error()
    {
        using var adapter = new OpenTelemetryTelemetry();

        Should.Throw<ArgumentNullException>(
            () => adapter.RecordException(null!),
            "argument guards are the adapter's own — 'record nothing' spelled as a call fails " +
            "where it is made, not on some exporter thread later.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BeginSpan_refuses_a_blank_name(string? name)
    {
        using var adapter = new OpenTelemetryTelemetry();

        Should.Throw<ArgumentException>(() => adapter.BeginSpan(name!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordMetric_refuses_a_blank_name(string? name)
    {
        using var adapter = new OpenTelemetryTelemetry();

        Should.Throw<ArgumentException>(() => adapter.RecordMetric(name!, 1));
    }
}
