using global::Sentry;

namespace SlayIdleRepeat.Adapters.Telemetry.Sentry;

/// <summary>
/// The adapter's composition hook: initialises the Sentry SDK from bound options. An empty DSN is
/// the SDK's own documented disabled state, so a composition root calls this unconditionally and a
/// deployment without Sentry simply sends nothing.
/// </summary>
public static class SentryInitialization
{
    /// <summary>Initialises the SDK. Dispose the returned handle to flush and shut it down.</summary>
    /// <param name="options">The bound configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static IDisposable Init(SentryTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return SentrySdk.Init(sentry => sentry.Dsn = options.Dsn);
    }
}
