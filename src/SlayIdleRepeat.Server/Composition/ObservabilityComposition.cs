using Serilog;
using SlayIdleRepeat.Adapters.Analytics.PostHog;
using SlayIdleRepeat.Adapters.Telemetry.OpenTelemetry;
using SlayIdleRepeat.Adapters.Telemetry.Sentry;
using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The observability functional area: Serilog to stdout, the OTel SDK over the telemetry adapter's
/// source and meter, Sentry from <c>Sentry:Dsn</c>, and the PostHog analytics sink from
/// <c>PostHog:*</c> — with the periodic flush that actually gets tracked events off the process.
/// </summary>
/// <remarks>
/// <para>
/// Composition-root code: this is the one place the observability adapters are named. Other areas
/// take the composed ports from <see cref="AnalyticsSink"/> / <see cref="Telemetry"/> — the same
/// instances this area flushes and exports — never new their own (the <see cref="GameBackbone"/>
/// rule; a second sink would be a second, unflushed queue).
/// </para>
/// <para>
/// Both vendors are off by a bare default: an empty <c>Sentry:Dsn</c> is the SDK's own documented
/// disabled state and is initialised unconditionally; <c>PostHog:Enabled=false</c> drops every
/// event at the sink and is announced by exactly one startup warning.
/// </para>
/// </remarks>
public static class ObservabilityComposition
{
    private static readonly object InitializationGate = new();
    private static ObservabilityArea? _shared;

    /// <summary>Configures logging, tracing, metrics, crash reporting and the analytics flush.</summary>
    /// <param name="builder">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Log.Logger = ObservabilityLogging.CreateLogger(Console.Out);

        // dispose: true hands the logger's lifetime to the host, so a stopping process closes and
        // flushes it instead of leaving whatever the pipeline still held unwritten.
        builder.Host.UseSerilog(Log.Logger, dispose: true);

        builder.Services.AddSlayIdleRepeatOpenTelemetry();

        var area = Shared(builder.Configuration);

        if (!area.PostHogOptions.Enabled)
        {
            Log.Warning(
                "PostHog analytics is DISABLED (PostHog:Enabled=false): every analytics event this "
                + "process produces is dropped at the sink and reaches no backend");
        }
        else if (string.IsNullOrWhiteSpace(area.PostHogOptions.Host)
                 || string.IsNullOrWhiteSpace(area.PostHogOptions.ProjectKey))
        {
            // Enabled without an address or a key posts nothing and, by the port's never-throw
            // promise, says nothing either — so the one place that can still see it says it here.
            Log.Error(
                "PostHog analytics is ENABLED but misconfigured (PostHog:Host or PostHog:ProjectKey "
                + "is blank): every batch will fail at the adapter's edge and be dropped silently");
        }

        builder.Services.AddHostedService(_ => new PostHogFlushLoop(area.Analytics));

        return builder;
    }

    /// <summary>
    /// Adds the area's middleware. Ordering, declared once: the telemetry exception middleware is
    /// the pipeline's FIRST, so it observes whatever escapes everything downstream of it.
    /// </summary>
    /// <param name="app">The composed host.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    public static WebApplication UseObservability(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<TelemetryExceptionMiddleware>(Telemetry(app.Configuration));

        return app;
    }

    /// <summary>The one analytics sink this process posts through.</summary>
    internal static IAnalyticsSinkPort AnalyticsSink(IConfiguration configuration) =>
        Shared(configuration).Analytics;

    /// <summary>The server's one telemetry port — the OTel adapter.</summary>
    internal static ITelemetryPort Telemetry(IConfiguration configuration) =>
        Shared(configuration).OpenTelemetry;

    /// <summary>The area's shared state, built on first call — the <see cref="GameBackbone"/> pattern.</summary>
    private static ObservabilityArea Shared(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (Volatile.Read(ref _shared) is { } built)
        {
            return built;
        }

        lock (InitializationGate)
        {
            return _shared ??= new ObservabilityArea(configuration);
        }
    }

    /// <summary>The composed adapters, shared across areas for the life of the process.</summary>
    private sealed class ObservabilityArea
    {
        internal ObservabilityArea(IConfiguration configuration)
        {
            PostHogOptions = configuration.GetSection("PostHog").Get<PostHogOptions>() ?? new PostHogOptions();
            Analytics = new PostHogAnalyticsSink(PostHogOptions, new SocketsHttpHandler());
            OpenTelemetry = new OpenTelemetryTelemetry();

            var sentryOptions = configuration.GetSection("Sentry").Get<SentryTelemetryOptions>()
                                ?? new SentryTelemetryOptions();

            // Held for the life of the process; disposing it would shut crash reporting down.
            SentryHandle = SentryInitialization.Init(sentryOptions);
        }

        internal PostHogOptions PostHogOptions { get; }

        internal PostHogAnalyticsSink Analytics { get; }

        internal OpenTelemetryTelemetry OpenTelemetry { get; }

        internal IDisposable SentryHandle { get; }
    }

    /// <summary>
    /// What makes <see cref="PostHogAnalyticsSink.Track"/> honest in production: drains the queue
    /// every <see cref="Interval"/>, and once more at shutdown so a stopping process posts what it
    /// still holds.
    /// </summary>
    private sealed class PostHogFlushLoop(PostHogAnalyticsSink sink) : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    await Drain(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown: fall through to the final drain.
            }

            await Drain(CancellationToken.None).ConfigureAwait(false);
        }

        // An unhandled exception out of ExecuteAsync stops the whole host, so a side channel that
        // is allowed to fail must not be allowed to escape — not on a tick, and least of all on the
        // shutdown drain, where the failure would replace an orderly stop with a crash.
        private async Task Drain(CancellationToken ct)
        {
            try
            {
                await sink.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown interrupted this tick's flush; the final drain still runs.
            }
            catch (Exception error)
            {
                Log.Warning(error, "The analytics flush failed; the buffered batch was dropped");
            }
        }
    }
}
