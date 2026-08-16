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
    /// <summary>The absolute path of the writable per-user directory the local profile lives in.</summary>
    public string ResolveWritableCacheRoot() => throw new NotImplementedException();

    /// <summary>
    /// The absolute path of the directory holding the game-data mirror.
    /// </summary>
    /// <remarks>
    /// ⚠️ Fails by name when the directory is absent. A packed build reaches its resources
    /// through the engine's own filesystem, not through <c>System.IO</c>, so a silent
    /// fallback here would ship a build that starts and then has no content.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">The resolved directory does not exist.</exception>
    public string ResolveContentDataRoot() => throw new NotImplementedException();
}
