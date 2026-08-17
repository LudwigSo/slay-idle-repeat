using System.Globalization;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The settable fake for <see cref="IPlatformInfoPort"/>: it reports the stated defaults until a
/// test says otherwise, and then reports exactly what it was told.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a scenario that depends on the host a function of its inputs — a German
/// player seeing German copy, a crash report grouped under a device that answered its model, and the
/// same report for a device that did not.
/// </para>
/// <para>
/// 🔒 <b>None of the port's clauses can be switched off here.</b> Every knob below refuses a value
/// the port forbids: a blank device model, a blank OS or build, a locale spelled the host's way
/// rather than the standard's. Those are properties of every implementation, not defaults a fake may
/// relax — and a fake that could be put in a forbidden state would let a use case pass on an answer
/// no real adapter can produce. Same construction, and the same reason, as
/// <see cref="AdjustableClock"/> refusing to be set backwards.
/// </para>
/// </remarks>
public sealed class InMemoryPlatformInfo : IPlatformInfoPort
{
    /// <summary>
    /// What a freshly constructed fake reports as the device. Deliberately a device that
    /// <em>identifies itself</em>, so the absent case is something a scenario has to ask for.
    /// </summary>
    public const string StartDeviceModel = "Contract Test Handset";

    /// <summary>What a freshly constructed fake reports as the operating system.</summary>
    public const string StartOsVersion = "Contract Test OS 1.0";

    /// <summary>What a freshly constructed fake reports as the build.</summary>
    public const string StartAppVersion = "0.0.0-contract-test";

    /// <summary>
    /// Characters a host uses to spell a locale that the standard does not, refused by
    /// <see cref="ReportLocale"/>.
    /// </summary>
    private static readonly char[] SpellingsThatAreNotBcp47 = ['_', '@'];

    private string? _deviceModel = StartDeviceModel;
    private string _osVersion = StartOsVersion;
    private string _appVersion = StartAppVersion;
    private CultureInfo _locale = StartLocale;

    /// <summary>The language a freshly constructed fake reports, where the host has it.</summary>
    public const string PreferredStartLocaleName = "en-GB";

    /// <summary>
    /// Every culture name this runtime enumerates, read <b>once</b>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Hoisted because it was measured costing 0.4 ms per construction: the enumeration runs 813
    /// cultures on this host, and it was being walked from a field initialiser, so every
    /// <c>new InMemoryPlatformInfo()</c> in every Application scenario paid for it — 100
    /// constructions took 40.6 ms against the host adapter's 0.5 ms. The set cannot change while the
    /// process runs, which is the same reason <c>IPlatformInfoPort</c>'s own members are facts.
    /// </remarks>
    private static readonly HashSet<string> EnumeratedCultureNames =
        CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Select(known => known.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What a freshly constructed fake reports as the player's language: a real culture where the
    /// host has cultures at all, and the invariant one where it does not.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The fallback is not defensive padding; without it this fake cannot be CONSTRUCTED on a
    /// host in globalization-invariant mode.</b> Measured: under
    /// <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1</c> the direct lookup throws
    /// <c>CultureNotFoundException</c> out of the field initialiser and every case running against
    /// this fake dies before it starts. That is not hypothetical here — five <c>tools/</c> projects
    /// already set <c>InvariantGlobalization</c>, and an ICU-less slim container is the ordinary
    /// shape of a Linux server image. The invariant culture is an answer this port explicitly
    /// admits, so falling back to it changes what a scenario sees without changing what is legal.
    /// <para>
    /// 🔒 <b>Both branches are pinned by a test</b>, because nothing else can see them: the shared
    /// suite asserts shapes, and the invariant culture satisfies every shape the preferred one does.
    /// Inverting this ternary left all 143 cases green — the fake would have reported the invariant
    /// culture on every host and no assertion anywhere would have moved.
    /// </para>
    /// </remarks>
    public static readonly CultureInfo StartLocale =
        EnumeratedCultureNames.Contains(PreferredStartLocaleName)
            ? CultureInfo.ReadOnly(CultureInfo.GetCultureInfo(PreferredStartLocaleName))
            : CultureInfo.InvariantCulture;

    /// <inheritdoc/>
    public string? DeviceModel => _deviceModel;

    /// <inheritdoc/>
    public string OsVersion => _osVersion;

    /// <inheritdoc/>
    public string AppVersion => _appVersion;

    /// <inheritdoc/>
    public CultureInfo Locale => _locale;

    /// <summary>
    /// Reports <paramref name="model"/> as the device, or <see langword="null"/> for a host that
    /// cannot identify one.
    /// </summary>
    /// <param name="model">The device, or <see langword="null"/> for an unidentified one.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="model"/> is empty or whitespace. An absence is <see langword="null"/>; a
    /// blank string is a third state the port does not have.
    /// </exception>
    public void ReportDeviceModel(string? model)
    {
        if (model is not null && string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException(
                "a blank device model is neither an absence nor an answer. IPlatformInfoPort spells " +
                "an unidentified device null, and a scenario that wants one should pass null — a fake " +
                "that accepted a blank would let a caller be written against a state no adapter can " +
                "reach.",
                nameof(model));
        }

        _deviceModel = model;
    }

    /// <summary>Reports <paramref name="osVersion"/> as the operating system.</summary>
    /// <param name="osVersion">The operating system. Never null, empty or whitespace.</param>
    /// <exception cref="ArgumentNullException"><paramref name="osVersion"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="osVersion"/> is empty or whitespace.</exception>
    public void ReportOsVersion(string osVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(osVersion);

        _osVersion = osVersion;
    }

    /// <summary>Reports <paramref name="appVersion"/> as the build.</summary>
    /// <param name="appVersion">The build. Never null, empty or whitespace.</param>
    /// <exception cref="ArgumentNullException"><paramref name="appVersion"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="appVersion"/> is empty or whitespace.</exception>
    public void ReportAppVersion(string appVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        _appVersion = appVersion;
    }

    /// <summary>Reports <paramref name="locale"/> as the language the player reads.</summary>
    /// <param name="locale">A culture the runtime resolves, named the way the standard names it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="locale"/> is named the way a host spells a locale rather than the way the
    /// standard does, or its name is one the runtime does not resolve to itself.
    /// </exception>
    public void ReportLocale(CultureInfo locale)
    {
        ArgumentNullException.ThrowIfNull(locale);

        if (locale.Name.IndexOfAny(SpellingsThatAreNotBcp47) >= 0)
        {
            throw new ArgumentException(
                $"'{locale.Name}' carries punctuation BCP 47 does not use, so it is a host's own " +
                "locale spelling rather than a translated one. Every implementation of " +
                "IPlatformInfoPort does that translation; a fake that skipped it would let a caller " +
                "be written against the engine's spelling and fail everywhere else.",
                nameof(locale));
        }

        if (!EnumeratedCultureNames.Contains(locale.Name))
        {
            throw new ArgumentException(
                $"'{locale.Name}' is not among the cultures this runtime enumerates. A CultureInfo " +
                "can be built around any well-formed tag — the runtime manufactures one and hands it " +
                "back — so a name that merely parses is no evidence the language exists, and a " +
                "scenario running against one is a scenario about a player nobody has.",
                nameof(locale));
        }

        _locale = CultureInfo.ReadOnly(locale);
    }
}
