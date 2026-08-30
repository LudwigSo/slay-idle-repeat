using SlayIdleRepeat.Adapters.Ads.AutoGrant;
using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Adapters.Api.Http;
using SlayIdleRepeat.Adapters.Cache.LocalFile;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Adapters.Content.Packed;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// Which rewarded-ad arm the composition root picked, named rather than inferred.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Both arms resolve to the same adapter type today, so "which adapter did you get"
/// cannot tell them apart and a test asserting the type alone would pass whichever arm
/// ran. The arm is therefore carried as a value: it is the decision, and the decision is
/// what is worth pinning.
/// </para>
/// </remarks>
public enum RewardedAdArm
{
    /// <summary>Plus is active, so the reward is already owed and no ad is shown for it.</summary>
    PlusAutoGrant = 1,

    /// <summary>
    /// ⚠️ No Plus, and no ad network built yet. This arm names the absence rather than
    /// pretending it is filled: <c>SlayIdleRepeat.Adapters.Ads.AppLovin</c> is the real
    /// implementation and it is an empty project until M15-04.
    /// </summary>
    NoAdNetworkResolved = 2,
}

/// <summary>
/// A rewarded-ad port together with the arm that produced it.
/// </summary>
public sealed class RewardedAdSelection
{
    /// <summary>Pairs an arm with the port it resolved to.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="port"/> is null.</exception>
    public RewardedAdSelection(RewardedAdArm arm, IRewardedAdPort port)
    {
        ArgumentNullException.ThrowIfNull(port);

        Arm = arm;
        Port = port;
    }

    /// <summary>The arm the composition root took.</summary>
    public RewardedAdArm Arm { get; }

    /// <summary>The port that arm resolved to.</summary>
    public IRewardedAdPort Port { get; }
}

/// <summary>
/// Which host arm the composition root picked, named rather than inferred.
/// </summary>
/// <remarks>
/// 🔒 Both arms resolve to the same host type today, so "which host did you get" cannot tell them
/// apart — the arm is carried as a value for the reason <see cref="RewardedAdArm"/> already states
/// about its own: the arm is the decision, and the decision is what is worth pinning.
/// </remarks>
public enum ClientArm
{
    /// <summary>The whole game runs in this process. No connection to lose, nothing to sync.</summary>
    InProcessLocalHost = 1,

    /// <summary>
    /// 🔴 The wire seam is live — session, content stamp, reconnect ladder and overlay all run
    /// against a server — and the presenters still drive the IN-PROCESS host, because no transport
    /// can answer with the aggregates the host's contract returns.
    /// </summary>
    ServerSessionWithLocalPresenterSurface = 2,
}

/// <summary>
/// The wire half of the graph: the two seams a server arm speaks through, and what they were built
/// over.
/// </summary>
/// <remarks>
/// 🔒 The adapters and the transport they share are disposed in that order — adapters first, then
/// the handler underneath them — because a handler torn down first turns an in-flight request into
/// a fault nobody asked for.
/// </remarks>
public sealed class ClientWireSeams : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _owned;

    /// <summary>Pairs the two seams with the disposables they were built over.</summary>
    /// <param name="api">The wire seam.</param>
    /// <param name="content">The content-distribution seam.</param>
    /// <param name="owned">What both were built over, disposed after them.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public ClientWireSeams(
        IGameApiPort api, IContentDistributionClient content, IReadOnlyList<IDisposable> owned)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owned);

        Api = api;
        Content = content;
        _owned = owned;
    }

    /// <summary>The wire seam the session and the reconnect ladder speak through.</summary>
    /// <remarks>
    /// 🔴 <b>No player command goes through it, and that is the whole shape of this arm.</b> The
    /// presenters drive the in-process host, so what reaches a server here is the sign-in and
    /// whatever the ladder asks for on a reconnect — never a move a player made. It carries commands
    /// the day the presenters submit through it, which is the migration <see cref="ClientArm"/>
    /// names.
    /// </remarks>
    public IGameApiPort Api { get; }

    /// <summary>The content-distribution seam the boot's sync stage speaks through.</summary>
    public IContentDistributionClient Content { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        (Api as IDisposable)?.Dispose();
        (Content as IDisposable)?.Dispose();

        foreach (var owned in _owned)
        {
            owned.Dispose();
        }
    }
}

/// <summary>
/// The object graph the client runs on, as the composition root hands it over.
/// </summary>
public sealed class ComposedClient : IDisposable
{
    /// <summary>Carries the composed graph. Built only by <see cref="ClientComposition"/>.</summary>
    /// <param name="gameHost">The in-process seam every presenter drives the game through.</param>
    /// <param name="rewardedAds">The rewarded-ad port with the arm that chose it.</param>
    /// <param name="revive">How a player reaches a revive.</param>
    /// <param name="content">The provider the host's snapshot was loaded from.</param>
    /// <param name="clock">The one clock the whole graph runs on.</param>
    /// <param name="arm">Which host arm this graph was composed on.</param>
    /// <param name="wire">The wire half, or null when this client was composed over no server.</param>
    /// <param name="connection">
    /// The connection presenter built over <paramref name="wire"/>, or null when there is none.
    /// The two are null together, by construction.
    /// </param>
    /// <exception cref="ArgumentNullException">Any non-optional part of the graph is null.</exception>
    public ComposedClient(
        ClientArm arm,
        IGameHost gameHost,
        RewardedAdSelection rewardedAds,
        ReviveArm revive,
        ContentProvider content,
        IClockPort clock,
        ClientWireSeams? wire,
        ConnectionPresenter? connection)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(rewardedAds);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(clock);

        Arm = arm;
        GameHost = gameHost;
        RewardedAds = rewardedAds;
        Revive = revive;
        Content = content;
        Clock = clock;
        Wire = wire;
        Connection = connection;
    }

    /// <summary>Which host arm this graph was composed on.</summary>
    public ClientArm Arm { get; }

    /// <summary>The single entry point the presenters drive the game through.</summary>
    public IGameHost GameHost { get; }

    /// <summary>The wire half, or <c>null</c> when this client was composed over no server.</summary>
    public ClientWireSeams? Wire { get; }

    /// <summary>
    /// The wire seam this client was composed over, or <c>null</c> when it was composed over none.
    /// </summary>
    /// <remarks>
    /// 🔒 Null is a decision, not an omission. A client composed over an in-process host has no
    /// connection to lose, so there is nothing for the reconnect ladder to climb and nothing for the
    /// overlay to draw — which is the specification's own rendering for a working connection: nothing
    /// at all. Held rather than dropped for the reason the whole graph is held: hand-rolled
    /// composition has no container, so a port nobody references is a port nobody can reach.
    /// </remarks>
    public IGameApiPort? GameApi => Wire?.Api;

    /// <summary>
    /// The one connection presenter the whole build shares, or <c>null</c> when no wire seam was
    /// composed.
    /// </summary>
    /// <remarks>
    /// 🔒 Null on the arm an exported build takes. <c>GodotClientComposition.ComposeClient</c> is the
    /// only production call into <see cref="ClientComposition.Compose"/> and it composes a wire seam
    /// only when the environment named a server, so a build without one instantiates no overlay and
    /// dims no control — the correct rendering for <em>Connected</em>. Where there is one, the root
    /// scene advances <see cref="ReconnectManager.PollAsync"/> through
    /// <see cref="ConnectionPump.Advance"/> every frame, which is what makes this state move.
    /// </remarks>
    public ConnectionPresenter? Connection { get; }

    /// <summary>
    /// The driver that advances the ladder each frame, or <c>null</c> on an arm with none.
    /// </summary>
    /// <remarks>
    /// Internal, and reached through <see cref="AppRootComposition.CreateConnectionPump"/>: the
    /// root scene asks a factory and is handed a driver or a null, the same shape the presenter
    /// factories have, so no scene ever reads machinery off the graph.
    /// </remarks>
    internal ConnectionPump? Pump { get; init; }

    /// <summary>The content sync the boot runs, or <c>null</c> on an arm with no server to ask.</summary>
    /// <remarks>Reached through <c>BootComposition</c>, for the reason <see cref="Pump"/> gives.</remarks>
    internal ContentSyncPresenter? ContentSync { get; init; }

    /// <summary>The session the boot opens, or <c>null</c> for the same reason.</summary>
    internal SessionOpener? Session { get; init; }

    /// <summary>The rewarded-ad port, with the arm that chose it.</summary>
    public RewardedAdSelection RewardedAds { get; }

    /// <summary>
    /// How a player reaches <c>02</c> §6's revive, resolved once here rather than at the screen.
    /// </summary>
    /// <remarks>
    /// 🔒 Carried as a decision for the same reason <see cref="RewardedAds"/> is: the Plus promise is
    /// an adapter swap chosen in a composition root and never a condition carried into the game
    /// (<c>12</c> §3.2), and <c>IsolationTests.No_entitlement_branch_outside_a_composition_root</c> makes
    /// that mechanical — a presenter cannot branch on the flag, so it is handed the outcome.
    /// </remarks>
    public ReviveArm Revive { get; }

    /// <summary>The provider the host's snapshot was loaded from, kept so the load is reachable.</summary>
    public ContentProvider Content { get; }

    /// <summary>
    /// The one clock the graph runs on, handed out so a screen measuring a soft timer measures
    /// against the same instant source the host stamps its commands with.
    /// </summary>
    /// <remarks>
    /// 🔒 Exposed rather than left inside the host, because the alternative is a screen calling
    /// <c>DateTimeOffset.UtcNow</c> — an ambient read that no case can drive, on the one part of the
    /// board a player experiences as a deadline.
    /// </remarks>
    public IClockPort Clock { get; }

    /// <summary>Tears the wire half down with the graph that owns it.</summary>
    public void Dispose() => Wire?.Dispose();
}

/// <summary>
/// The client-side composition root: the only place in the repository that names a
/// concrete client adapter, and the only place that branches on the entitlement.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Pure C#. Nothing here names an engine type, which is what makes the wiring
/// decisions — above all the entitlement branch — testable without booting anything.
/// The engine half resolves the two paths and hands them in.
/// </para>
/// <para>
/// Hand-rolled, with explicit factory methods and no container. A container would buy
/// nothing at this size and would fight the node lifecycle on the way in.
/// </para>
/// </remarks>
public static class ClientComposition
{
    /// <summary>
    /// Builds the whole client graph from two resolved absolute paths and the ambience the
    /// host was started with.
    /// </summary>
    /// <param name="cacheDirectoryPath">Absolute path of the writable directory the local profile lives in.</param>
    /// <param name="contentSource">
    /// Where the content set is read from — see <see cref="SelectContentSource"/>, which is what
    /// decides between a checkout on disk and a packed artefact.
    /// </param>
    /// <param name="entitlements">The resolved entitlement. Never inferred from a local receipt.</param>
    /// <param name="featureFlags">The resolved feature flags.</param>
    /// <param name="localeTag">
    /// The locale the device reported, as a BCP-47 tag. Needed here rather than at the screen because
    /// the connection presenter is built here and every word it shows is a catalogue lookup.
    /// </param>
    /// <param name="arm">
    /// 🔒 Which host arm this build resolves — <b>stated, never defaulted</b>. A root that cannot
    /// say which game it composed has not decided.
    /// </param>
    /// <param name="wire">
    /// 🔒 The wire half this client runs over, or <c>null</c> to say there is none — stated for the
    /// same reason <paramref name="arm"/> is.
    /// </param>
    /// <exception cref="ArgumentException">A path is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An ambience value is null.</exception>
    public static ComposedClient Compose(
        string cacheDirectoryPath,
        IContentSourcePort contentSource,
        Entitlements entitlements,
        FeatureFlags featureFlags,
        string localeTag,
        ClientArm arm,
        ClientWireSeams? wire)
    {
        // Every guard before anything touches a disk: a cache adapter creates its directory on
        // construction, so a root that validated as it went would leave a directory behind for a
        // call it then refused.
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectoryPath);
        ArgumentNullException.ThrowIfNull(contentSource);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(featureFlags);
        ArgumentNullException.ThrowIfNull(localeTag);

        // ⚠️ Canonical, not Shipping. The shipping gate fails while any translation sentinel
        // remains, and every DE value is one today — a shipping load here would refuse to start
        // the game rather than refuse to release it.
        var content = new ContentProvider(
            contentSource,
            ContentLoadOptions.Canonical,
            ContentReloadPolicy.Disabled);

        var rewardedAds = SelectRewardedAdPort(entitlements);

        // Built once and shared, rather than constructed at each use site: the host stamps commands
        // with it and a board screen times its prompt against it, and two clocks would be two
        // answers to "what time is it" in one process.
        var clock = new SystemClock();

        var gameHost = SelectGameHost(
            arm, new LocalFileCache(cacheDirectoryPath), clock, content.Current, entitlements, featureFlags);

        var connection = SelectConnectionHalf(wire, cacheDirectoryPath, content, localeTag, clock);

        return new ComposedClient(
            arm,
            gameHost,
            rewardedAds,
            SelectReviveArm(entitlements),
            content,
            clock,
            wire,
            connection.Presenter)
        {
            Pump = connection.Pump,
            Session = connection.Session,

            // Built here rather than at the boot screen because the sync is compared against the
            // snapshot this graph was composed over, and this is the only place that holds both.
            ContentSync = wire is null ? null : new ContentSyncPresenter(wire.Content, content.Current),
        };
    }

    /// <summary>The subdirectory of the writable cache root the server arm's mirror lives in.</summary>
    /// <remarks>
    /// 🔒 A directory of its own, so the mirror can be deleted wholesale — which is what "a caller
    /// may discard the whole of it at any moment" means — without touching the local profile the
    /// in-process host writes beside it.
    /// </remarks>
    public const string MirrorCacheDirectoryName = "cache";

    /// <summary>
    /// Chooses the host arm from the server address the engine half resolved, and from nothing else.
    /// </summary>
    /// <param name="serverBaseAddress">The address a developer switch named, or null/blank for none.</param>
    public static ClientArm SelectArm(string? serverBaseAddress) =>
        string.IsNullOrWhiteSpace(serverBaseAddress)
            ? ClientArm.InProcessLocalHost
            : ClientArm.ServerSessionWithLocalPresenterSurface;

    /// <summary>
    /// Chooses the host the presenters drive, from the arm and from nothing else.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Both arms resolve an in-process host, and the server arm's is a NAMED STAND-IN.</b>
    /// <see cref="IGameHost"/> answers with the domain's own aggregates, which no transport can
    /// honestly produce, so there is nowhere legal for a remote implementation of it to live. What
    /// the server arm actually resolves over HTTP is the wire seam beside this one; the presenter
    /// surface still plays locally, and <see cref="NoRemotePresenterSurfaceResolved"/> is where that
    /// gap is written down.
    /// </remarks>
    /// <param name="arm">The arm this build resolved.</param>
    /// <param name="cache">Where the local profile is written.</param>
    /// <param name="clock">The one clock the graph runs on.</param>
    /// <param name="content">The loaded content set the rules read their numbers from.</param>
    /// <param name="entitlements">The resolved entitlement.</param>
    /// <param name="featureFlags">The resolved feature flags.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public static IGameHost SelectGameHost(
        ClientArm arm,
        ILocalCachePort cache,
        IClockPort clock,
        ContentSnapshot content,
        Entitlements entitlements,
        FeatureFlags featureFlags) =>
        arm == ClientArm.InProcessLocalHost
            ? InProcessHost(cache, clock, content, entitlements, featureFlags)
            : NoRemotePresenterSurfaceResolved(cache, clock, content, entitlements, featureFlags);

    /// <summary>
    /// The server arm's host, spelling out that no remote presenter surface is built rather than
    /// hiding it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Returns the in-process host today, which is the same type the local arm returns. That is
    /// stated, not concealed, for the reason <see cref="NoAdNetworkResolved"/> gives about its own:
    /// the day the presenters submit through the wire and read projections back, this is the single
    /// site that changes, and it is findable by name rather than by reading a ternary.
    /// </remarks>
    /// <param name="cache">Where the local profile is written.</param>
    /// <param name="clock">The one clock the graph runs on.</param>
    /// <param name="content">The loaded content set the rules read their numbers from.</param>
    /// <param name="entitlements">The resolved entitlement.</param>
    /// <param name="featureFlags">The resolved feature flags.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public static IGameHost NoRemotePresenterSurfaceResolved(
        ILocalCachePort cache,
        IClockPort clock,
        ContentSnapshot content,
        Entitlements entitlements,
        FeatureFlags featureFlags) =>
        InProcessHost(cache, clock, content, entitlements, featureFlags);

    /// <summary>
    /// Builds the wire half for the server arm, or answers that there is none to build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>One transport under both seams, handed in as a handler and never as an
    /// <c>HttpClient</c>.</b> Each adapter builds its own client over it and leaves it open, so the
    /// connection pool, the DNS cache and the socket limits are one process-wide fact rather than
    /// two that disagree.
    /// </para>
    /// <para>
    /// ⚠️ <c>PooledConnectionLifetime</c> is deliberately not chosen. The BCL default never recycles
    /// a pooled connection, which is the known DNS-staleness hazard for a handler that lives as long
    /// as the process — but any interval named here would be invented rather than measured, so the
    /// default ships with the hazard written down instead.
    /// </para>
    /// </remarks>
    /// <param name="arm">The arm this build resolved.</param>
    /// <param name="serverBaseAddress">The address both seams are pointed at.</param>
    /// <exception cref="UriFormatException"><paramref name="serverBaseAddress"/> is not an absolute URI.</exception>
    public static ClientWireSeams? SelectWireSeams(ClientArm arm, string? serverBaseAddress)
    {
        if (arm != ClientArm.ServerSessionWithLocalPresenterSurface ||
            string.IsNullOrWhiteSpace(serverBaseAddress))
        {
            return null;
        }

        var options = new HttpGameApiOptions { BaseAddress = new Uri(serverBaseAddress, UriKind.Absolute) };
        var transport = new SocketsHttpHandler();

        return new ClientWireSeams(
            new HttpGameApi(options, transport),
            new HttpContentDistribution(options.BaseAddress, options.RequestTimeout, transport),
            [transport]);
    }

    /// <summary>
    /// The credential holder, spelling out that no secure store is built rather than hiding it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Named as its own method for the reason <see cref="NoAdNetworkResolved"/> is: the day a
    /// platform keystore port exists this is the single site that changes, and it is findable by
    /// name rather than by reading a constructor call.
    /// </remarks>
    public static EphemeralDeviceCredentials NoDeviceCredentialStoreResolved() => new();

    /// <summary>
    /// Builds the connection half over the wire seam, or answers that there is none to build one over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The port and the presenter are null together.</b> Anything else is a build claiming to
    /// draw a connection it has no way of losing, or holding a seam nothing can report on. Making it
    /// one branch rather than two arguments is what keeps the two from disagreeing.
    /// </para>
    /// <para>
    /// The mirror, the queue and the ladder are built here and shared by the presenter and the pump,
    /// which is the shape they want: one ladder, drawn by one and driven by the other. Two would be
    /// two answers to "is the connection up", and only one of them would ever have been attempted.
    /// </para>
    /// </remarks>
    /// <param name="wire">The wire half, or null when there is none.</param>
    /// <param name="cacheDirectoryPath">The writable root the mirror's own directory sits under.</param>
    /// <param name="content">The loaded content set the connection's four sentences are read out of.</param>
    /// <param name="localeTag">The locale the device reported.</param>
    /// <param name="clock">The one clock the ladder and the two timed announcements are measured on.</param>
    private static (ConnectionPresenter? Presenter, ConnectionPump? Pump, SessionOpener? Session)
        SelectConnectionHalf(
            ClientWireSeams? wire,
            string cacheDirectoryPath,
            ContentProvider content,
            string localeTag,
            IClockPort clock)
    {
        if (wire is null)
        {
            return (null, null, null);
        }

        var mirror = new StateMirror();
        var connection = new ReconnectManager(
            wire.Api, mirror, new CommandQueue(new SystemIdGenerator()), clock);
        var session = new SessionOpener(wire.Api, NoDeviceCredentialStoreResolved(), connection);

        // 🔒 A store of its own beside the local profile, never the same one: the mirror may be
        // deleted wholesale and the profile is the only copy of a player's game.
        var mirrorCache = new MirrorCache(
            new LocalFileCache(Path.Combine(cacheDirectoryPath, MirrorCacheDirectoryName)));

        return (
            new ConnectionPresenter(
                connection, mirror, new LocaleStringCatalogue(content.Current, localeTag), clock),
            new ConnectionPump(connection, session, mirror, mirrorCache, clock),
            session);
    }

    /// <summary>The in-process host both arms resolve, built once so the two branches cannot drift.</summary>
    private static InProcessGameHost InProcessHost(
        ILocalCachePort cache,
        IClockPort clock,
        ContentSnapshot content,
        Entitlements entitlements,
        FeatureFlags featureFlags) =>
        new(
            cache,
            clock,
            new SystemIdGenerator(),
            content,
            entitlements,
            featureFlags,
            sinks: [],
            // No local message store, said rather than defaulted: an inbox is a server-held account
            // fact, so CLAIM_INBOX arriving here is a miswired caller and the rules say so.
            messages: null);

    /// <summary>
    /// Chooses the rewarded-ad arm from the resolved entitlement, and from nothing else.
    /// </summary>
    /// <remarks>
    /// The Plus promise is an adapter swap decided once, here — never a condition carried
    /// into the game. Only <see cref="Entitlements.HasPlus"/> may move the outcome; the
    /// expiry is the server's business, not this branch's.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entitlements"/> is null.</exception>
    public static RewardedAdSelection SelectRewardedAdPort(Entitlements entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);

        return entitlements.HasPlus
            ? new RewardedAdSelection(RewardedAdArm.PlusAutoGrant, new AutoGrantRewardedAd())
            : NoAdNetworkResolved();
    }

    /// <summary>
    /// Chooses where the content set is read from: the checkout on disk when there is one, and the
    /// packed artefact otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>M7-10y. Until this existed, an exported build had no content at all.</b>
    /// <c>LocalFileContentSource</c> reads <c>game-data</c> with <c>System.IO</c>, which is right in a
    /// checkout and in the editor and impossible once the tree is packed — measured against a real
    /// desktop export, which started and then died at <c>AppRoot</c> because <c>res://data</c>
    /// globalises to a path that is not on disk. <c>PackedContentSource</c> is the other route and
    /// this is the one place that picks between them.
    /// </para>
    /// <para>
    /// 🔒 <b>Decided by PROBING THE DISK, never by a platform symbol or a build configuration.</b>
    /// The question is a physical one — is the mirror there — and the answer differs between two runs
    /// of the same binary: a desktop export that ships <c>data/</c> loose and one that packs it are
    /// both Windows release builds. An <c>#if</c> would be right for one of them and silently wrong for
    /// the other, and being wrong here means a game with no content.
    /// </para>
    /// <para>
    /// 🔒 <b>Disk wins when both are available, and that ordering is deliberate.</b> In the editor
    /// the mirror is on disk AND reachable through the engine, and the disk route is the one whose
    /// revision moves with an edit — so a developer changing a tuning value sees it. The packed route
    /// cannot offer that, because a mounted archive never changes.
    /// </para>
    /// </remarks>
    /// <param name="contentDataRootOnDisk">
    /// The mirror's absolute path, or <c>null</c> when it is not on disk — exactly what
    /// <c>GodotUserPaths.ContentDataRootOnDisk</c> answers.
    /// </param>
    /// <param name="packedDocuments">The reader for the packed artefact, used when the disk has none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="packedDocuments"/> is null.</exception>
    public static IContentSourcePort SelectContentSource(
        string? contentDataRootOnDisk, IPackedDocumentReader packedDocuments)
    {
        ArgumentNullException.ThrowIfNull(packedDocuments);

        return string.IsNullOrWhiteSpace(contentDataRootOnDisk)
            ? new PackedContentSource(packedDocuments)
            : new LocalFileContentSource(contentDataRootOnDisk);
    }

    /// <summary>
    /// Chooses the revive arm from the resolved entitlement, and from nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>02</c> §6 gives Plus subscribers a <c>REVIVE</c> that resolves instantly with no ad. Only
    /// <see cref="Entitlements.HasPlus"/> may move the outcome; the expiry is the server's business, not
    /// this branch's — the same rule <see cref="SelectRewardedAdPort"/> states about its own.
    /// </para>
    /// <para>
    /// ⚠️ <b>Separate from <see cref="SelectRewardedAdPort"/> although both read the same flag today,
    /// because they are two different questions.</b> That one asks which ad adapter a player gets; this
    /// one asks whether a revive can be produced at all. They part company the day M15-04 lands a real ad
    /// network — a non-Plus player would then have an ad ADAPTER and still no revive, because granting
    /// one needs <c>CLAIM_AD_REWARD</c>, which is <c>Deferred → M15-03</c>. Collapsing them into one
    /// value would offer that player a button nothing can resolve.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entitlements"/> is null.</exception>
    public static ReviveArm SelectReviveArm(Entitlements entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);

        return entitlements.HasPlus ? ReviveArm.PlusInstant : NoReviveRouteResolved();
    }

    /// <summary>
    /// The free arm, spelling out that no revive route is built rather than hiding it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Named as its own method for the reason <see cref="NoAdNetworkResolved"/> is: the day M15-03
    /// lands an ad-backed revive this is the single site that changes, and it is findable by name rather
    /// than by reading a ternary.
    /// </remarks>
    public static ReviveArm NoReviveRouteResolved() => ReviveArm.NoReviveRouteResolved;

    /// <summary>
    /// The free arm, spelling out that the ad network is not built rather than hiding it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Returns the auto-granting adapter today, which is the same type the Plus arm
    /// returns. That is stated, not concealed: it is a stand-in until
    /// <c>SlayIdleRepeat.Adapters.Ads.AppLovin</c> exists, and this method is the one
    /// place to change when it does.
    /// </remarks>
    public static RewardedAdSelection NoAdNetworkResolved() =>
        new(RewardedAdArm.NoAdNetworkResolved, new AutoGrantRewardedAd());
}
