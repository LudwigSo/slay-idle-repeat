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
/// is the application's. That is not a scene reaching into a port — the root hands this whole
/// object to a factory and reads nothing out of it — it is the container's job, done by hand.
/// </para>
/// </remarks>
public sealed class ComposedGodotClient : IDisposable
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

    /// <summary>Tears the graph down with the node that owns it.</summary>
    /// <remarks>
    /// 🔒 The whole reason the root scene can close a transport without ever naming one: the root
    /// disposes the object it holds, and disposal runs down the graph to the wire seams and the
    /// handler underneath them. The engine capabilities are not disposed — they are engine objects
    /// whose lifetime the engine owns, and the audio one is parented into the scene tree.
    /// </remarks>
    public void Dispose() => Client.Dispose();
}

/// <summary>
/// The engine half of the composition root: resolves what only the engine knows, then hands
/// it to the pure half.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The split exists so the decisions are testable. Everything that chooses — above all
/// which ad adapter a player gets, and which host arm this build runs — lives in
/// <see cref="ClientComposition"/>, which names no engine type. What is left here is lookup: two
/// paths, one environment variable and four capability objects, none of which decides anything.
/// </para>
/// </remarks>
public static class GodotClientComposition
{
    /// <summary>
    /// The environment variable a server address is read from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>A DEVELOPER SWITCH, NOT A DEPLOYMENT MECHANISM.</b> An exported handset build has no
    /// environment to read, so it always takes the local arm — which is correct today, because the
    /// server arm mints a new anonymous account on every cold start and holds its credential in
    /// memory only. Shipping a build against a server needs an export-time configuration format
    /// this repository does not have, and inventing one here would make an unbuilt mechanism look
    /// like a shipped one.
    /// </para>
    /// <para>
    /// Set it to an absolute base address — <c>http://127.0.0.1:8080/</c> — to compose the wire.
    /// Unset, empty or blank is the local arm, which is what every build without it composes.
    /// </para>
    /// </remarks>
    public const string ServerBaseAddressEnvironmentVariable = "SLAYIDLEREPEAT_SERVER";

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
    /// Resolves the roots and the server address through the engine, and composes the client on
    /// whichever arm that address selects: no subscription resolved, no remote config resolved.
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
    /// <para>
    /// 🔒 Lookup here, decision there. The environment read is an engine call and so cannot be
    /// reached from the unit tier at all; which arm an address selects, and what that arm builds,
    /// are <see cref="ClientComposition.SelectArm"/>'s and
    /// <see cref="ClientComposition.SelectWireSeams"/>'s, where both are driven on both arms by a
    /// test. What is left here is the one thing only the engine can answer.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">The content data root the engine resolves does not exist.</exception>
    /// <exception cref="UriFormatException">The environment named something that is not an absolute URI.</exception>
    public static ComposedGodotClient ComposeClient(GodotClientCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        // Empty when nothing set it, which SelectArm reads as the local arm — so an exported build,
        // which has no environment at all, composes exactly what it composed before this existed.
        var serverBaseAddress = global::Godot.OS.GetEnvironment(ServerBaseAddressEnvironmentVariable);

        var arm = ClientComposition.SelectArm(serverBaseAddress);

        // The graph owns the seams and disposes them with itself, which is why they are built here
        // and handed in rather than held beside it: the root scene holds one object, and tearing
        // that object down is what closes the transport.
        var wire = ClientComposition.SelectWireSeams(arm, serverBaseAddress);

        try
        {
            // The capabilities are handed back out rather than consumed and forgotten. Only the paths
            // have a caller today; audio, haptics and platform info have none until the ports they are
            // standing in for exist, and a composition root that dropped them would be constructing
            // three objects for nothing at all.
            return new ComposedGodotClient(
                capabilities,
                ClientComposition.Compose(
                    capabilities.Paths.ResolveWritableCacheRoot(),

                    // Lookup, then decision, split the way this file's remarks describe: the engine
                    // half answers whether the mirror is on disk, and the pure half chooses the
                    // source. The engine-backed reader is constructed either way and costs nothing
                    // unpicked — it holds no handle and opens nothing until it is asked.
                    ClientComposition.SelectContentSource(
                        capabilities.Paths.ContentDataRootOnDisk(), new GodotPackedDocuments()),
                    LocalHostAmbience.NoSubscriptionResolved(),
                    LocalHostAmbience.NoRemoteConfigResolved(),
                    capabilities.PlatformInfo.Locale,
                    arm,
                    wire));
        }
        catch
        {
            // The seams are built before the graph that would own them, and a graph that never got
            // built owns nothing — so a compose that threw would leave the transport open for the
            // life of a process the root goes on running after reporting the failure.
            wire?.Dispose();

            throw;
        }
    }
}
