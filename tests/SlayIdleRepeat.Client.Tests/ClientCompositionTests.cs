using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Core;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The pure half of the composition root: what it refuses, what it builds, and the one decision
/// it makes.
/// </summary>
public sealed class ClientCompositionTests : IDisposable
{
    // 🔒 One expiry permanently behind every clock the suite will ever run under and one
    // permanently ahead of it. Two dates that merely straddle today would stop being able to
    // catch an expiry-derived implementation the moment the later one lapsed.
    private static readonly DateTimeOffset ExpiryLongPast = new(2020, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiryFarFuture = new(2999, 11, 4, 12, 30, 0, TimeSpan.Zero);

    /// <summary>A document the checkout's own <c>game-data</c> holds, used to prove the root was read.</summary>
    private const string ShippedDocument = "content/enemies/enemies.json";

    private readonly string _cacheRoot = RepoPaths.ScratchCacheRoot();

    /// <summary>Removes the scratch cache root a composed graph may have created.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void SelectRewardedAdPort_takes_the_Plus_arm_when_the_entitlement_grants_Plus()
    {
        var selection = ClientComposition.SelectRewardedAdPort(new Entitlements(hasPlus: true, expiresAtUtc: null));

        selection.Arm.ShouldBe(
            RewardedAdArm.PlusAutoGrant,
            "the Plus promise is 'no ads, same rewards', and it is kept by swapping the adapter here " +
            "rather than by testing a flag somewhere in the game. Both arms happen to resolve to the " +
            "same type today, so the arm is the only thing that can show the swap actually happened.");
    }

    [Fact]
    public void SelectRewardedAdPort_takes_the_unresolved_ad_network_arm_without_Plus()
    {
        var selection = ClientComposition.SelectRewardedAdPort(new Entitlements(hasPlus: false, expiresAtUtc: null));

        selection.Arm.ShouldBe(
            RewardedAdArm.NoAdNetworkResolved,
            "a player without Plus is supposed to reach the ad network, and there is no ad network " +
            "built yet. Naming that arm is what keeps the gap visible; quietly handing back the " +
            "auto-granting adapter under the Plus arm's name would make free rewards look shipped.");
    }

    [Fact]
    public void SelectRewardedAdPort_keeps_the_Plus_arm_however_dead_the_expiry_looks()
    {
        var lapsed = ClientComposition.SelectRewardedAdPort(new Entitlements(hasPlus: true, expiresAtUtc: ExpiryLongPast));
        var live = ClientComposition.SelectRewardedAdPort(new Entitlements(hasPlus: true, expiresAtUtc: ExpiryFarFuture));

        lapsed.Arm.ShouldBe(
            RewardedAdArm.PlusAutoGrant,
            "the entitlement is server-issued and already resolved by the time it gets here, so whether " +
            "the term has run out is not this branch's question. A root that re-derives it from the " +
            "expiry decides the subscription locally, which is the one thing the entitlement exists to " +
            "stop — and it would start showing ads to a paying player the day the server's own renewal " +
            "arrives a minute late.");
        live.Arm.ShouldBe(
            RewardedAdArm.PlusAutoGrant,
            "stated as an absolute arm rather than as 'the same as the other one'. Two calls agreeing " +
            "proves nothing about which arm they agreed on, and an implementation that returned the " +
            "free arm for both would satisfy that weaker claim exactly.");
    }

    [Fact]
    public void SelectRewardedAdPort_keeps_the_unresolved_arm_however_live_the_expiry_looks()
    {
        var selection = ClientComposition.SelectRewardedAdPort(
            new Entitlements(hasPlus: false, expiresAtUtc: ExpiryFarFuture));

        selection.Arm.ShouldBe(
            RewardedAdArm.NoAdNetworkResolved,
            "the mirror of the case above, and the one that actually costs money: a future expiry on an " +
            "entitlement the server did NOT grant Plus on must not be read as 'still subscribed'. " +
            "HasPlus is the whole input, and a root inferring Plus from a date would hand the ad-free " +
            "adapter to someone who is not paying for it.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectRewardedAdPort_returns_a_port_alongside_the_arm(bool hasPlus)
    {
        var selection = ClientComposition.SelectRewardedAdPort(new Entitlements(hasPlus, expiresAtUtc: null));

        selection.Port.ShouldNotBeNull(
            "an arm with no port behind it is a decision nothing can act on — the placement would have " +
            "no way to show or grant anything. Checked on both arms, because the arm that is currently " +
            "a stand-in is exactly the one whose port is easiest to leave out.");
    }

    [Fact]
    public void SelectRewardedAdPort_rejects_a_null_entitlement()
    {
        Should.Throw<ArgumentNullException>(() => ClientComposition.SelectRewardedAdPort(entitlements: null!))
              .ParamName.ShouldBe(
                  "entitlements",
                  "a null entitlement is an unresolved one, and defaulting it either way silently decides " +
                  "a subscription question on the player's behalf.");
    }

    [Fact]
    public void Compose_rejects_a_blank_cache_directory_path()
    {
        Should.Throw<ArgumentException>(() => ClientComposition.Compose(
                  cacheDirectoryPath: "   ",
                  contentDataRootPath: RepoPaths.ContentDataRoot,
                  entitlements: LocalHostAmbience.NoSubscriptionResolved(),
                  featureFlags: LocalHostAmbience.NoRemoteConfigResolved()))
              .ParamName.ShouldBe(
                  "cacheDirectoryPath",
                  "nothing here is defaulted, exactly as the host it composes defaults nothing. A blank " +
                  "path silently becomes the process working directory, which on a handset is not a " +
                  "place a profile can be written and on a desktop is wherever the game happened to start.");
    }

    [Fact]
    public void Compose_rejects_a_blank_content_data_root_path()
    {
        Should.Throw<ArgumentException>(() => ClientComposition.Compose(
                  cacheDirectoryPath: _cacheRoot,
                  contentDataRootPath: "   ",
                  entitlements: LocalHostAmbience.NoSubscriptionResolved(),
                  featureFlags: LocalHostAmbience.NoRemoteConfigResolved()))
              .ParamName.ShouldBe(
                  "contentDataRootPath",
                  "the content root is the one input that cannot be recovered from at runtime. Failing " +
                  "here by name is the difference between 'the export shipped no data' and a game that " +
                  "starts and then has no enemies in it.");
    }

    [Fact]
    public void Compose_rejects_a_null_entitlement()
    {
        Should.Throw<ArgumentNullException>(() => ClientComposition.Compose(
                  cacheDirectoryPath: _cacheRoot,
                  contentDataRootPath: RepoPaths.ContentDataRoot,
                  entitlements: null!,
                  featureFlags: LocalHostAmbience.NoRemoteConfigResolved()))
              .ParamName.ShouldBe(
                  "entitlements",
                  "the absence of a subscription is stated by a factory that says so, never by a null " +
                  "that could equally mean 'not looked up yet'.");
    }

    [Fact]
    public void Compose_rejects_null_feature_flags()
    {
        Should.Throw<ArgumentNullException>(() => ClientComposition.Compose(
                  cacheDirectoryPath: _cacheRoot,
                  contentDataRootPath: RepoPaths.ContentDataRoot,
                  entitlements: LocalHostAmbience.NoSubscriptionResolved(),
                  featureFlags: null!))
              .ParamName.ShouldBe(
                  "featureFlags",
                  "the flags are kill switches, and their identity element is 'everything on, nothing " +
                  "disabled'. A null one would have to be interpreted, and either interpretation is a " +
                  "guess about what live-ops meant.");
    }

    [Fact]
    public void Compose_builds_a_graph_whose_host_is_the_in_process_host()
    {
        var composed = ClientComposition.Compose(
            cacheDirectoryPath: _cacheRoot,
            contentDataRootPath: RepoPaths.ContentDataRoot,
            entitlements: LocalHostAmbience.NoSubscriptionResolved(),
            featureFlags: LocalHostAmbience.NoRemoteConfigResolved());

        composed.GameHost.ShouldBeOfType<InProcessGameHost>(
            "the local host is the whole point of composing at all before there is a server: the client " +
            "is playable end to end against its own process. Anything else here means the root wired a " +
            "graph the presenters cannot actually drive.");
    }

    [Fact]
    public void Compose_loads_the_content_out_of_the_data_root_it_was_handed()
    {
        var composed = ClientComposition.Compose(
            cacheDirectoryPath: _cacheRoot,
            contentDataRootPath: RepoPaths.ContentDataRoot,
            entitlements: LocalHostAmbience.NoSubscriptionResolved(),
            featureFlags: LocalHostAmbience.NoRemoteConfigResolved());

        composed.Content.Current.DocumentPaths.ShouldContain(
            ShippedDocument,
            $"the composed snapshot does not hold '{ShippedDocument}', so the path handed in was not the " +
            "root the content actually came from — the parameter is being validated and then ignored, " +
            "or the source was pointed somewhere else. Every rule in the game reads its numbers through " +
            "this snapshot, and a graph built over an empty one is a game with no enemies, no gear and " +
            "no drops that nonetheless starts.");
    }

    [Theory]
    [InlineData(true, RewardedAdArm.PlusAutoGrant)]
    [InlineData(false, RewardedAdArm.NoAdNetworkResolved)]
    public void Compose_carries_the_arm_the_entitlement_selected(bool hasPlus, RewardedAdArm expected)
    {
        var composed = ClientComposition.Compose(
            cacheDirectoryPath: _cacheRoot,
            contentDataRootPath: RepoPaths.ContentDataRoot,
            entitlements: new Entitlements(hasPlus, expiresAtUtc: null),
            featureFlags: LocalHostAmbience.NoRemoteConfigResolved());

        composed.RewardedAds.Arm.ShouldBe(
            expected,
            "the branch has to be on the path the game actually takes. A selection method that is correct " +
            "in isolation but never reached from Compose leaves the real graph on whichever arm happened " +
            "to be written first — and one entitlement is not enough to tell 'selected' from 'constant', " +
            "which is why both are driven through here.");
    }
}
