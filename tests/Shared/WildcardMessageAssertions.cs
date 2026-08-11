using System.Text;
using System.Text.RegularExpressions;
using Shouldly;

namespace SlayIdleRepeat.TestSupport;

/// <summary>
/// <c>ShouldMatchWildcard</c> — the one idiom Shouldly has no equivalent for, reproduced exactly
/// so M1-00's assertion-library migration could not weaken the 19 assertions that used it.
/// </summary>
/// <remarks>
/// <para>
/// The assertion library this repository used before M1-00 — named in the M1-00 row of
/// <c>IMPLEMENTATION_TRACKER.md</c>, and removed because its next major went to a paid commercial
/// licence — matched an exception message against a <b>wildcard pattern</b>, spelled
/// <c>WithMessage("*16.6*")</c>. Shouldly offers <c>ShouldContain</c> (substring) and
/// <c>ShouldMatch</c> (regex), and <b>neither is that assertion</b>:
/// </para>
/// <list type="bullet">
///   <item><c>ShouldContain("16.6")</c> drops the anchoring — a pattern without a leading or
///   trailing <c>*</c> is an equality check in wildcard-land, and rewriting every pattern as a
///   substring silently turns those into something weaker.</item>
///   <item><c>ShouldContain</c> also loses <b>order</b> across an internal wildcard.
///   <c>"*09 §2*unreviewed*"</c> demands "09 §2" and then "unreviewed" after it; two
///   <c>ShouldContain</c> calls accept them in either order, which is a different assertion —
///   and this repository uses those patterns to pin <i>which rule fired</i> (steering S2).</item>
///   <item><c>ShouldMatch</c> is a regex, so every <c>.</c> in a pattern like <c>"*16.6*"</c>
///   would silently become "any character".</item>
/// </list>
/// <para>
/// The semantics below were established empirically — by running the old assertion against
/// crafted inputs before it was removed — not assumed from documentation:
/// </para>
/// <list type="bullet">
///   <item><b>Case-insensitive.</b> <c>"*hello*"</c> matches <c>"Hello World"</c>.</item>
///   <item><b>Whole-string anchored.</b> <c>"abc"</c> does <b>not</b> match <c>"abcdef"</c>;
///   only <c>"*abc*"</c> does.</item>
///   <item><b>Ordered.</b> <c>"*beta*alpha*"</c> does not match <c>"alpha then beta"</c>.</item>
///   <item><c>*</c> is any run of characters (including none); <c>?</c> is exactly one.</item>
///   <item>Every other character is <b>literal</b> — regex metacharacters included, so the
///   <c>.</c> in <c>"*16.6*"</c> means a full stop.</item>
///   <item><c>*</c> matches newlines too: these patterns are applied to multi-line messages.</item>
/// </list>
/// <para>
/// 🔒 Prefer a plain Shouldly assertion when one actually fits. Reach for this only where the
/// wildcard semantics are the point — chiefly exception messages, where a fragment pins which
/// rule fired without pinning the whole sentence.
/// </para>
/// <para>
/// Compiled into <c>SlayIdleRepeat.Core.Tests</c> and <c>SlayIdleRepeat.Application.Tests</c> as a
/// linked <c>&lt;Compile Include="..\Shared\*.cs" /&gt;</c>. It is deliberately <b>not</b> its own
/// project: <c>build/ci/test-suites.json</c> discovers suites with the glob
/// <c>tests/*/*.csproj</c> and requires every one it finds to contain tests, so a helper project
/// under <c>tests/</c> would fail the build as an empty suite. Link it into another suite the
/// same way when that suite first needs it.
/// </para>
/// </remarks>
public static class WildcardMessageAssertions
{
    /// <summary>
    /// Asserts that <paramref name="actual"/> matches <paramref name="pattern"/> under the
    /// wildcard rules described on this class: case-insensitive, whole-string, <c>*</c> = any run,
    /// <c>?</c> = exactly one character, everything else literal.
    /// </summary>
    /// <param name="actual">The string under test — usually an <c>Exception.Message</c>.</param>
    /// <param name="pattern">The wildcard pattern. Wrap in <c>*</c> to match a fragment.</param>
    /// <param name="customMessage">Shouldly's custom failure message, as elsewhere.</param>
    public static void ShouldMatchWildcard(this string? actual, string pattern, string? customMessage = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        // Shouldly reports the regex on failure, which is noise when the author wrote a
        // wildcard. Do the match here and hand Shouldly a boolean plus a message that quotes
        // the pattern the author actually wrote and the string it was tested against.
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

    /// <summary>
    /// Translates a wildcard pattern to an anchored regex, escaping every character that is not
    /// <c>*</c> or <c>?</c> so regex metacharacters stay literal.
    /// </summary>
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
