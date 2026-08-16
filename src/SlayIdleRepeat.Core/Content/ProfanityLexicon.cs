using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The word lists the name filter matches against, one per <see cref="ProfanityLanguage"/>, read out
/// of <c>content/profanity/</c>.
/// </summary>
/// <remarks>
/// <para>
/// The <b>mechanism</b> is code and lives in <c>Rules/Hero/</c>; the <b>words</b> are content and
/// live here, because which words a language considers profane is an authoring and localisation
/// decision that changes without a build. The two are deliberately separable: a term added to a file
/// is a data edit, and a change to how a candidate is normalised is a code change with tests.
/// </para>
/// <para>
/// 🔴 <b>An empty list is a fault, never an empty filter.</b> This is the whole reason this type
/// validates at all. A word list that failed to load reads as zero terms, and a filter with zero
/// terms accepts every name there is — silently, on every account created from that moment on, with
/// nothing anywhere reporting a problem. So a missing document, an unauthorised list, a list of the
/// wrong shape and a list of length zero are each refused loudly at the read, and every language
/// <c>27</c> §1 names must be present before any of them may be consulted.
/// </para>
/// <para>
/// The terms are held exactly as authored. Normalising them here would put half the matching rule in
/// the reader, where a change to it would be invisible to the rule's own tests — the rule normalises
/// both sides itself, and the files are authored in the form it produces so that a reader of the data
/// can see what is being compared.
/// </para>
/// </remarks>
internal sealed class ProfanityLexicon
{
    /// <summary>The directory the per-language word lists live in.</summary>
    internal const string DirectoryPath = "content/profanity/";

    /// <summary>The member each document holds its words under.</summary>
    internal const string TermsMember = "#/terms";

    private readonly IReadOnlyDictionary<ProfanityLanguage, IReadOnlyList<string>> _terms;

    private ProfanityLexicon(IReadOnlyDictionary<ProfanityLanguage, IReadOnlyList<string>> terms) =>
        _terms = terms;

    /// <summary>Every language a name is filtered in, in declaration order.</summary>
    /// <remarks>
    /// Read off the enum rather than off the loaded documents, so a language whose file went missing
    /// is a failure at <see cref="Read"/> rather than a language that quietly stopped being checked.
    /// </remarks>
    internal static IReadOnlyList<ProfanityLanguage> Languages { get; } =
        Array.AsReadOnly(Enum.GetValues<ProfanityLanguage>());

    /// <summary>The document one language's words are authored in.</summary>
    /// <param name="language">One of <see cref="Languages"/>.</param>
    /// <returns>The snapshot-relative path, e.g. <c>content/profanity/en.json</c>.</returns>
    internal static string DocumentFor(ProfanityLanguage language) =>
        DirectoryPath + language.ToString().ToLowerInvariant() + ".json";

    /// <summary>The <c>path#/pointer</c> one language's word list is read from.</summary>
    /// <param name="language">One of <see cref="Languages"/>.</param>
    /// <returns>The reference.</returns>
    internal static string TermsReference(ProfanityLanguage language) =>
        DocumentFor(language) + TermsMember;

    /// <summary>The words authored for one language, exactly as the file holds them. Never empty.</summary>
    /// <param name="language">One of <see cref="Languages"/>.</param>
    /// <returns>The terms, in the file's own order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="language"/> is not a declared language.</exception>
    internal IReadOnlyList<string> TermsFor(ProfanityLanguage language) =>
        _terms.TryGetValue(language, out var terms)
            ? terms
            : throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "This is not one of the languages 27 §1 authors a word list for. There is " +
                "deliberately no 0 member, so an uninitialised value arrives here rather than " +
                "reading as EN and reporting a German match as an English one.");

    /// <summary>
    /// Reads every language's word list. Throws rather than defaulting on anything missing,
    /// unauthorised, mistyped or empty.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The lexicon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">A language's document or its list is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A list holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A list is not an array of text.</exception>
    /// <exception cref="InvalidTunableException">A list is authorised but unusable — empty, blank or repeated.</exception>
    internal static ProfanityLexicon Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var byLanguage = new Dictionary<ProfanityLanguage, IReadOnlyList<string>>(Languages.Count);

        foreach (var language in Languages)
        {
            byLanguage[language] = ReadTerms(content, language);
        }

        return new ProfanityLexicon(byLanguage);
    }

    /// <summary>One language's list, with every way it could be useless refused separately.</summary>
    private static IReadOnlyList<string> ReadTerms(ContentSnapshot content, ProfanityLanguage language)
    {
        var reference = TermsReference(language);
        var authored = content.Read(reference);

        if (authored.IsUnauthorised)
        {
            throw new UnauthorisedTunableException(reference);
        }

        if (authored.Kind != ContentValueKind.Array)
        {
            throw new ContentTypeMismatchException(
                reference, authored.Kind, "an array of terms to match a candidate name against");
        }

        if (authored.Items.Count == 0)
        {
            throw new InvalidTunableException(
                reference,
                "The " + language + " word list is empty. This is refused rather than accepted as " +
                "'nothing is profane in this language', because the two are indistinguishable at " +
                "every later call site: a filter with no terms accepts EVERY name, on every account " +
                "created from here on, and reports nothing. 27 §1 filters in EN and DE at creation " +
                "and on every edit, so an empty list is a filter that has silently stopped existing.");
        }

        var terms = new string[authored.Items.Count];
        var seen = new HashSet<string>(terms.Length, StringComparer.Ordinal);

        for (var i = 0; i < terms.Length; i++)
        {
            var term = authored.Items[i].AsText(reference + "/" + Render(i));

            if (string.IsNullOrWhiteSpace(term))
            {
                throw new InvalidTunableException(
                    reference + "/" + Render(i),
                    "The " + language + " word list carries a blank term. Matching is by substring, " +
                    "and the empty string is a substring of every name there is — one blank row " +
                    "turns the filter into a rule that refuses everybody.");
            }

            if (!seen.Add(term))
            {
                throw new InvalidTunableException(
                    reference + "/" + Render(i),
                    "The " + language + " word list carries '" + term + "' twice. A repeated term " +
                    "matches nothing extra and makes the seed ceiling that guards this list count " +
                    "the same word twice.");
            }

            terms[i] = term;
        }

        return Array.AsReadOnly(terms);
    }

    /// <summary>Renders an index with <see cref="CultureInfo.InvariantCulture"/>, so a pointer reads the same on every host.</summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
