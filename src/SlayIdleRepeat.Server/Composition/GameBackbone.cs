using System.Globalization;
using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Server;
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
/// <c>game-data</c> under the content root), and a deployment without one must still boot and answer
/// <c>GET /health</c>; the first command on such a host faults loudly, and a later call retries
/// rather than replaying the first fault forever.
/// </para>
/// <para>
/// ⚠️ The container image WAS such a deployment for the whole of M5 — its Dockerfile copied
/// <c>src/</c> and nothing else — so every endpoint below <c>/health</c> faulted on the compose
/// stack and only an unobserved CI job would have said so. The image now carries the content set;
/// this paragraph stays because the lazy build is what made that a first-request fault rather than
/// a boot failure, which is why it went unnoticed.
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
        UnitOfWork = persistence.UnitOfWork;

        // M5-09: the shelf of published bundles, and the pins that decide which content a command
        // is judged against. Console.Error carries the [content-bundles] and [content-pin] markers
        // for the same reason the remote-config warn sink does — they must be greppable in the
        // container's log stream, and a degraded shelf is exactly what nobody notices otherwise.
        var configuredBundleRoot = configuration["Content:BundleRoot"];
        BundleRoot = string.IsNullOrWhiteSpace(configuredBundleRoot) || Path.IsPathRooted(configuredBundleRoot)
            ? configuredBundleRoot
            : Path.Combine(environment.ContentRootPath, configuredBundleRoot);

        Bundles = new ContentBundleStore(BundleRoot, Content, Console.Error.WriteLine);
        Bundles.Publish();

        ContentPins = new ContentPinning(
            persistence.ContentPins, Content, ResolvePinnedSnapshot, Console.Error.WriteLine);

        SweepRetiredBundles(persistence.ContentPins);

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

    /// <summary>The one boundary a processed command commits inside.</summary>
    public IUnitOfWork UnitOfWork { get; }

    /// <summary>The shelf behind <c>GET /content/{version}</c>.</summary>
    public ContentBundleStore Bundles { get; }

    /// <summary>Where the bundle shelf lives, or <c>null</c> when retention is off.</summary>
    public string? BundleRoot { get; }

    /// <summary>The run/session pins and the snapshots they resolve to.</summary>
    public ContentPinning ContentPins { get; }

    /// <summary>Drops the bundles nothing is pinned to any more, once per process start.</summary>
    /// <remarks>
    /// ⚠️ <b>The only thing that runs a sweep today is a deploy.</b> There is no scheduler here — no
    /// timer, no background service — so a process that stays up for a month retains everything
    /// written during it. That is deliberate rather than forgotten: a deploy is when a new version
    /// arrives and therefore when an old one becomes retirable, and a periodic sweep on a host that
    /// publishes nothing new would only re-decide the same answer. It also makes the operation
    /// observable, since it happens where the boot log already is.
    /// <para>
    /// Never fatal. Retention is bookkeeping; a sweep that cannot read its references or cannot
    /// delete a file must leave the server serving content, not refuse to start.
    /// </para>
    /// </remarks>
    private void SweepRetiredBundles(IContentPinStore pins)
    {
        try
        {
            var referenced = pins.ListRetainedAsync(CancellationToken.None)
                .GetAwaiter().GetResult()
                .Select(retained => retained.Version)
                .ToArray();

            Bundles.Sweep(Clock.UtcNow, referenced);
        }
        // 🔒 Every exception, not a named few. The pin store this reads is Postgres-backed on a
        // configured deployment, and a database that is down answers with an NpgsqlException — which
        // derives from DbException, not from any of the three types a narrower filter would name. It
        // would escape into the backbone's constructor and the "never fatal" above would be false
        // for the one store this actually runs against in production.
        catch (Exception fault)
        {
            Console.Error.WriteLine(
                "[content-bundles] the retention sweep did not run (" + fault.Message + "). Nothing " +
                "was deleted, so every retained version is still servable; the shelf will simply " +
                "keep growing until a later start sweeps it.");
        }
    }

    private readonly object _pinnedGate = new();
    private readonly Dictionary<string, ContentSnapshot?> _pinnedSnapshots = new(StringComparer.Ordinal);

    /// <summary>An older pinned version's snapshot, unpacked once and kept.</summary>
    /// <remarks>
    /// Memoised because this is on the path of every command on a pinned run, and resolving means
    /// gunzipping the whole content set, parsing it and re-hashing it. A miss is cached too: a
    /// version whose bundle has been swept must not be re-read from disk once per command for the
    /// rest of that run's life. The map is bounded by what the shelf retains, which the sweep keeps
    /// small.
    /// </remarks>
    private ContentSnapshot? ResolvePinnedSnapshot(ContentVersion version)
    {
        lock (_pinnedGate)
        {
            if (_pinnedSnapshots.TryGetValue(version.Value, out var known))
            {
                return known;
            }

            var resolved = Bundles.TryRead(version) is { } bundle
                ? ContentBundle.Open(bundle, version)
                : null;

            _pinnedSnapshots[version.Value] = resolved;

            return resolved;
        }
    }

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
