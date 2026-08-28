using System.Globalization;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Inbox;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The inbox area's composition: the claim seam the command pipeline reads through, and the nightly
/// expiry sweep.
/// </summary>
/// <remarks>
/// <para>
/// Both halves are <c>null</c>-on-a-volatile-process, deliberately. Without
/// <c>ConnectionStrings:Postgres</c> there is no message store, so the claim command fails as the
/// loading defect it is rather than answering a player "you have no messages", and no sweep starts
/// rather than one that finds nothing for ever.
/// </para>
/// <para>
/// 🔒 The sweep runs even when the mail kill switch is thrown. Mail off hides the inbox; it must
/// never be what destroys the rewards an incident is about to compensate, so the switch is not read
/// here and the job's own remarks say the same thing at the other end.
/// </para>
/// </remarks>
public static class InboxComposition
{
    /// <summary>The configuration key naming how many due messages one sweep takes.</summary>
    public const string BatchLimitKey = "Inbox:ExpiryBatchLimit";

    /// <summary>The startup marker naming the sweep's schedule, so a deployment's log states it.</summary>
    internal const string SweepScheduledMarker = "Inbox expiry sweep scheduled";

    private static readonly object Gate = new();
    private static InboxCommandSupport? _support;
    private static bool _resolved;

    /// <summary>
    /// The claim seam the command pipeline loads the inbox through, or <c>null</c> on a process with
    /// no message store.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    public static InboxCommandSupport? CommandSupport(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (Volatile.Read(ref _resolved))
        {
            return _support;
        }

        lock (Gate)
        {
            if (!_resolved)
            {
                _support = Messages(configuration) is { } messages
                    ? new InboxCommandSupport(messages)
                    : null;
                Volatile.Write(ref _resolved, true);
            }

            return _support;
        }
    }

    /// <summary>The inbox store, or <c>null</c> on a volatile (no-database) process.</summary>
    /// <param name="configuration">The host's configuration.</param>
    internal static IMessageRepository? Messages(IConfiguration configuration) =>
        PersistenceComposition.Shared(configuration).Postgres?.Messages;

    /// <summary>How many due messages one sweep takes, as this deployment configures it.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <remarks>
    /// A non-numeric or non-positive setting falls back to the documented default rather than
    /// faulting the boot: a typo in an operations value must not take the API down, and a sweep that
    /// took zero messages would report a clean run for an inbox filling up behind it.
    /// </remarks>
    internal static int BatchLimit(IConfiguration configuration) =>
        int.TryParse(
            configuration[BatchLimitKey],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configured) && configured > 0
            ? configured
            : InboxExpirySchedule.DefaultBatchLimit;
}

/// <summary>The nightly expiry sweep, on the authored daily boundary.</summary>
/// <remarks>
/// <para>
/// The service owns WHEN; <see cref="InboxExpiryJob"/> owns what. It sleeps to the next 05:00 UTC,
/// sweeps until a sweep comes back short of its batch limit, and sleeps again — so a night with more
/// due messages than one batch holds is cleared in one night rather than one batch per day.
/// </para>
/// <para>
/// A failed sweep is logged and the loop continues to the next boundary: a message whose expiry
/// could not be processed is still due tomorrow, and a service that stopped on the first fault would
/// silently leave every later night unswept.
/// </para>
/// </remarks>
public sealed class InboxExpiryLifecycle : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<InboxExpiryLifecycle> _logger;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private bool _stopped;

    /// <summary>Builds the lifecycle over the host's configuration.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="environment">The host's environment, for the content root.</param>
    /// <param name="logger">The host's logger.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public InboxExpiryLifecycle(
        IConfiguration configuration, IHostEnvironment environment, ILogger<InboxExpiryLifecycle> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var persistence = PersistenceComposition.Shared(_configuration);

        if (persistence.Postgres is not { } postgres || persistence.Players is not { } players)
        {
            _logger.LogInformation(
                "No inbox expiry sweep: this process has no message store, so nothing can expire.");

            return Task.CompletedTask;
        }

        var backbone = GameBackbone.Shared(_configuration, _environment);
        var job = new InboxExpiryJob(
            postgres.Messages,
            players,
            ObservabilityComposition.AnalyticsSink(_configuration),
            backbone.Content);

        var limit = InboxComposition.BatchLimit(_configuration);

        _logger.LogInformation(
            "{Marker}: {Time} UTC daily, {Limit} messages per batch.",
            InboxComposition.SweepScheduledMarker,
            GameCalendarText(),
            limit);

        _loop = Task.Run(() => RunAsync(job, backbone.Clock.UtcNow, limit, _stop.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;

        if (_loop is { } loop)
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await loop.ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private async Task RunAsync(
        InboxExpiryJob job, DateTimeOffset startedAtUtc, int limit, CancellationToken ct)
    {
        var next = InboxExpirySchedule.NextSweepAfter(startedAtUtc);

        while (!ct.IsCancellationRequested)
        {
            var wait = next - DateTimeOffset.UtcNow;

            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            await SweepUntilShortAsync(job, next, limit, ct).ConfigureAwait(false);

            next = InboxExpirySchedule.NextSweepAfter(next);
        }
    }

    private async Task SweepUntilShortAsync(
        InboxExpiryJob job, DateTimeOffset asOfUtc, int limit, CancellationToken ct)
    {
        try
        {
            InboxSweep sweep;

            do
            {
                sweep = await job.SweepAsync(asOfUtc, limit, ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Inbox expiry sweep: {Examined} due, {Granted} auto-granted, {Deleted} removed.",
                    sweep.Examined, sweep.AutoGranted, sweep.Deleted);

                foreach (var (message, refusal) in sweep.Withholdings)
                {
                    // Warning, not information: a reward that was owed and could not be paid is the
                    // one outcome of this job somebody has to act on.
                    _logger.LogWarning(
                        "Inbox expiry removed {Message} WITHOUT paying it: {Refusal}.",
                        message, refusal);
                }
            }
            while (sweep.Examined == limit && !ct.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down. Whatever is still due is still due at the next boundary.
        }
#pragma warning disable CA1031 // A sweep that faults must not stop every later night's sweep.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(exception, "The inbox expiry sweep failed; it will run again tomorrow.");
        }
    }

    private static string GameCalendarText() =>
        SlayIdleRepeat.Core.Primitives.GameCalendar.DayStart.ToString("hh\\:mm", CultureInfo.InvariantCulture);
}

/// <summary>The inbox area's one <c>Program.cs</c> line.</summary>
public static class InboxHostComposition
{
    /// <summary>Registers the nightly expiry sweep.</summary>
    /// <param name="builder">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static WebApplicationBuilder AddInbox(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHostedService(services => new InboxExpiryLifecycle(
            builder.Configuration,
            builder.Environment,
            services.GetRequiredService<ILogger<InboxExpiryLifecycle>>()));

        return builder;
    }
}
