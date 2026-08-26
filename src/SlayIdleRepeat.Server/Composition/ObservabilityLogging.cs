using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The server's log pipeline: structured events rendered as one-line compact JSON to a text
/// writer — stdout in production, so the backend is whatever scrapes the container's output.
/// </summary>
public static class ObservabilityLogging
{
    /// <summary>
    /// A logger writing every event as exactly one line of compact JSON — message template, level
    /// and structured properties all machine-readable — to <paramref name="output"/>.
    /// </summary>
    /// <param name="output">Where the lines go. Production passes the console; tests pass a writer they can read back.</param>
    /// <returns>The configured logger. Dispose it to flush.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    public static Logger CreateLogger(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        return new LoggerConfiguration()
            .WriteTo.Sink(new CompactJsonTextWriterSink(output))
            .CreateLogger();
    }

    /// <summary>Renders each event through <see cref="CompactJsonFormatter"/> — one line per event.</summary>
    private sealed class CompactJsonTextWriterSink(TextWriter output) : ILogEventSink
    {
        private readonly CompactJsonFormatter _formatter = new();

        public void Emit(LogEvent logEvent) => _formatter.Format(logEvent, output);
    }
}
