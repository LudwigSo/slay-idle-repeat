using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Services.Content;
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
    public RewardedAdSelection(RewardedAdArm arm, IRewardedAdPort port)
    {
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
    public ComposedClient(IGameHost gameHost, RewardedAdSelection rewardedAds, ContentProvider content)
    {
        GameHost = gameHost;
        RewardedAds = rewardedAds;
        Content = content;
    }

    /// <summary>The single entry point the presenters drive the game through.</summary>
    public IGameHost GameHost { get; }

    /// <summary>The rewarded-ad port, with the arm that chose it.</summary>
    public RewardedAdSelection RewardedAds { get; }

    /// <summary>The loaded content, kept so a later reload has somewhere to land.</summary>
    public ContentProvider Content { get; }
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
    /// <param name="contentDataRootPath">Absolute path of the directory holding the game-data mirror.</param>
    /// <param name="entitlements">The resolved entitlement. Never inferred from a local receipt.</param>
    /// <param name="featureFlags">The resolved feature flags.</param>
    /// <exception cref="ArgumentException">A path is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An ambience value is null.</exception>
    public static ComposedClient Compose(
        string cacheDirectoryPath,
        string contentDataRootPath,
        Entitlements entitlements,
        FeatureFlags featureFlags) =>
        throw new NotImplementedException();

    /// <summary>
    /// Chooses the rewarded-ad arm from the resolved entitlement, and from nothing else.
    /// </summary>
    /// <remarks>
    /// The Plus promise is an adapter swap decided once, here — never a condition carried
    /// into the game. Only <see cref="Entitlements.HasPlus"/> may move the outcome; the
    /// expiry is the server's business, not this branch's.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="entitlements"/> is null.</exception>
    public static RewardedAdSelection SelectRewardedAdPort(Entitlements entitlements) =>
        throw new NotImplementedException();

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
        throw new NotImplementedException();
}
