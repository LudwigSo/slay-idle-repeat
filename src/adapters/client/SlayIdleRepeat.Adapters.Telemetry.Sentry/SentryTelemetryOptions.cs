namespace SlayIdleRepeat.Adapters.Telemetry.Sentry;

/// <summary>
/// The adapter's configuration, bound from the <c>Sentry</c> section — key names here ARE the
/// deployment contract (<c>Sentry__Dsn</c> in the compose file and infra/README.md).
/// </summary>
public sealed class SentryTelemetryOptions
{
    /// <summary>The project DSN. Empty by default — the SDK's own documented "disabled".</summary>
    public string Dsn { get; set; } = "";
}
