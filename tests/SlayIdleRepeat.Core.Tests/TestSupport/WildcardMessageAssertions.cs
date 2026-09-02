using System.Text;
using System.Text.RegularExpressions;
using Shouldly;

namespace SlayIdleRepeat.Core.Tests.TestSupport;

/// <summary>
/// Wildcard string matching that Shouldly has no built-in equivalent for: case-insensitive,
/// whole-string anchored, <c>*</c> = any run of characters (including none, and including
/// newlines), <c>?</c> = exactly one character, everything else literal (regex metacharacters
/// included, so <c>.</c> in <c>"*16.6*"</c> means a full stop).
/// </summary>
/// <remarks>
/// <para>
/// Shouldly's <b>string</b> overloads of <c>ShouldContain</c>, <c>ShouldNotContain</c>,
/// <c>ShouldStartWith</c> and <c>ShouldEndWith</c> default to <c>Case.Insensitive</c>, unlike the
/// FluentAssertions assertion this replaces — pass <c>Case.Sensitive</c> explicitly whenever case
/// is part of the claim, or the assertion is silently weaker than it looks. The collection
/// overloads (<c>someList.ShouldContain(item)</c>) are unaffected; they already compare exactly.
/// </para>
/// <para>
/// It lives in this suite rather than in a shared folder because this suite holds all but a
/// handful of its call sites, and <c>build/ci/test-suites.json</c> requires every
/// <c>tests/*/*.csproj</c> it discovers to contain tests — so a helper project of its own would be
/// a permanently red empty suite.
/// </para>
/// </remarks>
internal static class WildcardMessageAssertions
{
    /// <summary>Asserts that <paramref name="actual"/> matches <paramref name="pattern"/> under the wildcard rules described on this class.</summary>
    /// <param name="actual">The string under test — usually an <c>Exception.Message</c>.</param>
    /// <param name="pattern">The wildcard pattern. Wrap in <c>*</c> to match a fragment.</param>
    /// <param name="customMessage">Shouldly's custom failure message, as elsewhere.</param>
    public static void ShouldMatchWildcard(this string? actual, string pattern, string? customMessage = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        // Matched manually so a failure quotes the wildcard pattern, not the translated regex.
        var matched = actual is not null
                      && Regex.IsMatch(actual, ToRegexPattern(pattern),
                                       RegexOptions.IgnoreCase | RegexOptions.Singleline);

        matched.ShouldBeTrue(
            customMessage
            ?? $"Expected the string to match the wildcard pattern{Environment.NewLine}"
               + $"    {pattern}{Environment.NewLine}"
               + $"but it was{Environment.NewLine}"
               + $"    {actual ?? "<null>"}");
    }

    /// <summary>Translates a wildcard pattern to an anchored regex, escaping everything but <c>*</c>/<c>?</c>.</summary>
    private static string ToRegexPattern(string pattern)
    {
        var builder = new StringBuilder("^");

        foreach (var c in pattern)
        {
            builder.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }

        return builder.Append('$').ToString();
    }
}
