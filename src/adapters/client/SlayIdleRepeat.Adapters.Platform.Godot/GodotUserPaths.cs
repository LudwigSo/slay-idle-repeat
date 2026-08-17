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
/// 🔒 <b>Nothing in this project implements a port, and the reason is measured rather than
/// argued.</b> Calling any of these classes outside the engine does not throw — it kills the
/// process. Every member here reaches <c>GodotSharp</c>, whose managed API is a shim over native
/// function pointers the engine populates at startup; headless, the first call marshals a string
/// through a null pointer and raises an <see cref="AccessViolationException"/> that no
/// <c>catch</c> can observe. M7-01b measured it from a <c>Contract.Tests</c> fixture and the test
/// host died mid-run, taking every other case with it.
/// </para>
/// <para>
/// That is what makes a port here impossible today rather than merely awkward:
/// <c>ContractSuiteCoverageTests.Every_implementation_of_a_port_has_a_contract_fixture</c> demands
/// a fixture for every concrete implementation it can see, this project is on
/// <c>Contract.Tests</c>' reference list, and the unit tier is the only tier this repository has.
/// So these stay concrete types the composition root names directly — a stated limitation, not a
/// design. ⚠️ It is also a contradiction with `23` §7.2, whose composition root registers
/// <c>GodotPlatformInfoAdapter</c>, <c>GodotAudioAdapter</c> and <c>GodotHapticsAdapter</c> against
/// the ports: one of the two has to give, and neither M7-01 nor M7-01b owns choosing which.
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
