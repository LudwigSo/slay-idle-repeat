using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Services;

namespace SlayIdleRepeat.Adapters.Platform.Host;

/// <summary>
/// The real <see cref="IPlatformInfoPort"/> over the host process: what the runtime itself knows
/// about the machine it was started on, read straight from the BCL.
/// </summary>
/// <remarks>
/// <para>
/// This is what a desktop build, the editor and every tool run on. It is not the engine's answer and
/// does not try to be — see <see cref="DeviceModel"/>.
/// </para>
/// <para>
/// 🔒 <b>Everything is captured once, in the constructor, and never re-read.</b> The port's first
/// clause is that its members are facts rather than samples, and capturing is how an implementation
/// makes that true rather than merely likely. It also fixes the one member whose source is not
/// process-wide: the host's language preference is thread-scoped, so an instance answers the
/// language of the thread that built it and keeps answering it.
/// </para>
/// </remarks>
public sealed class HostPlatformInfo : IPlatformInfoPort
{
    /// <summary>Separates a SemVer core and pre-release from the build metadata after it.</summary>
    private const char BuildMetadataSeparator = '+';

    private readonly string _osVersion;
    private readonly string _appVersion;
    private readonly CultureInfo _locale;

    /// <summary>Reads the host once.</summary>
    public HostPlatformInfo()
    {
        _osVersion = RuntimeInformation.OSDescription;
        _appVersion = ReadBuildVersion();
        _locale = HostAnswers.ToLocale(CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>
    /// Always <see langword="null"/>: a general-purpose runtime has no notion of a device model.
    /// </summary>
    /// <remarks>
    /// 🔒 Not a stub and not a gap. The port's ruling is that a host which cannot identify the
    /// device says so with <see langword="null"/>, and this host genuinely cannot — the BCL exposes
    /// an architecture, an OS string and a process, and none of those is the handset a crash report
    /// wants to group by. Answering the machine name here would be a lie a caller cannot detect,
    /// which is the exact failure the port's remarks describe.
    /// </remarks>
    public string? DeviceModel => null;

    /// <inheritdoc/>
    /// <remarks>
    /// The runtime's own description — a name and a version in one string, which is the form a human
    /// triaging a crash reads it in.
    /// <para>
    /// ⚠️ Unguarded, unlike <see cref="AppVersion"/>, and the asymmetry is deliberate: the runtime
    /// documents this as a description of the operating system and there is no supported host on
    /// which it is blank, so a fallback here would be dead code carrying an invented string. If one
    /// is ever found, the repair is to name that host, not to substitute for it.
    /// </para>
    /// </remarks>
    public string OsVersion => _osVersion;

    /// <inheritdoc/>
    public string AppVersion => _appVersion;

    /// <inheritdoc/>
    /// <remarks>
    /// The <em>UI</em> culture rather than the formatting culture: this member answers "which
    /// language does the player read", and a host is free to format numbers one way while reading
    /// another.
    /// </remarks>
    public CultureInfo Locale => _locale;

    /// <summary>
    /// The version of the assembly that started this process, falling back to this adapter's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entry assembly is the build the player is running, so it is asked first; a host that
    /// started the runtime itself has no entry assembly, and the Godot client is exactly that, so
    /// this adapter's own assembly answers instead. Both carry the same
    /// <c>Directory.Build.props</c> version.
    /// </para>
    /// <para>
    /// ⚠️ <b>Build metadata is stripped, and it is there in every real build.</b> The SDK writes
    /// the source revision into the informational version by default, so the raw attribute reads
    /// <c>0.1.0+&lt;commit sha&gt;</c> — measured on this repository's own output, not assumed. The
    /// port asks for the version.
    /// </para>
    /// <para>
    /// ⚠️ Under a test host the entry assembly is the <em>test platform</em>, so this answers the
    /// runner's version rather than the game's. That is the honest reading of "which build started
    /// this process" and is not worth special-casing, but it does mean no test observes the number
    /// a player would see.
    /// </para>
    /// </remarks>
    private static string ReadBuildVersion() =>
        ReadBuildVersion(Assembly.GetEntryAssembly(), typeof(HostPlatformInfo).Assembly);

    /// <summary>
    /// The build version, from <paramref name="entry"/> if it states one and from
    /// <paramref name="self"/> otherwise.
    /// </summary>
    /// <param name="entry">The assembly that started the process, or null when an unmanaged host did.</param>
    /// <param name="self">This adapter's own assembly, or null only in a test driving the last arm.</param>
    /// <remarks>
    /// 🔒 <b>Both assemblies are parameters so that every arm of this chain can be reached by a
    /// test</b>, including the failure. The previous shape terminated in a throw that no input could
    /// produce — <c>typeof(HostPlatformInfo).Assembly</c> is never null and its
    /// <c>GetName().Version</c> is <c>0.1.0.0</c> — so the guard was a comment with an exception
    /// around it, which is exactly the defect class steering S1 names.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Neither assembly states a version of any kind.</exception>
    internal static string ReadBuildVersion(Assembly? entry, Assembly? self) =>
        VersionOf(entry)
        ?? VersionOf(self)
        ?? throw new InvalidOperationException(
            "neither the assembly that started this process nor this adapter's own states a version "
            + "of any kind — no informational version and no assembly version. IPlatformInfoPort."
            + "AppVersion has no absent state, and the alternative to failing here is shipping a "
            + "placeholder that every crash report and every support conversation then treats as a "
            + "real build number.");

    /// <summary>An assembly's version, without whatever the build stamped after it.</summary>
    private static string? VersionOf(Assembly? assembly)
    {
        if (assembly is null)
        {
            return null;
        }

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString();
        }

        var metadata = informational.IndexOf(BuildMetadataSeparator);

        return metadata < 0 ? informational : informational[..metadata];
    }
}
