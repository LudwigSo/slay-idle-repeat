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
    /// The absolute path of the directory holding the game-data mirror, or <c>null</c> when the
    /// content is not on disk at all — which is every packed build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>A question, not a failure.</b> Its throwing sibling below states the case this one
    /// answers: a packed build reaches its resources through the engine's own filesystem and there is
    /// no directory for <c>System.IO</c> to open. Since M7-10y there is a content source for exactly
    /// that case, so "is the content on disk" became a fact the composition root chooses on rather
    /// than an exception it recovers from — and a root that had to catch a
    /// <c>DirectoryNotFoundException</c> to make a routine decision would be using an exception as a
    /// return value.
    /// </para>
    /// <para>
    /// ⚠️ It says <em>nothing</em> about whether the packed alternative will find anything.
    /// <c>null</c> means only that this route is unavailable.
    /// </para>
    /// </remarks>
    public string? ContentDataRootOnDisk()
    {
        var resolved = global::Godot.ProjectSettings.GlobalizePath(ContentDataResourcePath);

        return Directory.Exists(resolved) ? resolved : null;
    }

    /// <summary>
    /// The directory the game is installed in — where the executable itself sits.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>A real directory on every platform and in every build, which is the whole point.</b> It
    /// exists for the callers that need somewhere on disk to start looking and must not fail when the
    /// answer is "nothing there" — the placeholder atlas is the one today. Handing those callers the
    /// content root instead was what made a packed build throw before it could draw anything, because
    /// that root does not exist once the tree is packed.
    /// </remarks>
    public string ResolveInstallationRoot()
    {
        var executable = global::Godot.OS.GetExecutablePath();
        var directory = Path.GetDirectoryName(executable);

        // Falls back to the writable root rather than to the process working directory: a caller is
        // about to walk this looking for optional files, and the working directory on a handset is
        // not a place the game has any relationship with.
        return string.IsNullOrWhiteSpace(directory) ? ResolveWritableCacheRoot() : directory;
    }

    /// <summary>
    /// 🔴 <b>Deliberately absent since M7-10y, and named so the deletion is not mistaken for an
    /// oversight: there is no <c>ResolveContentDataRoot</c>.</b>
    /// </summary>
    /// <remarks>
    /// It threw <c>DirectoryNotFoundException</c> when the mirror was not on disk, and its own message
    /// said the fix was "an engine-backed implementation or an export that ships data/ loose". The
    /// first of those now exists — <c>PackedContentSource</c> over <c>GodotPackedDocuments</c> — so a
    /// packed build is an ordinary case rather than a failure, and a resolver that could only throw for
    /// it had no honest caller left. <see cref="ContentDataRootOnDisk"/> answers the question the
    /// composition root actually has, and <see cref="ResolveInstallationRoot"/> serves the callers that
    /// merely need somewhere to look.
    /// <para>
    /// ⚠️ It was reached from two places and only one was obvious. The second was
    /// <c>BootComposition</c>, handing it to the placeholder atlas as a search root — which is why an
    /// exported build still died with this method's message after the content source was wired.
    /// </para>
    /// </remarks>
    private const string ThereIsNoContentDataRootResolverAnyMore =
        "Removed in M7-10y. A packed build has no content directory on disk and that is now a route " +
        "rather than a fault: ask ContentDataRootOnDisk, or ResolveInstallationRoot if any real " +
        "directory will do.";
}
