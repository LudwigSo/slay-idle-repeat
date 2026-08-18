using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Tests.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Primitives;

/// <summary>Checks that <see cref="DifficultyTier"/> and the authored tuning data cannot drift apart.</summary>
/// <remarks>
/// The enum is the compile-time half of the tier list and the JSON is the authored half. Each on
/// its own is checkable and neither notices the other moving: a fourth tier added to
/// <c>progression.json</c> has no member to gate against, and a fourth enum member has no gate,
/// par-power column, or multiplier. Three separate authored transcriptions of this vocabulary exist
/// (<c>progression.json</c>'s <c>chapterGating</c>, <c>par_power.json</c>'s tier columns, and
/// <c>chapter.schema.json</c>'s <c>unlockCondition.tier</c> constraint) and are checked separately
/// rather than folded into one union, since a union would go green while two of the three disagreed
/// with each other. The third of them is no longer a list of all three tiers — see that case's own
/// remarks. <c>par_power.json</c> is checked twice — once for the <c>defaultFill</c> multipliers
/// and once for every <c>parPower</c> row — because each of the 24 cells is independently editable,
/// so a row losing a tier column is a real reachable state.
/// </remarks>
public sealed class DifficultyTierMatchesTuningDataTests
{
    private const string ProgressionDocument = "tuning/progression.json";
    private const string ParPowerDocument = "tuning/par_power.json";
    private const string ChapterSchemaDocument = "schema/chapter.schema.json";

    /// <summary>The enum is exactly <c>progression.json</c>'s <c>chapterGating</c> keys.</summary>
    /// <remarks>The <c>_doc</c> key is excluded by name and nothing else is — a filter dropping every underscore-prefixed key would also silently drop a future underscore-prefixed tier.</remarks>
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

    /// <summary>The enum is exactly <c>par_power.json</c>'s <c>defaultFill.tierMultiplier</c> keys.</summary>
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

    /// <summary>Every <c>parPower</c> row carries a column for every tier, and no column the enum does not declare.</summary>
    /// <remarks>Checked row by row rather than over the union of all rows: each of the 24 cells is independently editable, so a single row losing its MYTHIC column is a real reachable defect that a union check would miss.</remarks>
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
    /// <c>chapter.schema.json</c> locks <c>unlockCondition.tier</c> to a single tier, and that tier is
    /// one <see cref="DifficultyTier"/> declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 This used to be the third authored transcription of the whole vocabulary — an <c>enum</c>
    /// listing all three tiers, compared member for member against the enum. M7-04b narrowed it to a
    /// <c>const</c>: a chapter's <c>unlockCondition</c> is the per-chapter restatement of the ladder's
    /// Normal rung and of nothing else, so the schema now permits exactly one tier and a chapter
    /// cannot be authored with a prerequisite the ladder is unable to express.
    /// </para>
    /// <para>
    /// The vocabulary cross-check did not move with it: <c>chapterGating</c>'s keys and
    /// <c>par_power.json</c>'s 24 columns are still checked against the full enum above, so a fourth
    /// tier still has two authored transcriptions to disagree with. What survives here, and still has
    /// to, is that the one permitted token is a token <c>Core</c> can parse — a gate authored against
    /// a tier no <c>Enum.Parse</c> can read is a chapter that never unlocks.
    /// </para>
    /// <para>
    /// <em>Which</em> tier it must be is deliberately not asserted here. That is the ladder's
    /// business, and the loader rule that cross-checks this constraint against
    /// <c>tuning/progression.json#/chapterGating</c> already owns it; restating it would be a second
    /// answer to a question that has an owner.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_unlockCondition_tier_of_chapter_schema_json_is_one_tier_DifficultyTier_declares()
    {
        using var schema = JsonDocument.Parse(RepoData.Documents[ChapterSchemaDocument]);

        var tier = schema.RootElement
            .GetProperty("properties").GetProperty("unlockCondition")
            .GetProperty("properties").GetProperty("tier");

        tier.TryGetProperty("const", out var permitted).ShouldBeTrue(
            $"{ChapterSchemaDocument}'s unlockCondition.tier declares no const, so it permits more " +
            "than the one tier the ladder's Normal rung names — or has stopped constraining the " +
            "member at all, which is the hole this narrowing closed. Fix the schema, do not delete " +
            "the case.");

        tier.EnumerateObject().Select(property => property.Name).ShouldNotContain(
            "enum",
            $"{ChapterSchemaDocument}'s unlockCondition.tier carries an enum beside its const. Two " +
            "constraints on one member are two answers to which tiers a chapter may name, and the " +
            "wider of them is the one an author will discover.");

        Enum.GetNames<DifficultyTier>().ShouldContain(
            permitted.GetString()!,
            $"{ChapterSchemaDocument} locks unlockCondition.tier to a token DifficultyTier does not " +
            "declare. A chapter could then author an unlock gate naming a tier no run can be started " +
            "on, and the schema would validate it.");
    }

    /// <summary>The tier ids are the same text the enum members are spelled with, which is what makes the cross-checks above comparisons rather than coincidences.</summary>
    /// <remarks>
    /// The three tokens are written out as literals rather than derived from <c>Enum.GetNames</c>,
    /// which is the only way this case can bite — a list taken from the enum would agree with it by
    /// construction. Case-sensitively: <c>Normal</c> is not <c>NORMAL</c>, and a loader matching the
    /// two loosely would accept a document whose ids no <c>Enum.Parse</c> in Core can read.
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
