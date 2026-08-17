using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// <see cref="ProfanityLexicon"/> — every way a word list can be useless, told apart from every
/// other way.
/// </summary>
/// <remarks>
/// 🔴 The case this suite exists for is <see cref="An_empty_word_list_is_refused_rather_than_read_as_nothing_is_profane"/>.
/// An empty list is what a file that failed to load reads as, and a filter with no terms accepts
/// every name there is — silently, on every account created from then on. That failure is invisible
/// at every later call site, so it has to be caught here.
/// </remarks>
public sealed class ProfanityLexiconTests
{
    /// <summary>Both languages `27` §1 names are read, each with the terms its file authors.</summary>
    [Fact]
    public void Both_languages_are_read_with_the_terms_their_documents_author()
    {
        var lexicon = ProfanityLexicon.Read(ProfanityDocuments.Shipped);

        lexicon.TermsFor(ProfanityLanguage.EN)
            .ShouldBe(new[] { ProfanityDocuments.EnglishTerm, ProfanityDocuments.DoubledTerm });

        lexicon.TermsFor(ProfanityLanguage.DE).ShouldBe(new[] { ProfanityDocuments.GermanTerm });
    }

    /// <summary>
    /// 🔴 An empty list is a <b>fault</b>, never "nothing is profane in this language".
    /// </summary>
    /// <remarks>
    /// Asserted per language, not once: a reader that checked only the first would leave the second
    /// able to load empty, and the two lists are read by one loop whose bound is easy to get wrong.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_empty_word_list_is_refused_rather_than_read_as_nothing_is_profane(bool english)
    {
        var content = english
            ? ProfanityDocuments.With(english: ContentValue.EmptyArray)
            : ProfanityDocuments.With(german: ContentValue.EmptyArray);

        var thrown = Should.Throw<InvalidTunableException>(() => ProfanityLexicon.Read(content));

        thrown.Message.ShouldContain(english ? "EN word list is empty" : "DE word list is empty");
        thrown.Message.ShouldContain("accepts EVERY name");
    }

    /// <summary>A deliberate <c>null</c> is told apart from an empty list and from a missing document.</summary>
    [Fact]
    public void An_unauthorised_word_list_is_refused_as_the_hole_it_is()
    {
        Should.Throw<UnauthorisedTunableException>(
                () => ProfanityLexicon.Read(ProfanityDocuments.With(english: ContentValue.Unauthorised)))
            .Message.ShouldContain(ProfanityLexicon.TermsReference(ProfanityLanguage.EN));
    }

    /// <summary>
    /// A language whose document is not in the set at all fails — the filter never silently runs in
    /// one language.
    /// </summary>
    /// <remarks>
    /// The third door, distinct from the two above: `27` §1 filters in EN <em>and</em> DE, so a set
    /// carrying only one of them is not a set with a smaller filter, it is a set with no German
    /// filter and nothing saying so.
    /// </remarks>
    [Fact]
    public void A_language_whose_document_is_missing_is_refused()
    {
        Should.Throw<MissingContentException>(
                () => ProfanityLexicon.Read(ProfanityDocuments.Only(ProfanityLanguage.EN)))
            .Message.ShouldContain(ProfanityLexicon.DocumentFor(ProfanityLanguage.DE));
    }

    /// <summary>A blank term is refused: the empty string is a substring of every name there is.</summary>
    [Fact]
    public void A_blank_term_is_refused_because_it_would_refuse_everybody()
    {
        Should.Throw<InvalidTunableException>(
                () => ProfanityLexicon.Read(
                    ProfanityDocuments.With(english: ProfanityDocuments.Terms("badword", "   "))))
            .Message.ShouldContain("substring of every name");
    }

    /// <summary>A repeated term is refused rather than deduplicated.</summary>
    [Fact]
    public void A_repeated_term_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => ProfanityLexicon.Read(
                    ProfanityDocuments.With(english: ProfanityDocuments.Terms("badword", "badword"))))
            .Message.ShouldContain("twice");
    }

    /// <summary>A list that is not an array at all is a type mismatch, not an empty list.</summary>
    [Fact]
    public void A_word_list_of_the_wrong_shape_is_refused_as_a_type_mismatch()
    {
        Should.Throw<ContentTypeMismatchException>(
            () => ProfanityLexicon.Read(
                ProfanityDocuments.With(english: ContentValue.Text("badword"))));
    }

    /// <summary>
    /// The subject set is floored by identity: both `27` §1 languages are declared, and the reader
    /// reads every one of them.
    /// </summary>
    /// <remarks>
    /// Without this, deleting a member from <see cref="ProfanityLanguage"/> would make every case
    /// above hold over one language and nothing would say so (steering S3).
    /// </remarks>
    [Fact]
    public void The_language_set_is_the_two_27_section_1_names()
    {
        ProfanityLexicon.Languages.ShouldBe(
            new[] { ProfanityLanguage.EN, ProfanityLanguage.DE },
            "27 §1 filters in EN and DE, and game-data/loc/ holds exactly those two files.");

        var lexicon = ProfanityLexicon.Read(ProfanityDocuments.Shipped);

        foreach (var language in ProfanityLexicon.Languages)
        {
            lexicon.TermsFor(language).ShouldNotBeEmpty();
        }
    }

    /// <summary>An undeclared language is refused rather than answering the first list.</summary>
    [Fact]
    public void An_undeclared_language_is_refused_rather_than_answered()
    {
        var lexicon = ProfanityLexicon.Read(ProfanityDocuments.Shipped);

        Should.Throw<ArgumentOutOfRangeException>(() => lexicon.TermsFor(default));
    }

    /// <summary>The document path is derived from the language, so the two can never disagree.</summary>
    [Fact]
    public void Each_language_names_its_own_document()
    {
        ProfanityLexicon.DocumentFor(ProfanityLanguage.EN).ShouldBe("content/profanity/en.json");
        ProfanityLexicon.DocumentFor(ProfanityLanguage.DE).ShouldBe("content/profanity/de.json");
    }
}
