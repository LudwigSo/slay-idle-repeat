using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 The two <b>open string vocabularies</b> the domain has grown — `30` §2.3's daily-counter keys and
/// `30` §7's <c>CurrencyChanged.Reason</c> tokens — kept distinct, well formed, and visible.
/// </summary>
/// <remarks>
/// 🔴 Both had their first two occupants and no registry, uniqueness rule or floor, and both fail
/// <em>silently</em>: a daily-counter collision breaks "once per game day" (<c>Player.CountDaily</c>
/// takes any string, so a later handler reusing the token makes the day look already-run), and a
/// <c>Reason</c> collision merges two income sources in `21` §8.3's <c>income_attribution.csv</c>,
/// which cannot be told apart afterwards.
/// <para>
/// 🔒 A check over the tokens <b>in use</b>, not a closed enum: `30` §2.3's five daily-reset systems do
/// not exist yet and freezing their keys would invent them. Reflecting over declared constants adds no
/// token and forbids no future one — it only says two declarations must not collide.
/// </para>
/// <para>
/// ⚠️ The subject set is floored by identity: a reflection filter over field names can be emptied by a
/// rename, and would then report success forever over a domain where every token had collided.
/// </para>
/// </remarks>
public sealed class TokenVocabularyTests
{
    /// <summary>
    /// 🔒 `21` §8.3 groups on these tokens and `30` §2.3 keys idempotence on them, so both are
    /// <c>lower_snake_case</c> identifiers — never a sentence, never a number, never blank.
    /// </summary>
    private static readonly Regex Token = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// 🔒 The identity floor: every token constant this suite knows about today.
    /// </summary>
    /// <remarks>
    /// Transcribed by hand against the reflection below, which is the only way a floor can work — a
    /// list derived from the same filter it floors would agree with itself.
    /// </remarks>
    private static readonly string[] Floor =
    {
        "EnergyRegenReason",
        "DailyRefillReason",
        "DailyRunCounter",
    };

    /// <summary>
    /// 🔒 No two declared tokens are the same string, whatever they are declared for.
    /// </summary>
    /// <remarks>
    /// The two vocabularies are checked <b>together</b> deliberately: they are different namespaces
    /// conceptually, but nothing in the domain enforces that, both reach `21` §8.3's reporting, and a
    /// reader debugging <c>income_attribution.csv</c> against a daily counter of the same name could not
    /// tell which they were looking at. ⚠️ If a future task genuinely needs one string in both roles,
    /// this is the rule to come and argue with.
    /// </remarks>
    [Fact]
    public void No_two_declared_tokens_collide()
    {
        var declared = DeclaredTokens();

        var collisions = declared
            .GroupBy(t => t.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"'{group.Key}' is declared by {string.Join(" and ", group.Select(t => t.Declaration))}. " +
                "30 §2.3 keys BEGIN_SESSION's per-game-day idempotence on a daily-counter token and " +
                "21 §8.3 groups income_attribution.csv on a Reason token; two declarations of one " +
                "string make one of those questions unanswerable, and neither failure is visible " +
                "until a report is wrong or a grant stops paying.");

        collisions.ShouldBeEmpty();
    }

    /// <summary>🔒 Every declared token is a <c>lower_snake_case</c> identifier.</summary>
    /// <remarks>
    /// <c>CurrencyChanged</c> refuses a blank <c>Reason</c> and <c>Player.CountDaily</c> refuses a
    /// blank key, so blankness is already closed. What is not is <c>"Daily Free Refill"</c> or
    /// <c>"dailyFreeRefill"</c> — both legal strings, both of which would land in a CSV column the
    /// economy dashboards group on, beside tokens spelled the other way.
    /// </remarks>
    [Fact]
    public void Every_declared_token_is_a_lower_snake_case_identifier()
    {
        DeclaredTokens()
            .Where(t => !Token.IsMatch(t.Value))
            .Select(t => $"{t.Declaration} is '{t.Value}', which is not lower_snake_case.")
            .ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 The subject set is the one this file was written against — the vacuity check under both
    /// rules above (steering <b>S3</b>).
    /// </summary>
    /// <remarks>
    /// Both rules are of the shape "no member of set S does X" and hold vacuously over an empty S.
    /// The set is built by a <b>name filter</b> over <c>const string</c> fields, which a rename
    /// empties silently — so the three constants that exist today are asserted by name, and a fourth
    /// arriving is covered automatically by the filter.
    /// </remarks>
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
    /// Every <c>const string</c> in <c>Core</c> whose name ends in <c>Reason</c> or <c>Counter</c>.
    /// </summary>
    /// <remarks>
    /// Reflection rather than a transcription, so a token added by a later milestone is covered on
    /// the commit that adds it rather than on the commit somebody remembers to. The same mechanism
    /// <c>SubjectSetFloorTests.TypeNameConstants()</c> uses over in the architecture suite, and it
    /// carries the same known limit, which is what the floor above is for.
    /// </remarks>
    private static (string Constant, string Value, string Declaration)[] DeclaredTokens() =>
        typeof(GameContext).Assembly
            .GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
                .Where(f => f.Name.EndsWith("Reason", StringComparison.Ordinal) ||
                            f.Name.EndsWith("Counter", StringComparison.Ordinal))

                // ⚠️ THE ONE EXCLUSION, and it is inline rather than in a list so a second one cannot
                // be added quietly — the idiom SubjectSetFloorTests uses for IClockPort, and for the
                // same reason. CurrencyChanged.BlankReason is the REFUSAL MESSAGE shown when a
                // Reason is blank; it ends in "Reason" and is a prose paragraph, not a token. Caught
                // by this file's own lower_snake_case rule on its first run, which is the filter
                // saying it was too wide rather than the constant being wrong. A future exclusion is
                // an argument to have here, not a name to append somewhere.
                .Where(f => !f.Name.Equals("BlankReason", StringComparison.Ordinal))
                .Select(f => (
                    Constant: f.Name,
                    Value: (string)f.GetRawConstantValue()!,
                    Declaration: $"{t.Name}.{f.Name}")))
            .OrderBy(t => t.Declaration, StringComparer.Ordinal)
            .ToArray();
}
