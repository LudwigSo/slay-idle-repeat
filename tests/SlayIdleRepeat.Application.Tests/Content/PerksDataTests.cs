using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>Tests <c>content/perks/perks.json</c> and R35, the rule that validates its embedded effects against <c>schema/effect.schema.json</c>.</summary>
public sealed class PerksDataTests
{
    private const string Document = "content/perks/perks.json";

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    [Fact]
    public void The_document_is_governed_by_its_own_schema()
    {
        ContentLayout.SchemaFor(Document).ShouldBe("schema/perk.schema.json");

        ContentLoader.SchemasAwaitingContent.ShouldNotContain("schema/perk.schema.json");
        ContentLoader.VocabularySchemas.ShouldNotContain("schema/perk.schema.json");
    }

    /// <summary>The effect schema remains a vocabulary schema — it governs no file of its own, even now that perks embed effects.</summary>
    [Fact]
    public void The_effect_schema_is_still_a_vocabulary_schema_governing_no_file()
    {
        ContentLoader.VocabularySchemas.ShouldContain(ContentInvariants.EffectSchemaPath);

        Data().DocumentPaths.ShouldNotContain(
            "schema/effect.schema.json", "a schema is never a document of the snapshot");
    }

    /// <summary>Every category authors exactly one base perk, and every other row is gated behind one.</summary>
    /// <remarks>
    /// 🔒 The load-bearing shape of the reworked catalogue, and the one a row can break in
    /// silence. A second un-gated row in a category is a second entry to it, so the category stops
    /// being a commitment; a base that requires something is a category nothing can enter. Neither
    /// fails anywhere else — the draft would simply offer a different pool, and no test would notice.
    /// </remarks>
    [Fact]
    public void Every_category_is_entered_through_exactly_one_ungated_base_perk()
    {
        var ungatedByCategory = Rows()
            .Where(r => Array(r, "requires").Count == 0)
            .GroupBy(r => Text(r, "category"), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(r => Text(r, "id")).ToArray(), StringComparer.Ordinal);

        foreach (var (category, bases) in ungatedByCategory)
        {
            bases.Length.ShouldBe(
                1, $"{category} authors {bases.Length} un-gated perks: {string.Join(", ", bases)}");
        }

        ungatedByCategory.Keys.ShouldBe(Categories, ignoreOrder: true);
    }

    /// <summary>Every prerequisite names a perk this file authors, and no perk requires itself.</summary>
    /// <remarks>
    /// A prerequisite pointing at a perk that does not exist is a perk that can never be offered:
    /// the draft narrows its pool by "are all of these owned", and an id nothing authors is never
    /// owned. It is invisible in play — the perk simply never appears.
    /// </remarks>
    [Fact]
    public void Every_prerequisite_is_an_authored_perk_and_no_perk_requires_itself()
    {
        var authored = Rows().Select(r => Text(r, "id")).ToHashSet(StringComparer.Ordinal);

        foreach (var row in Rows())
        {
            var id = Text(row, "id");

            foreach (var required in Array(row, "requires").Select(v => v.AsText("requires")))
            {
                authored.ShouldContain(required, $"'{id}' requires '{required}'");
                required.ShouldNotBe(id, $"'{id}' requires itself and could never be offered");
            }
        }
    }

    /// <summary>A gated perk's prerequisites are themselves un-gated, so no chain runs deeper than one step.</summary>
    /// <remarks>
    /// Depth is a design decision, not an accident: one step means a category costs a player exactly
    /// one commitment. A two-step chain would need three specific offers before the third perk could
    /// appear at all, which at three options a battle is a perk most runs never see.
    /// </remarks>
    [Fact]
    public void No_perk_is_gated_more_than_one_step_deep()
    {
        var ungated = Rows()
            .Where(r => Array(r, "requires").Count == 0)
            .Select(r => Text(r, "id"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in Rows())
        {
            foreach (var required in Array(row, "requires").Select(v => v.AsText("requires")))
            {
                ungated.ShouldContain(
                    required,
                    $"'{Text(row, "id")}' requires '{required}', which is itself gated");
            }
        }
    }

    /// <summary>Every row carries exactly 3 tiers, numbered 1, 2, 3 in order.</summary>
    [Fact]
    public void Every_row_carries_exactly_3_tiers_in_order()
    {
        var perks = Data().GetDocument(Document).Root;
        perks.TryGetMember("perks", out var rows).ShouldBeTrue();

        foreach (var row in rows!.Items)
        {
            row.TryGetMember("tiers", out var tiers).ShouldBeTrue();
            tiers!.Items.Count.ShouldBe(3, "06 §1.1 — Tier I, II, III and no fourth");

            for (var i = 0; i < 3; i++)
            {
                tiers.Items[i].TryGetMember("tier", out var tierNumber).ShouldBeTrue();
                tierNumber!.AsInt32().ShouldBe(i + 1);
            }
        }
    }

    /// <summary>Every one of the 9 categories is represented.</summary>
    [Fact]
    public void All_9_categories_are_represented()
    {
        Rows().Select(r => Text(r, "category")).ToHashSet(StringComparer.Ordinal)
            .ShouldBe(Categories, ignoreOrder: true);
    }

    /// <summary>The elements carry the weight of the catalogue, and no category is too thin or too fat to draft.</summary>
    /// <remarks>
    /// The catalogue's own weighting, and a design statement rather than a tally: defence, offence,
    /// crit and sustain are what every build wants, so a generic half as wide as the elemental one
    /// would crowd out the identity the draft exists to build towards. Stated as totals rather than
    /// per-category, because the hybrids are filed under one of their several categories and a
    /// per-category floor would really be an assertion about where each hybrid was filed.
    /// <para>
    /// The 4–10 band is the other half: under four rows a category cannot fill a draft that offers
    /// three options and removes maxed perks, and over ten it drowns the eight others in the pool.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_elements_carry_more_of_the_catalogue_than_the_generic_categories()
    {
        var rowsPerCategory = Rows()
            .GroupBy(r => Text(r, "category"), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var (category, count) in rowsPerCategory)
        {
            count.ShouldBeInRange(4, 10, category);
        }

        Elements.Sum(c => rowsPerCategory[c]).ShouldBeGreaterThan(
            Generics.Sum(c => rowsPerCategory[c]),
            "the elements are what a run commits to; the generic categories are the filler around them");
    }

    /// <summary>Every one of the 4 rarities is represented.</summary>
    [Fact]
    public void All_4_rarities_are_represented()
    {
        var perks = Data().GetDocument(Document).Root;
        perks.TryGetMember("perks", out var rows).ShouldBeTrue();

        var rarities = rows!.Items
            .Select(r => { r.TryGetMember("rarity", out var c); return c!.AsText("rarity"); })
            .ToHashSet(StringComparer.Ordinal);

        rarities.ShouldBe(new[] { "COMMON", "RARE", "EPIC", "LEGENDARY" }, ignoreOrder: true);
    }

    // ─────────────────────────────────────────────────────── R35

    /// <summary>R35's floor, scoped to this document rather than read off the process-wide accumulator — see <c>BossesDataTests</c>' identical case for why.</summary>
    [Fact]
    public void R35_validated_every_embedded_effect_in_the_shipped_perk_catalogue()
    {
        var snapshot = Data();

        var validated = ContentInvariants.ValidatedEmbeddedEffects
            .Where(e => e.StartsWith(Document, StringComparison.Ordinal))
            .ToArray();

        var root = snapshot.GetDocument(Document).Root;
        root.TryGetMember("perks", out var rows).ShouldBeTrue();

        var embedded = rows!.Items.Sum(r =>
        {
            r.TryGetMember("tiers", out var tiers);
            return tiers!.Items.Sum(t =>
            {
                t.TryGetMember("effects", out var effects);
                return effects!.Items.Count;
            });
        });

        embedded.ShouldBe(237, "the shipped catalogue's own effect census — 67 rows x 3 tiers, plus multi-clause tiers");
        validated.Length.ShouldBe(embedded, "R35 reaches every embedded effect in the file, not a prefix of them");
    }

    /// <summary>R35 refuses an embedded effect whose op-specific keys are wrong, naming the document, pointer, and rule.</summary>
    /// <remarks>
    /// The second case is the collision R35 had to be fixed for: a condition comparator is ALSO
    /// spelled <c>op</c>, and before the fix a well-formed condition was found and validated as if
    /// it were the top-level effect. This pins the genuine negative instead, so a regression in
    /// either direction is caught.
    /// </remarks>
    [Theory]
    [InlineData(
        "\"id\": \"PK_MIGHT_T1\",",
        "\"id\": \"PK_MIGHT_T1\", \"charges\": 2,",
        "an op-specific key (charges) on an op that does not admit it")]
    [InlineData(
        "\"id\": \"PK_CASCADE_T1A\",",
        "\"id\": \"PK_CASCADE_T1A\", \"value\": 3.0,",
        "a value on the one combat-flow op that carries none")]
    public void R35_refuses_a_malformed_embedded_effect(string find, string replaceWith, string why)
    {
        var result = ContentLoader.Load(RepoData.SourceWithEdit(Document, find, replaceWith));

        result.Snapshot.ShouldBeNull(why);

        var issue = result.Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe(ContentIssueCode.SchemaViolation, "S2 — which rule fired");
        issue.Location.ShouldStartWith(Document, Case.Sensitive, "which document");
        issue.Location.ShouldContain("/effects/", Case.Sensitive, "which embedded effect");
        issue.Message.ShouldContain("oneOf", Case.Sensitive, "18 §1's closed op-to-key partition");
    }

    /// <summary>
    /// Negative control: a condition object is refused at the top-level effect oneOf if it's ever
    /// reached there directly — the tripwire for R35's id+op signature regressing back to op-alone.
    /// </summary>
    [Fact]
    public void An_intentionally_malformed_condition_comparator_is_refused()
    {
        // Every tier of the perk that carries this gate is mutated, and the count says so: the
        // three rungs author the same condition, so an anchor narrow enough to hit one would have to
        // reach for a magnitude and would move with the next re-tune.
        var result = ContentLoader.Load(RepoData.SourceWithEdit(
            Document,
            "\"op\": \"gte\"",
            "\"op\": \"BOGUS_COMPARATOR\"",
            occurrences: 3));

        result.Snapshot.ShouldBeNull("BOGUS_COMPARATOR is not one of the 7 comparators 18 §4 fixes");
        result.Issues.ShouldNotBeEmpty();
    }

    // ───────────────────────────────────────────────────── reading the rows

    /// <summary>The nine categories, spelled once.</summary>
    private static readonly string[] Categories =
    {
        "LIGHTNING", "COLD", "FIRE", "POISON", "BLEED", "DEFENSE", "OFFENSE", "CRIT", "SUSTAIN",
    };

    /// <summary>The five element categories — the ones told apart by their stacking rules.</summary>
    private static readonly string[] Elements = { "LIGHTNING", "COLD", "FIRE", "POISON", "BLEED" };

    /// <summary>The four generic categories.</summary>
    private static readonly string[] Generics = { "DEFENSE", "OFFENSE", "CRIT", "SUSTAIN" };

    private static IReadOnlyList<ContentValue> Rows()
    {
        var perks = Data().GetDocument(Document).Root;

        perks.TryGetMember("perks", out var rows).ShouldBeTrue();

        return rows!.Items;
    }

    private static string Text(ContentValue row, string member)
    {
        row.TryGetMember(member, out var value).ShouldBeTrue(member);

        return value!.AsText(member);
    }

    private static IReadOnlyList<ContentValue> Array(ContentValue row, string member)
    {
        row.TryGetMember(member, out var value).ShouldBeTrue(member);

        return value!.Items;
    }
}
