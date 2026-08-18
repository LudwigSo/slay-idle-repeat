using Godot;
using SlayIdleRepeat.Adapters.Platform.Godot;
using SlayIdleRepeat.Application.Hosting;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// The engine capabilities the client composes over, built as one bundle.
/// </summary>
public sealed class GodotClientCapabilities
{
    /// <summary>Carries the four engine-backed capabilities.</summary>
    /// <exception cref="ArgumentNullException">Any capability is null.</exception>
    public GodotClientCapabilities(
        GodotUserPaths paths,
        GodotPlatformInfo platformInfo,
        GodotHaptics haptics,
        GodotAudioOutput audio)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(platformInfo);
        ArgumentNullException.ThrowIfNull(haptics);
        ArgumentNullException.ThrowIfNull(audio);

        Paths = paths;
        PlatformInfo = platformInfo;
        Haptics = haptics;
        Audio = audio;
    }

    /// <summary>Resolves the writable and content roots.</summary>
    public GodotUserPaths Paths { get; }

    /// <summary>Locale and device.</summary>
    public GodotPlatformInfo PlatformInfo { get; }

    /// <summary>Handheld vibration.</summary>
    public GodotHaptics Haptics { get; }

    /// <summary>Sound and buses.</summary>
    public GodotAudioOutput Audio { get; }
}

/// <summary>
/// The whole composed client as the engine half hands it over: the engine capabilities and
/// the portable graph built on top of them.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Exists so the graph has an owner. With no container, the only thing that keeps a
/// composed object alive is a reference somebody holds, and three of the four capabilities
/// have no consumer yet — they are the ports this task was not allowed to declare, standing
/// as concrete types until it can be. Returning them as part of the result is the difference
/// between a graph waiting for its first caller and four objects allocated and dropped: the
/// first is what a composition root is for, the second is a constructor call with no effect.
/// </para>
/// <para>
/// The root node is what holds this, because the root node is the only thing whose lifetime
/// is the application's. That is not a scene reaching into a port — the root hands the host
/// to its presenter and reads nothing else — it is the container's job, done by hand.
/// </para>
/// </remarks>
public sealed class ComposedGodotClient
{
    /// <summary>Pairs the engine capabilities with the graph composed over them.</summary>
    /// <exception cref="ArgumentNullException">Either half is null.</exception>
    public ComposedGodotClient(GodotClientCapabilities capabilities, ComposedClient client)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(client);

        Capabilities = capabilities;
        Client = client;
    }

    /// <summary>The engine-backed capabilities, kept reachable for the consumers still to come.</summary>
    public GodotClientCapabilities Capabilities { get; }

    /// <summary>The portable half of the graph — host, ads and content.</summary>
    public ComposedClient Client { get; }
}

/// <summary>
/// The engine half of the composition root: resolves what only the engine knows, then hands
/// it to the pure half.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The split exists so the decisions are testable. Everything that chooses — above all
/// which ad adapter a player gets — lives in <see cref="ClientComposition"/>, which names no
/// engine type. What is left here is lookup: two paths and four capability objects, none of
/// which decides anything.
/// </para>
/// </remarks>
public static class GodotClientComposition
{
    /// <summary>Builds the engine capabilities, parenting audio players under the given node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="audioSceneRoot"/> is null.</exception>
    public static GodotClientCapabilities BuildCapabilities(Node audioSceneRoot)
    {
        ArgumentNullException.ThrowIfNull(audioSceneRoot);

        return new GodotClientCapabilities(
            new GodotUserPaths(),
            new GodotPlatformInfo(),
            new GodotHaptics(),
            new GodotAudioOutput(audioSceneRoot));
    }

    /// <summary>
    /// Resolves the roots through the engine and composes the client for a local host: one
    /// local profile, no subscription resolved, no remote config resolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Both ambience values are absences rather than defaults, and they are named as such by
    /// the factories that produce them. Nothing on the client is allowed to decide locally that
    /// a player has Plus, so the honest value here is the one that says no store and no remote
    /// config have been reached — not the one that would be most convenient to develop against.
    /// </para>
    /// <para>
    /// 🔒 The content root is PROBED before anything is constructed, and its absence is a route
    /// rather than a failure — see <see cref="ClientComposition.SelectContentSource"/>. It threw here
    /// until M7-10y, which is why an exported build used to start with no content at all.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">The content data root the engine resolves does not exist.</exception>
    public static ComposedGodotClient ComposeLocalHost(GodotClientCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        // The capabilities are handed back out rather than consumed and forgotten. Only the paths
        // have a caller today; audio, haptics and platform info have none until the ports they are
        // standing in for exist, and a composition root that dropped them would be constructing
        // three objects for nothing at all.
        return new ComposedGodotClient(
            capabilities,
            ClientComposition.Compose(
                capabilities.Paths.ResolveWritableCacheRoot(),

                // Lookup, then decision, split the way this file's remarks describe: the engine half
                // answers whether the mirror is on disk, and the pure half chooses the source. The
                // engine-backed reader is constructed either way and costs nothing unpicked — it holds
                // no handle and opens nothing until it is asked.
                ClientComposition.SelectContentSource(
                    capabilities.Paths.ContentDataRootOnDisk(), new GodotPackedDocuments()),
                LocalHostAmbience.NoSubscriptionResolved(),
                LocalHostAmbience.NoRemoteConfigResolved()));
    }
}
