using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>
/// Whether a candidate hero name may be used — the one gate, applied at creation and on every edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the two documents say, and what was ruled between them.</b> `07` §1 gives the hero name
/// twelve characters and the word "profanity-filtered" and nothing else: no language list, no rule
/// about edits, no behaviour on a match. `27` §1 gives a guild's name and tag the only profanity
/// specification the design set actually writes down — filtered in EN and DE, at creation and on
/// every edit. The M4 kickoff ruled that the hero name is held to that same standard, because running
/// two different filters on two name fields in one product is the worse outcome and `27`'s is the one
/// that exists. The hero name keeps its own limit of <see cref="MaximumLength"/>; `27`'s sixteen is a
/// different field's number and is deliberately not borrowed with the rest.
/// </para>
/// <para>
/// 🔒 <b>Applied on mutation, never on rehydration.</b> The word lists are content and change without
/// a build, so a term added tomorrow would make today's stored names unloadable — an authoring edit
/// turning into an account outage, which is the same trade the Energy ceiling and the inventory
/// capacity already refuse. A stored name is loaded as written; a name being <em>set</em> passes here
/// first, and that is what "on every edit" means mechanically.
/// </para>
/// <para>
/// The checks run cheapest-first and the first one to fire wins, so a name that is both too long and
/// profane reports its length. That is deliberate: the player fixes one thing at a time, and telling
/// them a name is too long is the answer that does not require quoting a slur back at them.
/// </para>
/// </remarks>
internal static class HeroNameRule
{
    /// <summary>
    /// The longest a hero name may be, in text elements. Twelve, from `07` §1's own property table.
    /// </summary>
    /// <remarks>
    /// A code constant rather than a tuning key, and that is the convention rather than an oversight:
    /// `14` §6 puts numbers marked 📐 in <c>game-data/tuning/</c>, and this one carries no marker —
    /// it is a field's shape, like the six gear slots, not an economic dial anything sweeps.
    /// </remarks>
    internal const int MaximumLength = 12;

    /// <summary>
    /// The name a player who has not chosen one carries. "Wanderer", from `07` §1.
    /// </summary>
    /// <remarks>
    /// Authored text rather than a placeholder, so it is written once here and never spelled at a
    /// call site. <see cref="Default"/> is the validated form, and it goes through the same gate as a
    /// player's own choice — a shipped default that the shipped word lists refuse is a bug worth
    /// failing on rather than special-casing past.
    /// </remarks>
    internal const string DefaultText = "Wanderer";

    /// <summary>Decides a candidate name.</summary>
    /// <param name="candidate">The name as the player typed it. <see langword="null"/> is refused, not thrown.</param>
    /// <param name="lexicon">The loaded word lists. Never null, and never empty — the reader refuses an empty list.</param>
    /// <returns>The verdict: the accepted name, or which check fired and on what.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lexicon"/> is null.</exception>
    internal static HeroNameVerdict Validate(string? candidate, ProfanityLexicon lexicon)
    {
        ArgumentNullException.ThrowIfNull(lexicon);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Refused(HeroNameRefusal.BLANK);
        }

        // Text elements, not chars: an emoji or an accented letter can be two UTF-16 code units, and
        // counting those would give the player a limit that shortens depending on what they type.
        if (new StringInfo(candidate).LengthInTextElements > MaximumLength)
        {
            return Refused(HeroNameRefusal.TOO_LONG);
        }

        foreach (var character in candidate)
        {
            if (char.IsControl(character) ||
                CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format)
            {
                return Refused(HeroNameRefusal.DISALLOWED_CHARACTER);
            }
        }

        var normalised = NameNormalisation.Normalise(candidate);
        var collapsed = NameNormalisation.Collapse(normalised);

        foreach (var language in ProfanityLexicon.Languages)
        {
            var match = FirstMatch(lexicon.TermsFor(language), normalised, collapsed);

            if (match is not null)
            {
                return new HeroNameVerdict(null, RefusalFor(language), match);
            }
        }

        return new HeroNameVerdict(new HeroName(candidate), null, null);
    }

    /// <summary>The default name, validated through the same gate as a player's own choice.</summary>
    /// <param name="lexicon">The loaded word lists.</param>
    /// <returns>"Wanderer", as a checked name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lexicon"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The shipped word lists refuse the shipped default. A defect in the data set, not a player
    /// action — every account that has not chosen a name carries this one.
    /// </exception>
    internal static HeroName Default(ProfanityLexicon lexicon)
    {
        var verdict = Validate(DefaultText, lexicon);

        return verdict.Name ?? throw new InvalidOperationException(
            "The shipped default hero name '" + DefaultText + "' is refused by the shipped word " +
            "lists (" + verdict.Refusal + (verdict.Match is null ? "" : ", on '" + verdict.Match + "'") +
            "). 07 §1 makes it the name of every account that has not chosen one, so this is a data " +
            "defect that would name every such player something the game itself refuses.");
    }

    /// <summary>
    /// The first authored term that appears in either form of the candidate, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Both forms are searched with the term as authored: the plain form is what the term is written
    /// against, and the collapsed form catches a stretched spelling. Collapsing the term as well
    /// would shorten it into something innocent names contain — see <c>NameNormalisation</c>.
    /// </remarks>
    private static string? FirstMatch(IReadOnlyList<string> terms, string normalised, string collapsed)
    {
        for (var i = 0; i < terms.Count; i++)
        {
            var term = NameNormalisation.Normalise(terms[i]);

            if (term.Length == 0)
            {
                continue;
            }

            if (normalised.Contains(term, StringComparison.Ordinal) ||
                collapsed.Contains(term, StringComparison.Ordinal))
            {
                return terms[i];
            }
        }

        return null;
    }

    /// <summary>The refusal one language's word list produces.</summary>
    /// <remarks>
    /// An exhaustive switch with a throwing default rather than an arithmetic mapping, so a third
    /// language added to <see cref="ProfanityLanguage"/> without a refusal of its own fails loudly
    /// instead of reporting its matches as English ones.
    /// </remarks>
    private static HeroNameRefusal RefusalFor(ProfanityLanguage language) => language switch
    {
        ProfanityLanguage.EN => HeroNameRefusal.PROFANE_EN,
        ProfanityLanguage.DE => HeroNameRefusal.PROFANE_DE,
        _ => throw new ArgumentOutOfRangeException(
            nameof(language),
            language,
            "This language has a word list and no refusal of its own, so a match in it would be " +
            "reported as a match in another language. Add the HeroNameRefusal member in the same " +
            "commit as the ProfanityLanguage member."),
    };

    private static HeroNameVerdict Refused(HeroNameRefusal refusal) => new(null, refusal, null);
}
