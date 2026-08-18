using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Adapters.Content.Packed;
using SlayIdleRepeat.Application.Ports.Shared;
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
                  contentSource: ShippedContentSource(),
                  entitlements: LocalHostAmbience.NoSubscriptionResolved(),
                  featureFlags: LocalHostAmbience.NoRemoteConfigResolved()))
              .ParamName.ShouldBe(
                  "cacheDirectoryPath",
                  "nothing here is defaulted, exactly as the host it composes defaults nothing. A blank " +
                  "path silently becomes the process working directory, which on a handset is not a " +
                  "place a profile can be written and on a desktop is wherever the game happened to start.");
    }

    [Fact]
    public void Compose_rejects_a_null_content_source()
    {
        Should.Throw<ArgumentNullException>(() => ClientComposition.Compose(
                  cacheDirectoryPath: _cacheRoot,
                  contentSource: null!,
                  entitlements: LocalHostAmbience.NoSubscriptionResolved(),
                  featureFlags: LocalHostAmbience.NoRemoteConfigResolved()))
              .ParamName.ShouldBe(
                  "contentSource",
                  "the content set is the one input that cannot be recovered from at runtime. Failing " +
                  "here by name is the difference between 'the export shipped no data' and a game that " +
                  "starts and then has no enemies in it.");
    }

    // ---- M7-10y: where the content is read from ---------------------------------------------

    /// <summary>
    /// 🔒 <b>A checkout on disk is read off the disk, and a packed artefact through the engine.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 Until M7-10y there was one source and an exported build had NO CONTENT AT ALL — measured
    /// against a real desktop export, which started and then died at <c>AppRoot</c> because
    /// <c>res://data</c> globalises to a path that is not on disk. These two arms are that fix, and the
    /// assertion is on the TYPE because the two differ in exactly the way that matters: one reads with
    /// <c>System.IO</c> and one cannot.
    /// </para>
    /// <para>
    /// 🔒 Both arms are driven, because one is not enough to tell a selection from a constant — the
    /// same argument <see cref="Compose_carries_the_arm_the_entitlement_selected"/> makes about its own.
    /// </para>
    /// </remarks>
    [Fact]
    public void SelectContentSource_reads_the_disk_when_there_is_one_and_the_artefact_otherwise()
    {
        ClientComposition.SelectContentSource(RepoPaths.ContentDataRoot, new StubPackedDocuments())
            .ShouldBeOfType<LocalFileContentSource>(
                "the mirror is on disk here, and the disk route is the only one whose revision moves " +
                "when a developer edits a tuning value.");

        ClientComposition.SelectContentSource(null, new StubPackedDocuments())
            .ShouldBeOfType<PackedContentSource>(
                "no mirror on disk is every packed build, and it is the case the game used to start " +
                "with no content in.");
    }

    /// <summary>…and a blank path is the same absence as a null one, not a root at the process's cwd.</summary>
    [Fact]
    public void SelectContentSource_treats_a_blank_disk_root_as_no_disk_root()
    {
        ClientComposition.SelectContentSource("   ", new StubPackedDocuments())
            .ShouldBeOfType<PackedContentSource>(
                "a blank path silently resolves to the process working directory, which holds no " +
                "content — so reading it as a disk root would produce a source that lists nothing and " +
                "a game that starts with no enemies rather than one that says what is wrong.");
    }

    [Fact]
    public void SelectContentSource_rejects_a_null_packed_reader()
    {
        Should.Throw<ArgumentNullException>(
                  () => ClientComposition.SelectContentSource(RepoPaths.ContentDataRoot, packedDocuments: null!))
              .ParamName.ShouldBe(
                  "packedDocuments",
                  "the packed route is the fallback, so a null reader is a graph with no answer for the " +
                  "case the fallback exists to serve — and it would only be discovered in an export.");
    }

    /// <summary>
    /// ⚠️ <b>Nothing in this tier can prove the real graph CALLS <c>SelectContentSource</c>.</b>
    /// </summary>
    /// <remarks>
    /// The call site is <c>GodotClientComposition.ComposeLocalHost</c>, which reaches the engine, and
    /// M7-01b measured that a GodotSharp call from the unit tier kills the test host. So the counterpart
    /// of <see cref="Compose_carries_the_arm_the_entitlement_selected"/> — "the branch is on the path the
    /// game actually takes" — cannot be written here for this branch. What proves it instead is running
    /// an exported build and watching it load its content, which is why that check is part of M7-10y's
    /// evidence rather than a nicety. This case exists to say so where somebody looking for the missing
    /// assertion will find it.
    /// </remarks>
    [Fact]
    public void The_wiring_of_the_content_source_is_proved_by_an_export_and_not_here()
    {
        typeof(ClientComposition)
            .GetMethod(nameof(ClientComposition.SelectContentSource))
            .ShouldNotBeNull(
                "the selection has to exist and be public for the engine half to call it; whether it " +
                "does call it is proved by an exported build loading content, not by this tier.");
    }

    [Fact]
    public void Compose_rejects_a_null_entitlement()
    {
        Should.Throw<ArgumentNullException>(() => ClientComposition.Compose(
                  cacheDirectoryPath: _cacheRoot,
                  contentSource: ShippedContentSource(),
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
                  contentSource: ShippedContentSource(),
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
            contentSource: ShippedContentSource(),
            entitlements: LocalHostAmbience.NoSubscriptionResolved(),
            featureFlags: LocalHostAmbience.NoRemoteConfigResolved());

        composed.GameHost.ShouldBeOfType<InProcessGameHost>(
            "the local host is the whole point of composing at all before there is a server: the client " +
            "is playable end to end against its own process. Anything else here means the root wired a " +
            "graph the presenters cannot actually drive.");
    }

    [Fact]
    public void Compose_loads_the_content_out_of_the_source_it_was_handed()
    {
        var composed = ClientComposition.Compose(
            cacheDirectoryPath: _cacheRoot,
            contentSource: ShippedContentSource(),
            entitlements: LocalHostAmbience.NoSubscriptionResolved(),
            featureFlags: LocalHostAmbience.NoRemoteConfigResolved());

        composed.Content.Current.DocumentPaths.ShouldContain(
            ShippedDocument,
            $"the composed snapshot does not hold '{ShippedDocument}', so the source handed in was not " +
            "where the content actually came from — the parameter is being validated and then ignored, " +
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
            contentSource: ShippedContentSource(),
            entitlements: new Entitlements(hasPlus, expiresAtUtc: null),
            featureFlags: LocalHostAmbience.NoRemoteConfigResolved());

        composed.RewardedAds.Arm.ShouldBe(
            expected,
            "the branch has to be on the path the game actually takes. A selection method that is correct " +
            "in isolation but never reached from Compose leaves the real graph on whichever arm happened " +
            "to be written first — and one entitlement is not enough to tell 'selected' from 'constant', " +
            "which is why both are driven through here.");
    }
    /// <summary>The shipped content, read off the checkout — what every Compose case here is about.</summary>
    private static IContentSourcePort ShippedContentSource() =>
        new LocalFileContentSource(RepoPaths.ContentDataRoot);

    /// <summary>
    /// A packed reader that holds nothing and is never read — the cases about
    /// <see cref="ClientComposition.SelectContentSource"/> are about WHICH source is chosen, not about
    /// what it can find.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than mocked, on the grounds every fake in this repository is: there is no
    /// mocking library here and adding one to stand in for two members would be a dependency bought for
    /// nothing. It answers empty rather than throwing, because a selection that constructed its source
    /// eagerly and read it would then fail for a reason the case is not about.
    /// </remarks>
    private sealed class StubPackedDocuments : IPackedDocumentReader
    {
        public IReadOnlyList<string> EnumerateDocuments() => [];

        public bool TryRead(string documentPath, out ReadOnlyMemory<byte> bytes)
        {
            bytes = ReadOnlyMemory<byte>.Empty;

            return false;
        }
    }
}