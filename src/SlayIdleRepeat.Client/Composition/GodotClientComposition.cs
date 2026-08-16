using Godot;
using SlayIdleRepeat.Adapters.Platform.Godot;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// The engine capabilities the client composes over, built as one bundle.
/// </summary>
public sealed class GodotClientCapabilities
{
    /// <summary>Carries the four engine-backed capabilities.</summary>
    public GodotClientCapabilities(
        GodotUserPaths paths,
        GodotPlatformInfo platformInfo,
        GodotHaptics haptics,
        GodotAudioOutput audio)
    {
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
    public static GodotClientCapabilities BuildCapabilities(Node audioSceneRoot) =>
        throw new NotImplementedException();

    /// <summary>
    /// Resolves the roots through the engine and composes the client for a local host: one
    /// local profile, no subscription resolved, no remote config resolved.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">The content data root the engine resolves does not exist.</exception>
    public static ComposedClient ComposeLocalHost(GodotClientCapabilities capabilities) =>
        throw new NotImplementedException();
}
