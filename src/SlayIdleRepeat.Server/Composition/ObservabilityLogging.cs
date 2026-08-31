using System.Globalization;
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
        private readonly object _writeGate = new();

        public void Emit(LogEvent logEvent)
        {
            // The formatter writes an event as several writes, and sinks are called concurrently:
            // unsynchronised, two events interleave into lines that no JSON reader can parse, which
            // is the one property everything downstream of stdout depends on.
            //
            // 🔒 Rendered into a buffer first, so the line reaches the writer in ONE call. The lock
            // only ever bound this sink's own callers, and this sink is not the only thing writing
            // to the container's log stream: the areas that have no logger yet write their markers
            // straight to Console.Error, which in a container is interleaved with stdout. A write
            // arriving between two halves of a formatted event splits it just as surely as a second
            // event would, and no lock here can reach that writer.
            var line = new StringWriter(CultureInfo.InvariantCulture);

            _formatter.Format(logEvent, line);

            lock (_writeGate)
            {
                output.Write(line.ToString());
            }
        }
    }
}
