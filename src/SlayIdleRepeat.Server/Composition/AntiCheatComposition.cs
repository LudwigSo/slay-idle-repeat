using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Application.Moderation;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>The per-player application-level limit's deployment contract.</summary>
/// <remarks>
/// ⚠️ Both numbers are OPERATIONAL defaults chosen by whoever runs the service. Neither is a game
/// tunable, neither lives in the shared content data, and neither is derived from any design text —
/// no document authors a per-endpoint request rate. They exist so an unconfigured process is limited
/// rather than unlimited; a deployment overrides them with <c>RateLimit__Player__*</c>.
/// </remarks>
public sealed class PlayerRateLimitOptions
{
    /// <summary>Sustained commands per second from one account. Default 5.</summary>
    public int SustainedPerSecond { get; set; } = RateLimitPolicy.PerPlayerPermitsPerSecond;

    /// <summary>How many commands one account may issue back to back. Default 20.</summary>
    public int Burst { get; set; } = RateLimitPolicy.PerPlayerBurst;
}

/// <summary>The plausibility monitor's deployment contract.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every threshold is absent by default, and absent means unauthored.</b> The sweep is also
/// off by default — two independent switches, because they say different things: off means nobody
/// is running it, and unauthored means nobody has written down what implausible looks like. A
/// deployment that turns the sweep on without authoring a threshold gets a job that observes,
/// stores and flags nothing, and is told so at startup rather than left to assume otherwise.
/// </para>
/// <para>
/// ⚠️ The interval is an operational default like the rate limits. The thresholds are not defaults
/// at all — there is nothing to default them to.
/// </para>
/// </remarks>
public sealed class PlausibilityOptions
{
    /// <summary>Whether the background sweep runs at all. Default <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long between sweeps, in minutes. Default 60.</summary>
    public int SweepIntervalMinutes { get; set; } = 60;

    /// <summary>Wallet currency per day above which an account is flagged. Absent = unauthored.</summary>
    public long? MaxCurrencyPerDay { get; set; }

    /// <summary>Legend XP per day above which an account is flagged. Absent = unauthored.</summary>
    public long? MaxLegendXpPerDay { get; set; }

    /// <summary>Battle-hash mismatches per day above which an account is flagged. Absent = unauthored.</summary>
    public long? MaxBattleHashMismatchesPerDay { get; set; }

    /// <summary>The envelope these thresholds describe.</summary>
    public PlausibilityEnvelope ToEnvelope() =>
        new(MaxCurrencyPerDay, MaxLegendXpPerDay, MaxBattleHashMismatchesPerDay);
}

/// <summary>
/// The anti-cheat functional area: the two rate limits, the plausibility sweep, and the
/// account-standing snapshot the request path reads.
/// </summary>
/// <remarks>
/// <para>
/// One area rather than two because the two halves share a configuration section, a startup
/// announcement and a background pass — but the two <em>limits</em> inside it are kept visibly
/// apart, and must stay that way: the per-player limit answers a 200 rejection envelope through the
/// command gateway's throttle seam, and the per-address limit answers HTTP 429 from the middleware
/// pipeline. See <see cref="PerIpRateLimit"/> for why collapsing them would be wrong.
/// </para>
/// <para>
/// The background work is a hosted service in this same container — no vendor trigger, no function
/// runtime, nothing outside the process this composes.
/// </para>
/// </remarks>
public static class AntiCheatComposition
{
    private static readonly object InitializationGate = new();
    private static AntiCheatArea? _shared;

    /// <summary>Registers the per-address rate limiter and the plausibility background service.</summary>
    /// <param name="builder">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static WebApplicationBuilder AddAntiCheat(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var area = Shared(builder.Configuration);

        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                http => RateLimitPartition.GetTokenBucketLimiter(
                    PerIpRateLimit.PartitionKeyFor(http), _ => PerIpRateLimit.BucketFor(area.Ip)));

            limiter.OnRejected = (context, _) =>
            {
                PerIpRateLimit.WriteRefusal(context.HttpContext.Response, area.Ip);

                return ValueTask.CompletedTask;
            };
        });

        if (area.Plausibility.Enabled)
        {
            builder.Services.AddHostedService(_ => new PlausibilityLoop(area));
        }

        Announce(area);

        return builder;
    }

    /// <summary>Adds the area's middleware: the per-address limiter, in front of everything it protects.</summary>
    /// <param name="app">The composed host.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <remarks>
    /// Ordering, declared once: after the observability area's exception middleware — a refusal here
    /// is an ordinary answer and must still be traced — and before every endpoint, since an address
    /// past its limit should cost no routing, no authentication and no database read.
    /// </remarks>
    public static WebApplication UseAntiCheat(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseRateLimiter();

        return app;
    }

    /// <summary>The one per-player limiter this process throttles commands with.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    internal static ICommandThrottle Throttle(IConfiguration configuration) =>
        Shared(configuration).Throttle;

    /// <summary>Wraps a principal resolver with this process's account-standing check.</summary>
    /// <param name="inner">The resolver that decides who is calling.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal static IPrincipalResolver WithAccountStanding(
        IPrincipalResolver inner, IConfiguration configuration) =>
        new SanctionAwarePrincipalResolver(inner, Shared(configuration).Standing);

    /// <summary>The area's shared state, built on first call — the same pattern the other areas use.</summary>
    private static AntiCheatArea Shared(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (Volatile.Read(ref _shared) is { } built)
        {
            return built;
        }

        lock (InitializationGate)
        {
            return _shared ??= new AntiCheatArea(configuration);
        }
    }

    /// <summary>
    /// Says out loud what this deployment's anti-cheat actually does, because every quiet state here
    /// is indistinguishable from a working one.
    /// </summary>
    private static void Announce(AntiCheatArea area)
    {
        if (!area.Plausibility.Enabled)
        {
            Log.Information(
                "Plausibility monitoring is DISABLED (Plausibility:Enabled=false): no account "
                + "trajectory is observed and no review entry can be raised by this process");

            return;
        }

        if (area.Envelope.AuthoredThresholds == 0)
        {
            Log.Warning(
                "Plausibility monitoring is ENABLED with NO authored threshold: it will observe "
                + "every account and flag nothing. Author Plausibility:MaxCurrencyPerDay, "
                + "Plausibility:MaxLegendXpPerDay or Plausibility:MaxBattleHashMismatchesPerDay "
                + "from measured production data — no design document states one, and none is "
                + "defaulted");
        }

        // The one an operator would otherwise mistake for a clean bill of health: a sweep reporting
        // "observed 0 accounts" looks exactly like a sweep reporting that nobody is cheating.
        Log.Warning(
            "The plausibility sweep will observe ZERO accounts and its findings are VOLATILE: no "
            + "adapter enumerates player rows, so the in-process store this composes has nothing to "
            + "read, and every observation and review-queue entry it records is lost when this "
            + "process stops. Thresholds authored today cannot flag anything until a durable "
            + "IModerationStore implementation exists");
    }

    /// <summary>
    /// The area's composed state: everything this deployment's configuration decides, in one object.
    /// </summary>
    /// <remarks>
    /// A named type rather than a closure over <see cref="Shared"/>, so that what the configuration
    /// composes can be exercised without the process-wide cache in front of it — a singleton keyed on
    /// nothing answers the first caller's configuration to every later one.
    /// </remarks>
    internal sealed class AntiCheatArea
    {
        /// <summary>Composes the area from one deployment's configuration.</summary>
        /// <param name="configuration">The host's configuration.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
        internal AntiCheatArea(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var player = configuration.GetSection("RateLimit:Player").Get<PlayerRateLimitOptions>()
                         ?? new PlayerRateLimitOptions();

            Ip = configuration.GetSection("RateLimit:Ip").Get<PerIpRateLimitOptions>()
                 ?? new PerIpRateLimitOptions();

            Plausibility = configuration.GetSection("Plausibility").Get<PlausibilityOptions>()
                           ?? new PlausibilityOptions();

            // Both checked HERE rather than where they are used, so a deployment typo is a process
            // that refuses to start instead of one that looks healthy and throws out of the rate
            // limiter on its first request — or out of OnRejected on its first refusal.
            _ = PerIpRateLimit.BucketFor(Ip);
            PerIpRateLimit.RequireBackoff(Ip);

            SweepInterval = SweepIntervalOf(Plausibility.SweepIntervalMinutes);
            Envelope = Plausibility.ToEnvelope();
            Clock = new SystemClock();
            Store = new VolatileModerationStore();
            Standing = new AccountStandingSnapshot();

            Throttle = new PlayerRateLimiter(
                Clock, RateLimitPolicy.PerSecond(player.SustainedPerSecond, player.Burst));

            Sweep = new PlausibilitySweep(Store, Clock, new SystemIdGenerator(), Envelope);
        }

        internal PerIpRateLimitOptions Ip { get; }

        internal PlausibilityOptions Plausibility { get; }

        /// <summary>How long between sweeps, checked once here so the timer cannot refuse it later.</summary>
        internal TimeSpan SweepInterval { get; }

        internal PlausibilityEnvelope Envelope { get; }

        internal IClockPort Clock { get; }

        internal IModerationStore Store { get; }

        internal AccountStandingSnapshot Standing { get; }

        internal ICommandThrottle Throttle { get; }

        internal PlausibilitySweep Sweep { get; }

        /// <summary>
        /// The configured interval, refused rather than coerced. A zero or negative one silently
        /// rewritten to "a minute" is a hole filled with a plausible value; one above a day is a
        /// timer the host cannot construct, so it would fail to start with no idea why.
        /// </summary>
        private static TimeSpan SweepIntervalOf(int minutes)
        {
            const int LongestSweepIntervalMinutes = 24 * 60;

            if (minutes is <= 0 or > LongestSweepIntervalMinutes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PlausibilityOptions.SweepIntervalMinutes),
                    minutes,
                    "Plausibility:SweepIntervalMinutes is a count of minutes between one and a "
                    + "day's worth. Outside that the background timer is either impossible or one "
                    + "the host refuses to build, and neither says so where an operator would look.");
            }

            return TimeSpan.FromMinutes(minutes);
        }
    }

    /// <summary>
    /// The background work: a hosted service in this same container — no vendor trigger, no function
    /// runtime, nothing outside the process this composes.
    /// </summary>
    private sealed class PlausibilityLoop(AntiCheatArea area) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Off the startup thread before anything else: every call below completes synchronously
            // against the in-process store, so without this the whole first sweep would run inside
            // StartAsync — and would block it outright once a durable store does real I/O.
            await Task.Yield();

            using var timer = new PeriodicTimer(area.SweepInterval);

            // The first pass runs at startup rather than one interval later, so a process that is
            // restarted more often than the interval still refreshes the locked-account set.
            await PassAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    await PassAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
        }

        // An unhandled exception out of ExecuteAsync stops the whole host, and a backstop that is
        // allowed to find nothing is certainly allowed to fail: a moderation outage must never take
        // the game down with it.
        private async Task PassAsync(CancellationToken ct)
        {
            try
            {
                await area.Sweep.RefreshStandingAsync(area.Standing, ct).ConfigureAwait(false);

                var result = await area.Sweep.RunOnceAsync(ct).ConfigureAwait(false);

                Log.Information(
                    "Plausibility sweep observed {Accounts} accounts, measured {Deltas} and raised "
                    + "{Flags} review entries",
                    result.AccountsObserved,
                    result.DeltasMeasured,
                    result.FlagsRaised);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown interrupted this pass.
            }
            catch (Exception failure)
            {
                Log.Warning(failure, "The plausibility sweep failed; this pass observed nothing");
            }
        }
    }
}
