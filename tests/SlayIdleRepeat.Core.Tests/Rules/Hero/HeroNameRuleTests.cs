using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Hero;

/// <summary>
/// <see cref="HeroNameRule"/> — `07` §1's twelve characters, and `27` §1's EN + DE filter applied to
/// them.
/// </summary>
/// <remarks>
/// 🔒 <b>Every case here asserts WHICH check fired, never merely that the name was refused.</b> A
/// name can be refused for five different reasons, and a suite that asserted only "refused" could
/// have four of them broken while staying green — the length check could be filtering, the German
/// list could be reporting itself as the English one, and nothing would say so.
/// </remarks>
public sealed class HeroNameRuleTests
{
    private static readonly ProfanityLexicon Lexicon =
        ProfanityLexicon.Read(ProfanityDocuments.Shipped);

    /// <summary>An ordinary name is accepted, and comes back exactly as it was typed.</summary>
    [Fact]
    public void An_ordinary_name_is_accepted_unchanged()
    {
        var verdict = HeroNameRule.Validate("Ludwig", Lexicon);

        verdict.Accepted.ShouldBeTrue();
        verdict.Name!.Value.ShouldBe("Ludwig", "normalisation decides whether a name is allowed, never what it is.");
        verdict.Refusal.ShouldBeNull();
    }

    /// <summary>Blank is its own refusal — <c>null</c>, empty and whitespace all reach it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" ")]
    public void A_blank_name_is_refused_as_BLANK(string? candidate)
    {
        var verdict = HeroNameRule.Validate(candidate, Lexicon);

        verdict.Refusal.ShouldBe(HeroNameRefusal.BLANK);
        verdict.Name.ShouldBeNull();
    }

    /// <summary>
    /// The thirteenth character is refused and the twelfth is not — the boundary, from both sides.
    /// </summary>
    /// <remarks>
    /// Both sides, because a rule written with the wrong comparison passes the "too long" case on
    /// its own: <c>&gt;=</c> refuses a twelve-character name too, and nothing but the accepting half
    /// would notice.
    /// </remarks>
    [Fact]
    public void The_length_boundary_is_twelve_characters_from_both_sides()
    {
        HeroNameRule.Validate(new string('a', HeroNameRule.MaximumLength), Lexicon)
            .Accepted.ShouldBeTrue("07 §1 authors 12 characters, so 12 is legal.");

        HeroNameRule.Validate(new string('a', HeroNameRule.MaximumLength + 1), Lexicon)
            .Refusal.ShouldBe(HeroNameRefusal.TOO_LONG);
    }

    /// <summary>
    /// The limit counts what a player sees, not UTF-16 code units.
    /// </summary>
    /// <remarks>
    /// Twelve astral characters are twenty-four <c>char</c>s. A rule counting <c>Length</c> would
    /// refuse this name while accepting twelve Latin letters, so the limit would silently shorten
    /// depending on what the player typed.
    /// </remarks>
    [Fact]
    public void The_limit_counts_text_elements_rather_than_UTF16_code_units()
    {
        var astral = string.Concat(Enumerable.Repeat("\U0001F600", HeroNameRule.MaximumLength));

        astral.Length.ShouldBe(
            HeroNameRule.MaximumLength * 2,
            "the fixture only discriminates while these two counts differ.");

        HeroNameRule.Validate(astral, Lexicon).Accepted.ShouldBeTrue();
    }

    /// <summary>A control or format character is its own refusal, distinct from length and profanity.</summary>
    /// <remarks>
    /// Three shapes across the two categories the rule names: a newline (<c>Control</c>), a
    /// zero-width joiner and a right-to-left override (both <c>Format</c>). The negative control is
    /// its own case below — a non-breaking space, which is <c>SpaceSeparator</c> and therefore
    /// neither — so the arm is proven to be about those two categories rather than about "anything
    /// unusual".
    /// </remarks>
    [Theory]
    [InlineData("Lud\nwig")]
    [InlineData("Lud‍wig")]   // zero-width joiner: Format
    [InlineData("Lud‮wig")]   // right-to-left override: Format
    public void A_control_or_format_character_is_refused_as_DISALLOWED_CHARACTER(string candidate)
    {
        HeroNameRule.Validate(candidate, Lexicon).Refusal.ShouldBe(HeroNameRefusal.DISALLOWED_CHARACTER);
    }

    /// <summary>
    /// The negative control for the case above: a character that is unusual but is neither a control
    /// nor a format character is accepted.
    /// </summary>
    /// <remarks>
    /// A non-breaking space is <c>UnicodeCategory.SpaceSeparator</c>. It is not whitespace to
    /// <c>string.IsNullOrWhiteSpace</c>'s caller here — the name has letters either side of it — and
    /// it is invisible in neither of the two ways the rule refuses. Without this case, the theory
    /// above would hold just as well over a rule that refused every non-ASCII character, and the
    /// twelve-character limit would silently be a Latin-only limit.
    /// </remarks>
    [Fact]
    public void A_character_that_is_neither_control_nor_format_is_accepted()
    {
        HeroNameRule.Validate("Lud wig", Lexicon).Accepted.ShouldBeTrue();
    }

    /// <summary>An English match reports the English list, and carries the term it matched.</summary>
    [Fact]
    public void An_English_match_is_refused_as_PROFANE_EN_and_names_the_term()
    {
        var verdict = HeroNameRule.Validate(ProfanityDocuments.EnglishTerm, Lexicon);

        verdict.Refusal.ShouldBe(HeroNameRefusal.PROFANE_EN);
        verdict.Match.ShouldBe(ProfanityDocuments.EnglishTerm);
    }

    /// <summary>
    /// A German match reports the <b>German</b> list — the arm a single "profane" refusal would hide.
    /// </summary>
    /// <remarks>
    /// The fixture German term is twelve characters, which is exactly the hero-name limit, so this
    /// case also proves the length check is not what refused it.
    /// </remarks>
    [Fact]
    public void A_German_match_is_refused_as_PROFANE_DE_and_not_as_the_English_one()
    {
        var verdict = HeroNameRule.Validate(ProfanityDocuments.GermanTerm, Lexicon);

        verdict.Refusal.ShouldBe(HeroNameRefusal.PROFANE_DE);
        verdict.Match.ShouldBe(ProfanityDocuments.GermanTerm);
    }

    /// <summary>
    /// The filter runs over the normalised name, so case, diacritics, separators and the common
    /// digit substitutions do not evade it.
    /// </summary>
    /// <remarks>
    /// The negative control matters as much as the evasions: <c>wordbad</c> carries every letter of
    /// the term and is accepted, which is what proves the matcher is a substring match rather than
    /// a letter-set comparison.
    /// </remarks>
    [Theory]
    [InlineData("BADWORD", false)]
    [InlineData("b.a-d w0rd", false)]
    [InlineData("bädwörd", false)]
    [InlineData("b4dw0rd", false)]
    [InlineData("baaadword", false)]
    [InlineData("xxbadwordxx", false)]
    [InlineData("wordbad", true)]
    [InlineData("badwor", true)]
    public void The_filter_runs_over_the_normalised_name(string candidate, bool accepted)
    {
        HeroNameRule.Validate(candidate, Lexicon).Accepted.ShouldBe(accepted);
    }

    /// <summary>
    /// 🔒 The collapse arm is the <b>candidate's alone</b>, and this pins both halves of that trade —
    /// what it catches, and what it deliberately does not.
    /// </summary>
    /// <remarks>
    /// The three cases are one decision seen from three sides. A term with no doubled letter is
    /// caught however far a player stretches it. A term that <em>does</em> double a letter is not,
    /// because collapsing the candidate removes the doubling the term needs. And the reason that
    /// second half is the right trade is the third case: collapsing the TERM as well would shorten
    /// <c>grollen</c> to <c>grolen</c> and start refusing <c>Grolen</c>, an ordinary name — which is
    /// the shape of the false positive that makes a filter refuse place names. A suite asserting
    /// only the first case would read as though the collapse were free.
    /// </remarks>
    [Fact]
    public void Collapsing_catches_a_stretched_name_without_shortening_the_term()
    {
        HeroNameRule.Validate("baaadword", Lexicon)
            .Refusal.ShouldBe(
                HeroNameRefusal.PROFANE_EN,
                "'badword' doubles no letter, so a stretched spelling collapses straight onto it.");

        HeroNameRule.Validate("grrrolllen", Lexicon)
            .Accepted.ShouldBeTrue(
                "'grollen' doubles its l, and collapsing the candidate removes exactly that doubling. " +
                "This is the documented cost of not collapsing the term — see NameNormalisation.");

        HeroNameRule.Validate("Grolen", Lexicon)
            .Accepted.ShouldBeTrue(
                "and this is what that cost buys: collapsing the term would shorten 'grollen' to " +
                "'grolen' and refuse an ordinary name.");
    }

    /// <summary>Length is reported before profanity, so a player is never shown a slur back.</summary>
    [Fact]
    public void A_name_that_is_both_too_long_and_profane_reports_its_length()
    {
        var verdict = HeroNameRule.Validate(ProfanityDocuments.EnglishTerm + "aaaaaaaaaaaa", Lexicon);

        verdict.Refusal.ShouldBe(HeroNameRefusal.TOO_LONG);
        verdict.Match.ShouldBeNull();
    }

    /// <summary>`07` §1's default name is what the document says, and passes the shipped gate.</summary>
    [Fact]
    public void The_default_name_is_Wanderer_and_passes_its_own_filter()
    {
        HeroNameRule.DefaultText.ShouldBe("Wanderer");
        HeroNameRule.Default(Lexicon).Value.ShouldBe("Wanderer");
    }

    /// <summary>
    /// A word list that refuses the shipped default fails loudly rather than being special-cased.
    /// </summary>
    /// <remarks>
    /// Every account that has not chosen a name carries it, so a data set that refuses it is a data
    /// defect worth stopping on rather than a name to wave through.
    /// </remarks>
    [Fact]
    public void A_default_the_word_lists_refuse_is_a_loud_failure()
    {
        var hostile = ProfanityLexicon.Read(
            ProfanityDocuments.With(english: ProfanityDocuments.Terms("wanderer")));

        Should.Throw<InvalidOperationException>(() => HeroNameRule.Default(hostile))
            .Message.ShouldContain("PROFANE_EN");
    }

    /// <summary>A null lexicon is a caller defect, not a name that happens to be allowed.</summary>
    [Fact]
    public void A_missing_lexicon_throws_rather_than_accepting_everything()
    {
        Should.Throw<ArgumentNullException>(() => HeroNameRule.Validate("Ludwig", null!));
    }
}
