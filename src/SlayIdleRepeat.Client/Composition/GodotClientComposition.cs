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
    /// 🔒 The content root is resolved before anything is constructed, and its absence throws
    /// rather than degrading. See <see cref="GodotUserPaths.ResolveContentDataRoot"/> for why a
    /// packed build is the case that hits it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">The content data root the engine resolves does not exist.</exception>
    public static ComposedClient ComposeLocalHost(GodotClientCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        return ClientComposition.Compose(
            capabilities.Paths.ResolveWritableCacheRoot(),
            capabilities.Paths.ResolveContentDataRoot(),
            LocalHostAmbience.NoSubscriptionResolved(),
            LocalHostAmbience.NoRemoteConfigResolved());
    }
}
