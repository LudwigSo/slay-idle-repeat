using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>content/profanity/</c> fixtures — one document per language, built as a
/// <see cref="ContentSnapshot"/> in memory.
/// </summary>
/// <remarks>
/// <para>
/// <c>Core.Tests</c> is hermetic, so these mirror the shipped files rather than reading them;
/// <c>SlayIdleRepeat.Application.Tests</c>' profanity suite reads the real files at the same
/// pointers. Neither half is sufficient alone: this proves the matcher is right about a word list,
/// that one proves the shipped list is a list at all.
/// </para>
/// <para>
/// 🔒 <b>The fixture terms are not the shipped terms, and that is deliberate.</b> Every case here is
/// about the MECHANISM — normalisation, substring matching, which language reported the match — and
/// a fixture spelling real slurs would make each assertion read as a claim about the word list
/// instead. <c>badword</c> and <c>schlechtwort</c> are nobody's profanity and cannot collide with
/// anything the shipped seed happens to carry.
/// </para>
/// </remarks>
internal static class ProfanityDocuments
{
    /// <summary>The English fixture term. Not a real one — see the type's remarks.</summary>
    internal const string EnglishTerm = "badword";

    /// <summary>The German fixture term, distinct from <see cref="EnglishTerm"/> in every letter that matters.</summary>
    internal const string GermanTerm = "schlechtwort";

    /// <summary>A term carrying a doubled letter, so the collapse arm can be told from the plain one.</summary>
    internal const string DoubledTerm = "grollen";

    /// <summary>The shipped set: both languages, each with its fixture terms.</summary>
    internal static ContentSnapshot Shipped { get; } = With();

    /// <summary>
    /// A snapshot with either language's list replaced. Pass <see cref="ContentValue.Unauthorised"/>
    /// for a deliberate <c>null</c> hole, <see cref="ContentValue.EmptyArray"/> for the empty list,
    /// or omit a parameter to keep the fixture value.
    /// </summary>
    internal static ContentSnapshot With(ContentValue? english = null, ContentValue? german = null) =>
        new(
            ContentVersion.FromHex(new string('c', ContentVersion.HexLength)),
            [
                Document(ProfanityLanguage.EN, english ?? Terms(EnglishTerm, DoubledTerm)),
                Document(ProfanityLanguage.DE, german ?? Terms(GermanTerm)),
            ]);

    /// <summary>A snapshot holding only one language's document — the "a language went missing" case.</summary>
    internal static ContentSnapshot Only(ProfanityLanguage language) =>
        new(
            ContentVersion.FromHex(new string('c', ContentVersion.HexLength)),
            [Document(language, Terms(EnglishTerm))]);

    /// <summary>A snapshot whose named language's document has an arbitrary root value.</summary>
    /// <remarks>For the "the document is not an object" refusals, which <see cref="With"/> cannot express.</remarks>
    internal static ContentSnapshot Root(ProfanityLanguage language, ContentValue root) =>
        new(
            ContentVersion.FromHex(new string('c', ContentVersion.HexLength)),
            [new ContentDocument(ProfanityLexicon.DocumentFor(language), root)]);

    /// <summary>A <c>terms</c> array of the shape a language document carries.</summary>
    internal static ContentValue Terms(params string[] terms) =>
        ContentValue.Array(terms.Select(ContentValue.Text));

    private static ContentDocument Document(ProfanityLanguage language, ContentValue terms) =>
        new(
            ProfanityLexicon.DocumentFor(language),
            ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["language"] = ContentValue.Text(language.ToString()),
                ["terms"] = terms,
            }));
}
