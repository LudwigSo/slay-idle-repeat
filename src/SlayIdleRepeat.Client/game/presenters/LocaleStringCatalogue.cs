using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// Turns a loc key into the words a player reads, out of the content set the game already loaded.
/// One catalogue answers for one locale, for every screen in the client.
/// </summary>
/// <remarks>
/// <para>
/// A plain C# collaborator in the client assembly, deliberately not a port: a port needs a contract
/// fixture this pipeline may not author, and the same precedent already covers <c>IGameHost</c>.
/// </para>
/// <para>
/// It reads the locale documents out of the <see cref="ContentSnapshot"/> the composition root
/// already built, so a screen's strings cost no extra I/O, are validated by the same content
/// invariants as everything else, and stay reachable from a build where the resources are packed
/// and the BCL cannot open them. One instance serves every screen: a second class over the same
/// documents would be the same lookup written twice, drifting on the first fix to either.
/// </para>
/// <para>
/// 🔒 <b>This is not a localisation runtime and must not grow into one.</b> It is a key-to-string
/// lookup with a fallback chain, and that is the whole of it: there are no plurals, no gender or
/// case selection, no ICU or message formatting, no interpolation or argument substitution, no
/// number, date or currency formatting, no font fallback for a script the bundled faces do not
/// cover, and no way to change locale after construction. No such runtime exists anywhere in this
/// repository and no task owns building one, so a screen that needs one of those things has found
/// an unowned gap to report rather than a hole to fill here.
/// </para>
/// <para>
/// Nothing here throws on a miss. An unusable content set is a boot failure with a named kind and a
/// stage, decided by <see cref="BootPresenter"/> and shown on a screen; a catalogue that threw
/// instead would take the exception out through an engine callback where nothing catches it.
/// </para>
/// </remarks>
public sealed class LocaleStringCatalogue
{
    /// <summary>Where the locale documents sit in the content set.</summary>
    private const string LocaleDirectoryPrefix = "loc/";

    /// <summary>The member each locale document states its own tag in.</summary>
    private const string LocaleTagMember = "_locale";

    /// <summary>The member holding the key-to-string table.</summary>
    private const string StringsMember = "strings";

    /// <summary>The source locale every other locale falls back to.</summary>
    private const string SourceLocaleTag = "en";

    /// <summary>Separates the language subtag from the rest of a BCP-47 tag.</summary>
    private const char SubtagSeparator = '-';

    private static readonly IReadOnlyDictionary<string, string> NoStrings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, string> _active;
    private readonly IReadOnlyDictionary<string, string> _source;

    /// <summary>Builds a catalogue over one content set, answering for one locale.</summary>
    /// <param name="content">The loaded content set the locale documents are read from.</param>
    /// <param name="localeTag">The locale the device reported, as a BCP-47 tag.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> or <paramref name="localeTag"/> is null.</exception>
    public LocaleStringCatalogue(ContentSnapshot content, string localeTag)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(localeTag);

        LocaleTag = localeTag;
        ContentSetIsEmpty = content.DocumentPaths.Count == 0;

        var byLocale = ReadLocales(content);

        _active = Select(byLocale, localeTag);
        _source = byLocale.TryGetValue(SourceLocaleTag, out var source) ? source : NoStrings;
    }

    /// <summary>The locale this catalogue answers out of, as it was handed in.</summary>
    public string LocaleTag { get; }

    /// <summary>
    /// True when the content set holds no documents at all — the shape a content load that produced
    /// nothing leaves behind, and the one content state a boot cannot continue past.
    /// </summary>
    /// <remarks>
    /// Deliberately "no documents" rather than "a key was not found". One missing caption renders as
    /// its own key and the game still starts; a content set with nothing in it means no rule the
    /// profile is rehydrated against exists either.
    /// </remarks>
    public bool ContentSetIsEmpty { get; }

    /// <summary>
    /// The string for a key in this catalogue's locale, falling back to the source locale and then
    /// to the key itself.
    /// </summary>
    /// <remarks>
    /// The key is returned rather than a blank because a missing string has to be a visible defect:
    /// an empty label reads as a design choice, a dotted identifier on the screen reads as a bug and
    /// can be found with one search.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or whitespace.</exception>
    public string Resolve(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_active.TryGetValue(key, out var localised))
        {
            return localised;
        }

        return _source.TryGetValue(key, out var source) ? source : key;
    }

    /// <summary>Picks the table for a tag: exact, then its language subtag, then the source locale.</summary>
    private static IReadOnlyDictionary<string, string> Select(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byLocale, string localeTag)
    {
        if (byLocale.TryGetValue(localeTag, out var exact))
        {
            return exact;
        }

        var separator = localeTag.IndexOf(SubtagSeparator);

        if (separator > 0 && byLocale.TryGetValue(localeTag[..separator], out var language))
        {
            return language;
        }

        return byLocale.TryGetValue(SourceLocaleTag, out var source) ? source : NoStrings;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReadLocales(
        ContentSnapshot content)
    {
        var byLocale = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in content.DocumentPaths)
        {
            if (!path.StartsWith(LocaleDirectoryPrefix, StringComparison.Ordinal) ||
                !content.TryGetDocument(path, out var document))
            {
                continue;
            }

            var root = document!.Root;

            // Keyed on the tag the document states rather than on its file name: the file name is a
            // path convention, the member is the document's own claim about which locale it is.
            if (root.TryGetMember(LocaleTagMember, out var tag) && tag!.Kind == ContentValueKind.Text)
            {
                byLocale[tag.AsText()] = ReadStrings(root);
            }
        }

        return byLocale;
    }

    private static IReadOnlyDictionary<string, string> ReadStrings(ContentValue root)
    {
        if (!root.TryGetMember(StringsMember, out var table) || table!.Kind != ContentValueKind.Object)
        {
            return NoStrings;
        }

        var strings = new Dictionary<string, string>(table.MemberNames.Count, StringComparer.Ordinal);

        foreach (var name in table.MemberNames)
        {
            if (table.TryGetMember(name, out var value) && value!.Kind == ContentValueKind.Text)
            {
                strings[name] = value.AsText();
            }
        }

        return strings;
    }
}
