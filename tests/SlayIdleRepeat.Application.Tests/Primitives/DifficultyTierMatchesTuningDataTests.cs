using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Tests.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Primitives;

/// <summary>
/// 🔒 `10` §7 / `29` §4-5 — <see cref="DifficultyTier"/> and the authored tuning data cannot drift
/// apart.
/// </summary>
/// <remarks>
/// <para>
/// The enum is the compile-time half of the tier list and the JSON is the authored half. Each on its
/// own is checkable and neither check notices the other moving: a fourth tier added to
/// <c>progression.json</c> would have no member to gate against, and a fourth member added to the
/// enum would name a difficulty with no gate, no par-power column and no multiplier — a run seeded
/// on a tier the balance data cannot describe.
/// </para>
/// <para>
/// ⚠️ <b>Three documents, four assertions, deliberately.</b> <see cref="DifficultyTier"/>'s own
/// remarks name <b>three</b> authored transcriptions of this vocabulary — <c>progression.json</c>'s
/// <c>chapterGating</c>, <c>par_power.json</c>'s tier columns, and
/// <c>schema/chapter.schema.json</c>'s <c>unlockCondition.tier</c> <c>enum</c> — and every one of
/// them is checked here, because a claim in a doc comment that no test checks is a claim that stops
/// being true without anything going red. They are checked separately rather than folded into one
/// union: a union would go green while two of the three disagreed with each other.
/// <c>par_power.json</c> is checked twice over — once for the <c>defaultFill</c> multipliers and once
/// for <b>every</b> <c>parPower</c> row — because `29` §7 makes every one of the 24 cells
/// independently editable, so a row that lost a tier column is a real, reachable state.
/// </para>
/// <para>
/// It lives in <c>SlayIdleRepeat.Application.Tests</c> rather than <c>Core.Tests</c> because it reads
/// files off disk and <c>Core.Tests</c> is hermetic. <see cref="RepoData"/> already exists for
/// exactly this and is reused rather than reimplemented — still no adapter, no port, no container,
/// no network (`23` §3), just <c>System.IO</c> over the checkout the test runs from.
/// </para>
/// </remarks>
public sealed class DifficultyTierMatchesTuningDataTests
{
    private const string ProgressionDocument = "tuning/progression.json";
    private const string ParPowerDocument = "tuning/par_power.json";
    private const string ChapterSchemaDocument = "schema/chapter.schema.json";

    /// <summary>
    /// `10` §7 — the enum is exactly <c>progression.json</c>'s <c>chapterGating</c> keys.
    /// </summary>
    /// <remarks>
    /// ⚠️ The <c>_doc</c> key is excluded by name and nothing else is: the tuning documents carry
    /// <c>_doc</c>/<c>_status</c> annotations throughout, and a filter that dropped every key starting
    /// with an underscore would also drop a future <c>_</c>-prefixed tier without saying so.
    /// </remarks>
    [Fact]
    public void DifficultyTier_is_exactly_the_chapterGating_keys_of_progression_json()
    {
        var gated = KeysOf(ProgressionDocument, "chapterGating");
        var declared = Enum.GetNames<DifficultyTier>();

        gated.ShouldNotBeEmpty(
            $"{ProgressionDocument} declares no chapterGating tiers, so this cross-check would " +
            "compare the enum against nothing and pass forever. The document moved — fix the " +
            "reader, do not delete the case.");

        gated.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            $"{ProgressionDocument} gates a tier DifficultyTier does not declare. No run can be " +
            "started on it, so the gate authors an unreachable difficulty.");

        declared.Except(gated, StringComparer.Ordinal).ShouldBeEmpty(
            $"DifficultyTier declares a tier {ProgressionDocument} does not gate. 10 §7's unlock " +
            "ladder would have nothing to say about it, so it would be either permanently locked or " +
            "permanently open depending on which way M3/M4 reads a missing row.");
    }

    /// <summary>
    /// `29` §4-5 — the enum is exactly <c>par_power.json</c>'s <c>defaultFill.tierMultiplier</c> keys.
    /// </summary>
    [Fact]
    public void DifficultyTier_is_exactly_the_tierMultiplier_keys_of_par_power_json()
    {
        var multipliers = KeysOf(ParPowerDocument, "defaultFill", "tierMultiplier");
        var declared = Enum.GetNames<DifficultyTier>();

        multipliers.ShouldNotBeEmpty(
            $"{ParPowerDocument} declares no defaultFill.tierMultiplier, so this cross-check would " +
            "compare the enum against nothing. The document moved — fix the reader.");

        multipliers.OrderBy(id => id, StringComparer.Ordinal).ShouldBe(
            declared.OrderBy(id => id, StringComparer.Ordinal),
            $"DifficultyTier and {ParPowerDocument}'s defaultFill.tierMultiplier disagree. The " +
            "default fill is what generates a missing par-power cell (29 §7), so a tier with no " +
            "multiplier has no computable par at all.");
    }

    /// <summary>
    /// 🔒 `29` §4-5 — <b>every</b> <c>parPower</c> row carries a column for every tier, and no column
    /// the enum does not declare.
    /// </summary>
    /// <remarks>
    /// ⚠️ Row by row rather than over the union of all rows: `29` §7 makes each of the 24 cells
    /// independently editable, so a single row that lost its <c>MYTHIC</c> column is exactly the
    /// reachable defect — and a union check would still see <c>MYTHIC</c> in the other seven rows and
    /// report agreement.
    /// </remarks>
    [Fact]
    public void Every_parPower_row_carries_exactly_one_column_per_DifficultyTier()
    {
        using var document = JsonDocument.Parse(RepoData.Documents[ParPowerDocument]);

        var rows = document.RootElement.GetProperty("parPower").EnumerateArray().ToArray();
        var declared = Enum.GetNames<DifficultyTier>().OrderBy(id => id, StringComparer.Ordinal).ToArray();

        rows.Length.ShouldBe(
            8,
            $"29 §4 puts par power on 02 §1's eight chapters, and {ParPowerDocument} transcribes " +
            "one row each. A shrunken table would leave the loop below asserting over fewer rows " +
            "than the document has — fix the reader, do not delete the case.");

        foreach (var row in rows)
        {
            var chapter = row.GetProperty("chapter").GetInt32();

            var columns = row.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => !name.Equals("chapter", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            columns.ShouldBe(
                declared,
                $"{ParPowerDocument}'s parPower row for chapter {chapter} does not carry exactly one " +
                "column per DifficultyTier. A missing column is a tier with no par power for that " +
                "chapter — 29 §5's clear-rate assertion has nothing to measure against — and an extra " +
                "one is a difficulty no run can be started on.");
        }
    }

    /// <summary>
    /// 🔒 `14` §6 / `10` §7 — the enum is exactly <c>chapter.schema.json</c>'s
    /// <c>unlockCondition.tier</c> <c>enum</c>, the third authored transcription.
    /// </summary>
    /// <remarks>
    /// ⚠️ It is a <b>schema</b> rather than a tuning file, and that is why it needs its own case
    /// rather than riding on the two above: <c>chapter.schema.json</c> sits on
    /// <c>ContentLoader.SchemasAwaitingContent</c> (<c>content/chapters/</c> is empty until M3-14),
    /// so <b>no content document is validated against it today</b> and nothing else in this
    /// repository would notice its tier list drifting away from the enum. A gate authored against a
    /// tier <c>Core</c> cannot parse is a chapter that never unlocks.
    /// </remarks>
    [Fact]
    public void DifficultyTier_is_exactly_the_unlockCondition_tier_enum_of_chapter_schema_json()
    {
        using var schema = JsonDocument.Parse(RepoData.Documents[ChapterSchemaDocument]);

        var tiers = schema.RootElement
            .GetProperty("properties").GetProperty("unlockCondition")
            .GetProperty("properties").GetProperty("tier")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        tiers.ShouldNotBeEmpty(
            $"{ChapterSchemaDocument} declares no unlockCondition.tier enum, so this cross-check " +
            "would compare the enum against nothing and pass forever. The schema moved — fix the " +
            "reader, do not delete the case.");

        tiers.OrderBy(id => id, StringComparer.Ordinal).ShouldBe(
            Enum.GetNames<DifficultyTier>().OrderBy(id => id, StringComparer.Ordinal),
            $"DifficultyTier and {ChapterSchemaDocument}'s unlockCondition.tier disagree. A chapter " +
            "could then author an unlock gate naming a tier no run can be started on, and the " +
            "schema would validate it.");
    }

    /// <summary>
    /// 🔒 …and the tier ids are the same <b>text</b> the enum members are spelled with, which is what
    /// makes the cross-checks above comparisons rather than coincidences.
    /// </summary>
    /// <remarks>
    /// ⚠️ The three tokens are written out as literals rather than derived from
    /// <c>Enum.GetNames</c>, which is the only way this case can bite: a list taken from the enum
    /// would agree with the enum by construction. Exactly, and case-sensitively — these are the
    /// <b>collection</b> overloads of <c>ShouldContain</c>, which compare with
    /// <c>EqualityComparer&lt;string&gt;</c> and are already ordinal, unlike Shouldly's string
    /// overload. <c>Normal</c> is not <c>NORMAL</c>, and a loader matching the two loosely would
    /// accept a document whose ids no <c>Enum.Parse</c> in <c>Core</c> can read.
    /// </remarks>
    [Fact]
    public void The_tier_ids_are_the_upper_case_tokens_the_enum_spells()
    {
        var gated = KeysOf(ProgressionDocument, "chapterGating");

        gated.ShouldContain("NORMAL");
        gated.ShouldContain("HEROIC");
        gated.ShouldContain("MYTHIC");
        gated.ShouldNotContain("Normal");
    }

    /// <summary>
    /// The keys of one object in a tuning document, with the <c>_doc</c> annotation dropped by name.
    /// </summary>
    private static string[] KeysOf(string document, params string[] path)
    {
        using var parsed = JsonDocument.Parse(RepoData.Documents[document]);

        var element = parsed.RootElement;
        foreach (var step in path)
        {
            element = element.GetProperty(step);
        }

        return element.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => !name.Equals("_doc", StringComparison.Ordinal))
            .ToArray();
    }
}
