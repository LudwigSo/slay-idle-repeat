namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>
/// Operational telemetry: exceptions, spans and metrics, for whoever is on call rather than for
/// product analytics.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>No method surfaces a vendor failure as an exception.</b> Telemetry observes the system; a
/// telemetry backend that is down must never take a request down with it. Bad arguments are the
/// only throws, and a blank or <see langword="null"/> name is an <see cref="ArgumentException"/>
/// from every method here.
/// </para>
/// <para>
/// Fire-and-forget: every call returns immediately, and what an implementation could not deliver
/// it drops at its own edge.
/// </para>
/// </remarks>
public interface ITelemetryPort
{
    /// <summary>Reports an exception that was caught, with whatever context the catcher can name.</summary>
    /// <param name="error">What was thrown.</param>
    /// <param name="context">Extra key/value context, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    void RecordException(Exception error, IReadOnlyDictionary<string, string>? context = null);

    /// <summary>Opens a named span; disposing it closes the span. The scope is double-dispose safe.</summary>
    /// <param name="name">What the span measures.</param>
    /// <returns>A non-null scope whose disposal ends the span. Disposing it twice is harmless.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank or null.</exception>
    IDisposable BeginSpan(string name);

    /// <summary>Records one measurement of a named metric.</summary>
    /// <param name="name">The metric's name.</param>
    /// <param name="value">The measurement.</param>
    /// <param name="tags">Dimensions the measurement is sliced by. May be empty.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank or null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tags"/> is passed explicitly as null.</exception>
    void RecordMetric(string name, double value, params (string Key, string Value)[] tags);
}
