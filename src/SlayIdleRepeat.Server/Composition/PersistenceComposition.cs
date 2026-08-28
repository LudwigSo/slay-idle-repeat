using SlayIdleRepeat.Adapters.Cache.Redis;
using SlayIdleRepeat.Adapters.ObjectStore.S3;
using SlayIdleRepeat.Adapters.Persistence.Postgres;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The persistence area's composition: which stores this process runs on, decided once from
/// configuration — the M5-05 replacement for the volatile placeholders.
/// </summary>
/// <remarks>
/// <para>
/// With <c>ConnectionStrings:Postgres</c> configured, the world rows and the command ledger run on
/// the Postgres-authoritative stores — through the ports, never past them — with the Redis hot
/// cache decorating them when <c>ConnectionStrings:Redis</c> is configured too. Without it, the
/// volatile placeholders stand exactly as before: local tooling and the unit tier boot no
/// infrastructure, ever.
/// </para>
/// <para>
/// The battle-log store exists when the <c>ObjectStore:*</c> group is configured, independent of
/// the database: it has no producer yet, but constructing it here is what the compose-boot probe
/// asserts, so a broken binding is caught now rather than by the first battle.
/// </para>
/// <para>
/// A process-wide singleton for <c>GameBackbone</c>'s reason: the backbone reads its stores from
/// here, and <see cref="PersistenceLifecycle"/> migrates and drains the SAME instances — two
/// resolutions would be two worlds. Built lazily without caching a failure, also the backbone's
/// rule.
/// </para>
/// </remarks>
public sealed class PersistenceComposition : IAsyncDisposable
{
    private static readonly object InitializationGate = new();
    private static PersistenceComposition? _shared;

    /// <summary>How many battle logs may wait for the drain. A server-operations constant, not a game tunable: at one log per battle, 1024 is minutes of full outage before the first loss.</summary>
    private const int BattleLogQueueCapacity = 1024;

    private readonly RedisVolatileByteCache? _redisCache;
    private readonly S3BattleLogStore? _s3Store;

    private PersistenceComposition(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        var runTtl = TimeSpan.FromHours(configuration.GetValue("Cache:RunStateTtlHours", 48));

        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                WorldRows = new PlaceholderVolatileWorldStore();
                Ledger = new VolatileCommandLedger();
            }
            else
            {
                Postgres = PostgresPersistence.Create(connectionString, runTtl);

                var redisConfiguration = configuration.GetConnectionString("Redis");
                IRunStateStore runs = Postgres.RunStates;
                IIdempotencyStore idempotency = Postgres.Idempotency;

                if (!string.IsNullOrWhiteSpace(redisConfiguration))
                {
                    _redisCache = RedisVolatileByteCache.Connect(redisConfiguration);
                    CacheFailures = new CacheFailureCounter();
                    runs = new RedisRunStateCache(_redisCache, runs, CacheFailures);
                    idempotency = new RedisIdempotencyCache(_redisCache, idempotency, CacheFailures);
                }

                Players = Postgres.Players;
                RunStates = runs;
                Idempotency = idempotency;
                WorldRows = new RepositoryWorldRows(Postgres.Players, runs, runTtl);
                Ledger = new DurableCommandLedger(idempotency, runTtl);
            }

            if (configuration["ObjectStore:ServiceUrl"] is { Length: > 0 } serviceUrl)
            {
                BattleLogLosses = new BattleLogLossCounter();
                _s3Store = S3BattleLogStore.Create(new S3ObjectStoreOptions(
                    serviceUrl,
                    configuration["ObjectStore:Region"] ?? "us-east-1",
                    configuration.GetValue("ObjectStore:ForcePathStyle", false),
                    configuration["ObjectStore:AccessKey"] ?? string.Empty,
                    configuration["ObjectStore:SecretKey"] ?? string.Empty,
                    configuration["ObjectStore:BattleLogBucket"] ?? string.Empty));
                BattleLogQueue = new QueuedBattleLogStore(_s3Store, BattleLogQueueCapacity, BattleLogLosses);
                BattleLogs = BattleLogQueue;
            }
        }
        catch
        {
            // A failed build is retried, never cached — so whatever this attempt already opened
            // must close now, or every retry leaks another connection pool.
            _redisCache?.Dispose();
            _s3Store?.Dispose();
            Postgres?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>The byte store the backbone's <c>WorldSliceStore</c> runs over.</summary>
    public ILocalCachePort WorldRows { get; }

    /// <summary>The sequencing/idempotency ledger the backbone hands the gateway.</summary>
    public ICommandLedgerStore Ledger { get; }

    // ⚠️ The six below have NO reader in this build — they are the handles the next tasks compose
    // from, each named with its owner so a reader can tell "unused" from "abandoned" (steering
    // S25's corollary: the seams are grepped, and these come back with nothing but their own
    // assignment). Everything this process actually runs on goes through WorldRows and Ledger.

    /// <summary>The player store, or <c>null</c> on a volatile (no-database) process. Composed by the unit-of-work task (M5-04).</summary>
    public IPlayerRepository? Players { get; }

    /// <summary>The run store (cache-decorated when Redis is configured), or <c>null</c> on a volatile process. Composed by M5-04.</summary>
    public IRunStateStore? RunStates { get; }

    /// <summary>The idempotency store (cache-decorated when Redis is configured), or <c>null</c> on a volatile process. Composed by M5-04.</summary>
    public IIdempotencyStore? Idempotency { get; }

    /// <summary>The battle-log store behind its write-behind queue, or <c>null</c> when no object store is configured. Its first producer is the battle milestone's.</summary>
    public IBattleLogStore? BattleLogs { get; }

    /// <summary>Absorbed cache-failure count, when the cache layer exists. Read by the telemetry task (M5-11).</summary>
    public CacheFailureCounter? CacheFailures { get; }

    /// <summary>Dropped battle-log count, when the store exists. Read by M5-11.</summary>
    public BattleLogLossCounter? BattleLogLosses { get; }

    /// <summary>The queue the lifecycle drains, when the store exists.</summary>
    internal QueuedBattleLogStore? BattleLogQueue { get; }

    /// <summary>The Postgres door, for the lifecycle's migration run. <c>null</c> on a volatile process.</summary>
    internal PostgresPersistence? Postgres { get; }

    /// <summary>The process's persistence, built on first call. A failed build is retried, never cached.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    public static PersistenceComposition Shared(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (Volatile.Read(ref _shared) is { } built)
        {
            return built;
        }

        lock (InitializationGate)
        {
            return _shared ??= new PersistenceComposition(configuration);
        }
    }

    /// <summary>Closes everything this composition opened. The lifecycle calls it at host shutdown, after the drain has stopped.</summary>
    public async ValueTask DisposeAsync()
    {
        _redisCache?.Dispose();
        _s3Store?.Dispose();

        if (Postgres is { } postgres)
        {
            await postgres.DisposeAsync().ConfigureAwait(false);
        }

        lock (InitializationGate)
        {
            if (ReferenceEquals(_shared, this))
            {
                _shared = null;
            }
        }
    }
}
