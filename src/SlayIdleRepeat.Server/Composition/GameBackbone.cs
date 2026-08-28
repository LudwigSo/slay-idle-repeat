using System.Globalization;
using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The state every functional area shares: the content set, the world store, the command ledger,
/// the ambient adapters and the boot-resolved ambience — built once per process, on first use.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 An area that needs any of these takes them FROM HERE, never news its own: the query surface
/// (M5-07) must read the very rows the command endpoints write, and a second
/// <see cref="PlaceholderVolatileWorldStore"/> would be a disjoint world where POSTed runs are
/// invisible to GET with nothing going red. Area-specific seams (a throttle, a principal resolver)
/// stay in the area's own composition file.
/// </para>
/// <para>
/// What is named here, with its expiry: the real ambient adapters (<see cref="SystemClock"/>,
/// <see cref="SystemIdGenerator"/>) and the local-file content source, which stay; the world store
/// and command ledger from <see cref="PersistenceComposition"/> (Postgres-backed when configured,
/// volatile otherwise); <see cref="LocalHostAmbience"/>'s remaining named absence (M5-06 resolves
/// the entitlement per player); and the flags' live source, <see cref="RemoteConfigSource"/>
/// (M5-10) — its warn sink is a bare stderr write until M5-11's telemetry lands.
/// </para>
/// <para>
/// Built lazily WITHOUT caching a failure: the content set lives at <c>GameData:Root</c> (default
/// <c>game-data</c> under the content root), and a deployment without one — today's container
/// image — must still boot and answer <c>GET /health</c>; the first command on such a host faults
/// loudly, and a later call retries rather than replaying the first fault forever.
/// </para>
/// <para>
/// A process-wide singleton rather than a per-<c>WebApplication</c> value because the process
/// hosts exactly one application; there is deliberately no DI container to hang it on (O15).
/// </para>
/// </remarks>
public sealed class GameBackbone
{
    private static readonly object InitializationGate = new();
    private static GameBackbone? _shared;

    private GameBackbone(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredRoot = configuration["GameData:Root"] ?? "game-data";
        var dataRoot = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);

        Content = ContentLoader.Load(new LocalFileContentSource(dataRoot)).Require();
        Clock = new SystemClock();
        Ids = new SystemIdGenerator();

        // M5-05: the stores come from the persistence composition — Postgres-backed (with the Redis
        // hot cache) when ConnectionStrings:Postgres is configured, the volatile placeholders when
        // it is not.
        var persistence = PersistenceComposition.Shared(configuration);
        WorldStore = new WorldSliceStore(persistence.WorldRows);
        Ledger = persistence.Ledger;

        Entitlements = LocalHostAmbience.NoSubscriptionResolved();

        var configuredConfigPath = configuration["RemoteConfig:Path"];
        var configPath = string.IsNullOrEmpty(configuredConfigPath) || Path.IsPathRooted(configuredConfigPath)
            ? configuredConfigPath
            : Path.Combine(environment.ContentRootPath, configuredConfigPath);

        // Console.Error keeps the [remote-config] marker greppable in the container's log stream;
        // the real telemetry sink is M5-11's.
        RemoteConfig = new RemoteConfigSource(configPath, Console.Error.WriteLine);

        // Non-numeric or non-positive falls back to the documented 60: a typo here must not fault
        // the boot (the source refuses a non-positive interval loudly rather than looping never).
        var reloadSeconds = int.TryParse(
            configuration["RemoteConfig:ReloadSeconds"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configured) && configured > 0
            ? configured
            : 60;
        RemoteConfig.EnsureReloadLoopStarted(TimeSpan.FromSeconds(reloadSeconds));
    }

    /// <summary>The loaded, validated content set every command and query reads.</summary>
    public ContentSnapshot Content { get; }

    /// <summary>The one store the players' rows live in.</summary>
    public WorldSliceStore WorldStore { get; }

    /// <summary>The one sequencing/idempotency ledger.</summary>
    public ICommandLedgerStore Ledger { get; }

    /// <summary>The real clock.</summary>
    public IClockPort Clock { get; }

    /// <summary>The real identity generator.</summary>
    public IIdGeneratorPort Ids { get; }

    /// <summary>The subscription entitlement nothing has resolved yet.</summary>
    public Entitlements Entitlements { get; }

    /// <summary>The reloading flags document behind <c>GET /config</c> and the kill switches.</summary>
    public RemoteConfigSource RemoteConfig { get; }

    /// <summary>The kill switches' live read — what the source's last accepted document says right now.</summary>
    public FeatureFlags Flags => RemoteConfig.Current;

    /// <summary>The process's backbone, built on first call. A failed build is retried, never cached.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="environment">The host's environment, for the content root.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static GameBackbone Shared(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (Volatile.Read(ref _shared) is { } built)
        {
            return built;
        }

        lock (InitializationGate)
        {
            // A throw leaves the field null, so the next command retries the build instead of
            // replaying a transient first fault for the life of the process.
            return _shared ??= new GameBackbone(configuration, environment);
        }
    }
}
