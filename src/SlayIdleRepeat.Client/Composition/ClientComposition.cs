using SlayIdleRepeat.Adapters.Ads.AutoGrant;
using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Adapters.Cache.LocalFile;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Adapters.Content.Packed;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core;

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
/// The object graph the client runs on, as the composition root hands it over.
/// </summary>
public sealed class ComposedClient
{
    /// <summary>Carries the composed graph. Built only by <see cref="ClientComposition"/>.</summary>
    /// <exception cref="ArgumentNullException">Any part of the graph is null.</exception>
    public ComposedClient(
        IGameHost gameHost,
        RewardedAdSelection rewardedAds,
        ReviveArm revive,
        ContentProvider content,
        IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(rewardedAds);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(clock);

        GameHost = gameHost;
        RewardedAds = rewardedAds;
        Revive = revive;
        Content = content;
        Clock = clock;
    }

    /// <summary>The single entry point the presenters drive the game through.</summary>
    public IGameHost GameHost { get; }

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
    /// <exception cref="ArgumentException">A path is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An ambience value is null.</exception>
    public static ComposedClient Compose(
        string cacheDirectoryPath,
        IContentSourcePort contentSource,
        Entitlements entitlements,
        FeatureFlags featureFlags)
    {
        // Every guard before anything touches a disk: a cache adapter creates its directory on
        // construction, so a root that validated as it went would leave a directory behind for a
        // call it then refused.
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectoryPath);
        ArgumentNullException.ThrowIfNull(contentSource);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(featureFlags);

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

        var gameHost = new InProcessGameHost(
            new LocalFileCache(cacheDirectoryPath),
            clock,
            new SystemIdGenerator(),
            content.Current,
            entitlements,
            featureFlags,
            sinks: []);

        return new ComposedClient(
            gameHost, rewardedAds, SelectReviveArm(entitlements), content, clock);
    }

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
