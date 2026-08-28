using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The log pipeline's whole contract: one log event renders as exactly one line of compact JSON —
/// message template, level and structured properties machine-readable — so whatever scrapes stdout
/// gets rows, not prose.
/// </summary>
public sealed class ObservabilityLoggingTests
{
    [Fact]
    public void A_log_event_renders_as_one_line_of_compact_json_with_its_structured_property()
    {
        var output = new StringWriter();

        using (var logger = ObservabilityLogging.CreateLogger(output))
        {
            logger.Warning("Command {CommandName} was refused", "ROLL_DICE");
        }

        var text = output.ToString();

        text.ShouldNotBeNullOrWhiteSpace("the event has to reach the writer at all.");

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(
            1,
            "one event is exactly one line — a multi-line render breaks every line-oriented " +
            "scraper between this process and the log store.");

        using var row = JsonDocument.Parse(lines[0]);

        row.RootElement.GetProperty("@mt").GetString().ShouldBe(
            "Command {CommandName} was refused",
            "the message template travels intact, so the backend can group by it.");
        row.RootElement.GetProperty("@l").GetString().ShouldBe(
            "Warning", "the level travels as data, not as a prose prefix.");
        row.RootElement.GetProperty("CommandName").GetString().ShouldBe(
            "ROLL_DICE",
            "the structured property is a queryable field — string interpolation upstream would " +
            "have melted it into the message.");
    }

    [Fact]
    public void Two_events_render_as_two_lines()
    {
        var output = new StringWriter();

        using (var logger = ObservabilityLogging.CreateLogger(output))
        {
            logger.Warning("First {Ordinal}", 1);
            logger.Warning("Second {Ordinal}", 2);
        }

        output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(
            2, "the line is the unit of scraping; two events on one line are one broken row.");
    }

    [Fact]
    public void CreateLogger_refuses_a_null_writer()
    {
        Should.Throw<ArgumentNullException>(() => ObservabilityLogging.CreateLogger(null!));
    }
}
