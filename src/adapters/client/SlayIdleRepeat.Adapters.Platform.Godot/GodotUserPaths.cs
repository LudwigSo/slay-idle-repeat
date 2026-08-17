namespace SlayIdleRepeat.Adapters.Platform.Godot;

/// <summary>
/// Resolves the engine's virtual roots to absolute filesystem paths the BCL can open.
/// </summary>
/// <remarks>
/// <para>
/// Both roots differ per platform, and neither difference is expressible as a compile-time
/// symbol: the writable root is a per-user, per-OS directory the engine picks, and the data
/// root moves when the game is exported. Resolving them here is where the platform
/// conditionality of the composition root actually lives.
/// </para>
/// <para>
/// ⚠️ Implements no port. Audio, haptics, locale and device info are catalogued as deferred
/// ports with no adapters and no fakes yet, and declaring one here would pull in obligations
/// this task cannot discharge. Until then these are concrete types the composition root
/// names directly, which is a stated limitation rather than a design.
/// </para>
/// </remarks>
public sealed class GodotUserPaths
{
    /// <summary>Where the build-time mirror of <c>game-data</c> lands inside the project.</summary>
    private const string ContentDataResourcePath = "res://data";

    /// <summary>The absolute path of the writable per-user directory the local profile lives in.</summary>
    public string ResolveWritableCacheRoot() => global::Godot.OS.GetUserDataDir();

    /// <summary>
    /// The absolute path of the directory holding the game-data mirror.
    /// </summary>
    /// <remarks>
    /// ⚠️ Fails by name when the directory is absent. A packed build reaches its resources
    /// through the engine's own filesystem, not through <c>System.IO</c>, so a silent
    /// fallback here would ship a build that starts and then has no content.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">The resolved directory does not exist.</exception>
    public string ResolveContentDataRoot()
    {
        var resolved = global::Godot.ProjectSettings.GlobalizePath(ContentDataResourcePath);

        return Directory.Exists(resolved)
            ? resolved
            : throw new DirectoryNotFoundException(
                $"The engine resolved '{ContentDataResourcePath}' to '{resolved}', and there is no directory " +
                "there. Running from a checkout, that means the build-time mirror of 'game-data' into the " +
                "project has not run. Running from an EXPORTED build, it is expected and is not fixable here: " +
                "resources packed into a .pck or an APK are reachable only through the engine's own file " +
                "access, never through System.IO, so the content source needs either an engine-backed " +
                "implementation or an export that ships 'data/' loose beside the executable. Either way the " +
                "game has no content to load, and starting anyway would only move the failure somewhere it " +
                "no longer names its cause.");
    }
}
