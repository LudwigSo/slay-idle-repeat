namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The persistence area's host hooks: migrations before the first request, the battle-log drain for
/// the process's lifetime.
/// </summary>
/// <remarks>
/// <para>
/// Migrations run in <see cref="StartAsync"/>, under the adapter's advisory lock, so the host does
/// not serve until the schema is the one this build ships — and two instances starting together
/// apply it once. A volatile (no-database) process starts with neither hook and stays exactly what
/// it was before this area existed.
/// </para>
/// <para>
/// The startup marker line below is load-bearing: the compose-boot job's object-store probe greps
/// the API log for it, because no production path writes a battle log yet and the probe's whole
/// claim is "the adapter was constructed against the live store and its drain is running".
/// </para>
/// </remarks>
public sealed class PersistenceLifecycle : IHostedService
{
    /// <summary>The exact text the object-store probe greps for.</summary>
    internal const string BattleLogStoreReadyMarker = "Battle-log store ready: queued drain running";

    private readonly IConfiguration _configuration;
    private readonly ILogger<PersistenceLifecycle> _logger;
    private readonly CancellationTokenSource _stopDrain = new();
    private PersistenceComposition? _started;
    private Task? _drain;
    private bool _stopped;

    /// <summary>Builds the lifecycle over the host's configuration.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="logger">The host's logger.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public PersistenceLifecycle(IConfiguration configuration, ILogger<PersistenceLifecycle> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var persistence = _started = PersistenceComposition.Shared(_configuration);

        if (persistence.Postgres is { } postgres)
        {
            await postgres.MigrateAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Postgres migrations applied; the schema is this build's.");
        }

        if (persistence.BattleLogQueue is { } queue)
        {
            _drain = Task.Run(() => queue.RunAsync(_stopDrain.Token), CancellationToken.None);
            _logger.LogInformation(BattleLogStoreReadyMarker);
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;

        if (_drain is { } drain)
        {
            await _stopDrain.CancelAsync().ConfigureAwait(false);
            await drain.ConfigureAwait(false);
        }

        _stopDrain.Dispose();

        // The drain is down, so the stores it uploads through can close cleanly. The instance
        // StartAsync built, never a fresh Shared() call: Shared rebuilds after a dispose, so
        // shutting down a host that never started would OPEN a connection pool in order to close
        // one.
        if (_started is { } persistence)
        {
            await persistence.DisposeAsync().ConfigureAwait(false);
            _started = null;
        }
    }
}

/// <summary>The persistence area's one <c>Program.cs</c> line.</summary>
public static class PersistenceHostComposition
{
    /// <summary>Registers the persistence lifecycle: migrations at startup, the battle-log drain for the process's lifetime.</summary>
    /// <param name="builder">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static WebApplicationBuilder AddPersistence(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHostedService(services => new PersistenceLifecycle(
            builder.Configuration, services.GetRequiredService<ILogger<PersistenceLifecycle>>()));

        return builder;
    }
}
