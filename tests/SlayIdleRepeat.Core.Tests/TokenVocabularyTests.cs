using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// The two open string vocabularies the domain has grown — daily-counter keys and
/// <c>CurrencyChanged.Reason</c> tokens — kept distinct, well formed, and visible. Both fail
/// silently on collision: a daily-counter collision breaks "once per game day", and a Reason
/// collision merges two income sources that can no longer be told apart in reporting.
/// </summary>
public sealed class TokenVocabularyTests
{
    private static readonly Regex Token = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Every token constant this suite knows about today, transcribed by hand rather than derived
    /// from the reflection below — a floor built from the same filter it floors would agree with itself.
    /// </summary>
    private static readonly string[] Floor =
    {
        "EnergyRegenReason",
        "DailyRefillReason",
        "DailyRunCounter",
    };

    /// <summary>
    /// The two vocabularies are checked together deliberately: nothing in the domain enforces that
    /// they stay separate namespaces, and a collision between them is just as unreadable in reports.
    /// </summary>
    [Fact]
    public void No_two_declared_tokens_collide()
    {
        var declared = DeclaredTokens();

        var collisions = declared
            .GroupBy(t => t.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"'{group.Key}' is declared by {string.Join(" and ", group.Select(t => t.Declaration))}. " +
                "Two declarations of one string make idempotence or income attribution unanswerable, " +
                "and neither failure is visible until a report is wrong or a grant stops paying.");

        collisions.ShouldBeEmpty();
    }

    /// <summary>
    /// Blankness is already refused elsewhere; what isn't is <c>"Daily Free Refill"</c> or
    /// <c>"dailyFreeRefill"</c> landing in a report column beside tokens spelled the other way.
    /// </summary>
    [Fact]
    public void Every_declared_token_is_a_lower_snake_case_identifier()
    {
        DeclaredTokens()
            .Where(t => !Token.IsMatch(t.Value))
            .Select(t => $"{t.Declaration} is '{t.Value}', which is not lower_snake_case.")
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The two rules above hold vacuously over an empty set. The set is built by a name filter that a
    /// rename could empty silently, so the constants known today are floored by name here.
    /// </summary>
    [Fact]
    public void The_token_constants_this_file_watches_are_all_present()
    {
        var declarations = DeclaredTokens().Select(t => t.Constant).ToArray();

        Floor.Except(declarations, StringComparer.Ordinal)
            .Select(missing =>
                $"the token constant '{missing}' is gone. This file finds tokens by a NAME filter " +
                "(*Reason / *Counter over Core's const strings), so a rename drops the constant out " +
                "of both rules above and they pass over a smaller set — or, if every constant is " +
                "renamed, over nothing at all. Restore it, or lower this floor in the same commit " +
                "and say why.")
            .ShouldBeEmpty();

        declarations.Length.ShouldBeGreaterThanOrEqualTo(
            Floor.Length,
            "…and the set has not shrunk below the floor by some route the names above do not cover.");
    }

    /// <summary>
    /// Every <c>const string</c> in <c>Core</c> whose name ends in <c>Reason</c> or <c>Counter</c>,
    /// found by reflection so a token added later is covered automatically.
    /// </summary>
    private static (string Constant, string Value, string Declaration)[] DeclaredTokens() =>
        typeof(GameContext).Assembly
            .GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
                .Where(f => f.Name.EndsWith("Reason", StringComparison.Ordinal) ||
                            f.Name.EndsWith("Counter", StringComparison.Ordinal))

                // The one exclusion, kept inline so a second can't be added quietly: BlankReason is a
                // refusal message, not a token, and it ends in "Reason" so the name filter catches it.
                .Where(f => !f.Name.Equals("BlankReason", StringComparison.Ordinal))
                .Select(f => (
                    Constant: f.Name,
                    Value: (string)f.GetRawConstantValue()!,
                    Declaration: $"{t.Name}.{f.Name}")))
            .OrderBy(t => t.Declaration, StringComparer.Ordinal)
            .ToArray();
}
