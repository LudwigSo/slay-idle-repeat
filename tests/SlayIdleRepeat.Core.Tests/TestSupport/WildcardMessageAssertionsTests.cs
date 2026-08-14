using Shouldly;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.TestSupport;

/// <summary>
/// Pins <see cref="WildcardMessageAssertions.ShouldMatchWildcard"/> against the exception-message
/// wildcard behaviour it replaced.
/// </summary>
/// <remarks>
/// 🔒 The helper is the one part of the migration that is <b>new code</b> rather than a translation, and
/// it carries 19 assertions that pin <i>which rule fired</i>. A helper that matched everything would take
/// all 19 green with nothing going red.
/// <para>
/// So the negative cases matter more than the positive ones: each asserts a pattern that must <b>not</b>
/// match, and every one is a way a sloppy implementation could have been written — a substring check
/// (loses anchoring), a <c>ShouldContain</c> pair (loses order), a raw regex (metacharacters stop being
/// literal). The expectations were measured against the real assertion before the library was removed,
/// not inferred from its documentation.
/// </para>
/// </remarks>
public sealed class WildcardMessageAssertionsTests
{
    // ------------------------------------------------------------------ matching

    [Theory]
    // Fragment matching, the overwhelmingly common shape in this repository.
    [InlineData("Hello World", "*Hello*")]
    [InlineData("Hello World", "*World*")]
    [InlineData("Hello World", "*")]
    // Case-insensitive, in both directions. The old assertion matched "*infinit*" against a
    // message reading "Infinities are forbidden"; dropping this would have broken that site.
    [InlineData("Hello World", "*hello*")]
    [InlineData("hello world", "*HELLO*")]
    // Whole-string patterns with no wildcard are an equality check, not a substring check.
    [InlineData("abc", "abc")]
    [InlineData("abc", "ABC")]
    // '?' is exactly one character.
    [InlineData("abc", "a?c")]
    [InlineData("abc", "???")]
    // Internal wildcards, matched in order.
    [InlineData("alpha then beta", "*alpha*beta*")]
    [InlineData("09 §2 marker is unreviewed", "*09 §2*unreviewed*")]
    // Regex metacharacters are literal: the '.' in "*16.6*" is a full stop.
    [InlineData("see 14 §16.6 for the rule", "*16.6*")]
    [InlineData("a.c", "*a.c*")]
    [InlineData("cost is $5 (approx)", "*$5 (approx)*")]
    [InlineData("a+b", "*a+b*")]
    // '*' spans newlines — these patterns are applied to multi-line exception messages.
    [InlineData("first line\nsecond line", "*first*second*")]
    public void A_matching_pattern_passes(string actual, string pattern) =>
        Should.NotThrow(() => actual.ShouldMatchWildcard(pattern));

    [Theory]
    // 🔒 Anchoring. A pattern with no leading/trailing '*' is NOT a substring check. This is the
    // row that fails if the helper is quietly reimplemented as ShouldContain.
    [InlineData("abcdef", "abc")]
    [InlineData("abcdef", "def")]
    // 🔒 Order across an internal wildcard. This is the row that fails if the helper is
    // reimplemented as two independent ShouldContain calls — and order is what makes these
    // patterns pin which rule fired rather than merely which words appeared (steering S2).
    [InlineData("alpha then beta", "*beta*alpha*")]
    // 🔒 Metacharacters stay literal. This is the row that fails if the pattern is handed to
    // Regex unescaped, which would make "*16.6*" match "16X6".
    [InlineData("abc", "*a.c*")]
    [InlineData("16X6", "*16.6*")]
    [InlineData("aab", "*a+b*")]
    // '?' is exactly one character, not zero and not many.
    [InlineData("ac", "a?c")]
    [InlineData("abbc", "a?c")]
    // A fragment that simply is not there.
    [InlineData("Hello World", "*goodbye*")]
    [InlineData("", "*something*")]
    public void A_non_matching_pattern_fails(string actual, string pattern) =>
        Should.Throw<ShouldAssertException>(() => actual.ShouldMatchWildcard(pattern));

    // ------------------------------------------------------------------ edges

    [Fact]
    public void A_null_subject_fails_rather_than_matching_or_throwing_NullReference()
    {
        string? actual = null;

        Should.Throw<ShouldAssertException>(() => actual.ShouldMatchWildcard("*anything*"));
    }

    [Fact]
    public void A_null_subject_fails_even_against_the_match_everything_pattern() =>
        Should.Throw<ShouldAssertException>(() => ((string?)null).ShouldMatchWildcard("*"));

    [Fact]
    public void A_null_pattern_is_the_caller_s_bug_and_says_so() =>
        Should.Throw<ArgumentNullException>(() => "anything".ShouldMatchWildcard(null!));

    [Fact]
    public void An_empty_pattern_matches_only_the_empty_string()
    {
        Should.NotThrow(() => string.Empty.ShouldMatchWildcard(string.Empty));
        Should.Throw<ShouldAssertException>(() => "x".ShouldMatchWildcard(string.Empty));
    }

    // ------------------------------------------------------------------ reporting

    [Fact]
    public void The_failure_message_quotes_the_pattern_and_the_subject_so_the_reader_can_see_the_gap()
    {
        var thrown = Should.Throw<ShouldAssertException>(
            () => "the actual text".ShouldMatchWildcard("*the expected pattern*"));

        thrown.Message.ShouldContain("*the expected pattern*", Case.Sensitive);
        thrown.Message.ShouldContain("the actual text", Case.Sensitive);
    }

    [Fact]
    public void A_custom_message_replaces_the_default_one_the_way_it_does_everywhere_else_in_Shouldly()
    {
        var thrown = Should.Throw<ShouldAssertException>(
            () => "actual".ShouldMatchWildcard("*expected*", "16 §A7 ruling 13 pins this stream name"));

        thrown.Message.ShouldContain("16 §A7 ruling 13 pins this stream name", Case.Sensitive);
    }
}
