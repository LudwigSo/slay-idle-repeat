using System.Globalization;

namespace SlayIdleRepeat.Application.Services;

/// <summary>
/// Turns what a host says about itself into what <c>IPlatformInfoPort</c> promises: a culture the
/// runtime knows, and a device model that is either a real answer or an absence.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is the port's only algorithmic content, and it lives here because it is the only part
/// a test can reach.</b> Every implementation of that port faces the same two translations — a host
/// spells a locale its own way, and a host that cannot identify a device says so with a literal of
/// its own — and the port rules that both arrive as one shape. Left in the adapters, that ruling is
/// re-derived per adapter and pinned by nothing: the shared contract suite can only ask whether the
/// answer has the right <em>shape</em>, and four well-formed constants have the right shape.
/// </para>
/// <para>
/// 🔒 <b>It is pure and machine-independent on purpose.</b> The engine adapter that most needs this
/// logic is the one adapter that cannot be tested at all — calling it outside the Godot runtime is a
/// fatal <c>AccessViolationException</c>, measured. Extracting the translation is what lets that
/// adapter's hardest behaviour be tested anyway: the engine call fetches a string, and everything
/// that happens to the string afterwards happens here.
/// </para>
/// <para>
/// ⚠️ It knows no host's vocabulary. The placeholders a host uses for an unidentified device are
/// that host's, so they arrive as an argument rather than as a list here — a table of engine
/// literals in <c>Application</c> would be the vendor concept crossing the boundary that `23` §5 A2
/// exists to stop.
/// </para>
/// </remarks>
public static class HostAnswers
{
    /// <summary>Separates a host's trailing keyword list from the locale proper.</summary>
    private const char ExtraKeywordSeparator = '@';

    /// <summary>The subtag separator hosts use that <c>BCP 47</c> does not.</summary>
    private const char HostSubtagSeparator = '_';

    /// <summary>The standard's subtag separator.</summary>
    private const char StandardSubtagSeparator = '-';

    /// <summary>Every culture name this runtime knows, read once.</summary>
    /// <remarks>
    /// The enumeration is fixed for the life of the process and walking it costs real time — 813
    /// cultures here, measured at 0.4 ms a pass — so it is read once rather than per translation.
    /// </remarks>
    private static readonly HashSet<string> KnownCultureNames =
        CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Select(known => known.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The culture a host's locale string names, or the invariant culture when it names none this
    /// runtime knows.
    /// </summary>
    /// <param name="hostLocale">
    /// The host's own spelling, e.g. <c>de_DE</c>, <c>zh_Hans_CN@collation=pinyin</c> or
    /// <c>en-GB</c>. May be null or blank.
    /// </param>
    /// <returns>A read-only culture, never null.</returns>
    /// <remarks>
    /// <para>
    /// Three steps, each forced by something a real host does. The trailing keyword list after
    /// <c>@</c> is <b>dropped</b>: it has no <c>BCP 47</c> counterpart to translate into, and
    /// nothing in this game reads a calendar or collation preference from the host, so keeping it
    /// would mean carrying a string no caller can act on. Underscores become hyphens, which is the
    /// same information under different punctuation. What is left is accepted only if the runtime
    /// enumerates it.
    /// </para>
    /// <para>
    /// 🔒 <b>Unknown resolves to invariant rather than throwing.</b> A host reporting a language
    /// nothing has resources for is not a fault a caller can handle — the game still has to start,
    /// in some language — and the port already rules the invariant culture an answer. Throwing here
    /// would turn an unusual handset into a crash at composition.
    /// </para>
    /// </remarks>
    public static CultureInfo ToLocale(string? hostLocale)
    {
        if (string.IsNullOrWhiteSpace(hostLocale))
        {
            return CultureInfo.InvariantCulture;
        }

        var extra = hostLocale.IndexOf(ExtraKeywordSeparator);
        var withoutKeywords = extra < 0 ? hostLocale : hostLocale[..extra];

        var normalised = withoutKeywords
            .Trim()
            .Replace(HostSubtagSeparator, StandardSubtagSeparator);

        return KnownCultureNames.Contains(normalised)
            ? CultureInfo.ReadOnly(CultureInfo.GetCultureInfo(normalised))
            : CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// The device a host named, or <see langword="null"/> when it named none.
    /// </summary>
    /// <param name="hostDeviceModel">The host's answer. May be null, blank, or a placeholder.</param>
    /// <param name="placeholders">
    /// The literals <em>this host</em> uses to mean "I do not know" — e.g. a game engine's
    /// <c>GenericDevice</c>. Compared case-insensitively after trimming.
    /// </param>
    /// <returns>A trimmed, non-blank device model, or <see langword="null"/>.</returns>
    /// <remarks>
    /// 🔒 The port has two states and this is what keeps hosts from inventing a third. Let a
    /// placeholder through and crash grouping collects every unidentified handset in the world under
    /// one invented model name, which is indistinguishable from one real device with one
    /// catastrophic defect.
    /// </remarks>
    public static string? ToDeviceModel(string? hostDeviceModel, params string[] placeholders)
    {
        if (string.IsNullOrWhiteSpace(hostDeviceModel))
        {
            return null;
        }

        var trimmed = hostDeviceModel.Trim();

        return placeholders.Any(p => trimmed.Equals(p, StringComparison.OrdinalIgnoreCase))
            ? null
            : trimmed;
    }
}
