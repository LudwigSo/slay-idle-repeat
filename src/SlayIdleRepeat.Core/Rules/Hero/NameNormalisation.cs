using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>
/// Turns a candidate name into the two forms a word list is matched against.
/// </summary>
/// <remarks>
/// <para>
/// The whole mechanism of the name filter, in one place, because the alternative is a matcher whose
/// behaviour is spread over the call sites that use it. The word lists themselves are content and
/// live in <c>content/profanity/</c>; nothing here knows a single word.
/// </para>
/// <para>
/// <b>The steps, in order.</b> Each one closes an evasion that costs a player nothing to try:
/// </para>
/// <list type="number">
///   <item><b>Case-fold</b> — invariant, never the current culture: Turkish lowercases <c>I</c> to a
///   dotless <c>ı</c>, so a culture-sensitive fold would filter different words on a Turkish phone.</item>
///   <item><b>Expand the ligatures a decomposition cannot reach</b> — <c>ß</c> is not a decorated
///   <c>s</c> and survives step 3 untouched, so it is expanded to <c>ss</c> by hand. The German word
///   list is authored in the expanded spelling for this reason.</item>
///   <item><b>Decompose and drop the marks</b> — <c>ä</c> becomes <c>a</c>, and so does every accent,
///   cedilla and umlaut in every language, without a table of pairs to keep up to date.</item>
///   <item><b>Fold the common substitutions</b> — digits and punctuation that read as letters.</item>
///   <item><b>Keep only <c>a</c>–<c>z</c></b> — spaces, punctuation and anything left over are
///   dropped, so inserted separators stop being an evasion.</item>
/// </list>
/// <para>
/// 🔒 <b>Two forms, not one, and the second is the candidate's alone.</b> <see cref="Normalise"/>
/// produces the plain form and <see cref="Collapse"/> squeezes every run of one letter down to a
/// single letter. A matcher compares the authored term against <em>both</em> forms of the candidate,
/// which catches a stretched name without corrupting the term: collapsing the term as well would turn
/// a slur with a doubled letter into a shorter string that innocent names contain, and the filter
/// would start refusing place names. The cost of the asymmetry is stated rather than hidden — a name
/// that stretches a letter the term does not double evades the filter.
/// </para>
/// <para>
/// ⚠️ <b>Matching is by substring, and substring matching has false positives by construction</b>
/// (the standard example is the English town whose name contains a slur). The seed word lists are
/// deliberately short and chosen to minimise it, and the curated lists are M17's — see
/// <c>content/profanity/en.json</c>, which records the terms left out for exactly this reason.
/// </para>
/// </remarks>
internal static class NameNormalisation
{
    /// <summary>
    /// Characters that read as a letter, and the letter they read as.
    /// </summary>
    /// <remarks>
    /// Deliberately short. Every pair here is a substitution a filter of this kind is expected to
    /// see; a longer table buys diminishing evasion coverage and pays for it in false positives,
    /// because each pair makes more innocent strings normalise onto a term.
    /// </remarks>
    private static readonly Dictionary<char, char> Substitutions = new()
    {
        ['0'] = 'o',
        ['1'] = 'i',
        ['3'] = 'e',
        ['4'] = 'a',
        ['5'] = 's',
        ['7'] = 't',
        ['8'] = 'b',
        ['9'] = 'g',
        ['@'] = 'a',
        ['$'] = 's',
        ['!'] = 'i',
        ['|'] = 'i',
        ['+'] = 't',
    };

    /// <summary>The plain normalised form: lowercase, mark-free, substitution-folded, <c>a</c>–<c>z</c> only.</summary>
    /// <param name="candidate">The text as the player typed it.</param>
    /// <returns>The normalised form. Empty when the candidate carries no letters at all.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidate"/> is null.</exception>
    internal static string Normalise(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var expanded = Expand(candidate.ToLowerInvariant());
        var decomposed = expanded.Normalize(NormalizationForm.FormD);
        var kept = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var folded = Substitutions.TryGetValue(character, out var letter) ? letter : character;

            if (folded is >= 'a' and <= 'z')
            {
                kept.Append(folded);
            }
        }

        return kept.ToString();
    }

    /// <summary>The same string with every run of one letter squeezed to a single letter.</summary>
    /// <param name="normalised">A string <see cref="Normalise"/> produced.</param>
    /// <returns>The collapsed form.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="normalised"/> is null.</exception>
    internal static string Collapse(string normalised)
    {
        ArgumentNullException.ThrowIfNull(normalised);

        var collapsed = new StringBuilder(normalised.Length);

        foreach (var character in normalised)
        {
            if (collapsed.Length == 0 || collapsed[^1] != character)
            {
                collapsed.Append(character);
            }
        }

        return collapsed.ToString();
    }

    /// <summary>
    /// The ligatures and digraphs a Unicode decomposition leaves alone, expanded by hand.
    /// </summary>
    /// <remarks>
    /// <c>ß</c> is the one that matters for `27` §1's German list: it is a letter in its own right
    /// rather than a decorated <c>s</c>, so <c>FormD</c> does not touch it and a word list spelled
    /// with it would never match a name spelled without it, or the reverse.
    /// </remarks>
    private static string Expand(string lowered) => lowered
        .Replace("ß", "ss", StringComparison.Ordinal)
        .Replace("æ", "ae", StringComparison.Ordinal)
        .Replace("œ", "oe", StringComparison.Ordinal)
        .Replace("ø", "o", StringComparison.Ordinal)
        .Replace("đ", "d", StringComparison.Ordinal)
        .Replace("ł", "l", StringComparison.Ordinal);
}
