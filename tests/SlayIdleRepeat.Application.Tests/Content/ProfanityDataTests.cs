using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The shipped <c>content/profanity/</c> word lists, and the register that expires them.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic and proves the matcher against fixture terms that are nobody's
/// profanity; this suite is the other half — that the shipped lists are lists at all, that they pair
/// with a schema, and that the deferral of the curated ones cannot outlive them.
/// </remarks>
public sealed class ProfanityDataTests
{
    private const string English = "content/profanity/en.json";

    private const string German = "content/profanity/de.json";

    /// <summary>
    /// Both `27` §1 languages ship a list, and neither is empty.
    /// </summary>
    /// <remarks>
    /// 🔴 The floor the whole filter rests on (steering S3). An empty list is what a file that failed
    /// to load reads as, and a filter with no terms accepts every name there is. <c>ProfanityLexicon</c>
    /// refuses one at read time; this is the same claim about the shipped bytes, because the reader's
    /// refusal would only ever be seen by whoever ran the game.
    /// </remarks>
    [Theory]
    [InlineData(English, "EN")]
    [InlineData(German, "DE")]
    public void Each_language_ships_a_non_empty_word_list(string documentPath, string language)
    {
        var root = Root(documentPath);

        root.GetProperty("language").GetString().ShouldBe(language);

        var terms = root.GetProperty("terms");

        terms.ValueKind.ShouldBe(JsonValueKind.Array);
        terms.GetArrayLength().ShouldBeGreaterThan(
            0,
            $"{documentPath} authors no terms. 27 §1 filters names in EN and DE; a list with none " +
            "accepts every name there is, on every account created from then on, and reports nothing.");
    }

    /// <summary>
    /// The terms are written in the form the normaliser produces, so none of them is dead text.
    /// </summary>
    /// <remarks>
    /// The matcher normalises both sides, so a term spelled with an umlaut or a capital would still
    /// match — but it would be a term nobody reading the file could check against the data, and the
    /// German list depends on the ss-for-ss spelling being the authored one. The schema's pattern
    /// says the same thing; this says it about the bytes that shipped.
    /// </remarks>
    [Theory]
    [InlineData(English)]
    [InlineData(German)]
    public void Every_term_is_lowercase_diacritic_free_letters(string documentPath)
    {
        foreach (var term in Root(documentPath).GetProperty("terms").EnumerateArray())
        {
            var text = term.GetString()!;

            text.ShouldAllBe(
                character => character >= 'a' && character <= 'z',
                $"{documentPath} carries '{text}'. Terms are authored in the form the normaliser " +
                "produces — lowercase a-z, ss expanded, diacritics stripped — so a reader can see " +
                "what is actually being compared.");

            text.Length.ShouldBeGreaterThanOrEqualTo(
                3,
                $"{documentPath} carries '{text}', which is short enough to appear inside ordinary " +
                "words. Matching is by substring; a two-letter term refuses names nobody would call " +
                "profane.");
        }
    }

    /// <summary>The two lists are different lists, not one translated into the other.</summary>
    /// <remarks>
    /// 🔒 `16` D20 forbids machine translation, and the strongest checkable form of that here is
    /// that the German list is not the English one: German profanity does not map word-for-word onto
    /// English profanity, so an overlap would be the signature of a translated file rather than an
    /// authored one. It cannot prove a human wrote it — nothing can — but it fails on the specific
    /// mistake D20 is about.
    /// </remarks>
    [Fact]
    public void The_German_list_is_not_the_English_one_translated()
    {
        var english = Terms(English);
        var german = Terms(German);

        german.ShouldNotBe(english);
        german.Intersect(english, StringComparer.Ordinal).ShouldBeEmpty(
            "a term in both lists is either a translation or a duplicate. 16 D20: nothing ships " +
            "machine-translated, and the German seed was authored rather than translated.");
    }

    /// <summary>The word lists pair with their schema by the declared table rather than by the stem rule.</summary>
    /// <remarks>
    /// The files are named after their language, so the stem rule would look for
    /// <c>schema/en.schema.json</c> and find nothing. Adding a content type means adding its schema
    /// AND its <c>ContentTypeSchemas</c> row in the same commit; this is the pin on that row.
    /// </remarks>
    [Theory]
    [InlineData(English)]
    [InlineData(German)]
    public void The_word_lists_pair_with_the_profanity_schema(string documentPath)
    {
        ContentLayout.SchemaFor(documentPath).ShouldBe("schema/profanity.schema.json");
    }

    // -------------------------------------------------------------- the register that expires them

    /// <summary>🔒 The register holds in all four directions over the real data set.</summary>
    [Fact]
    public void The_curation_register_holds_over_the_shipped_data()
    {
        var documents = RepoData.Documents;

        ContentCurationRegister.Satisfied(ContentCurationRegister.Seeds, documents).ShouldBeEmpty();
        ContentCurationRegister.Overgrown(ContentCurationRegister.Seeds, documents).ShouldBeEmpty();
        ContentCurationRegister.Undeclared(ContentCurationRegister.Seeds, documents).ShouldBeEmpty();
        ContentCurationRegister.Unanchored(ContentCurationRegister.Seeds, documents).ShouldBeEmpty();
        ContentCurationRegister.Malformed(ContentCurationRegister.Seeds).ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 Both word lists are declared seeds owned by M17, and the register is not empty.
    /// </summary>
    /// <remarks>
    /// The floor under the four directions above: every one of them holds vacuously over an empty
    /// register, and three of them hold vacuously over a data set with no seeded document in it.
    /// Named by identity rather than counted, so a renamed entry goes red.
    /// </remarks>
    [Fact]
    public void Both_word_lists_are_declared_seeds_owned_by_M17()
    {
        ContentCurationRegister.Seeds.ShouldNotBeEmpty();

        foreach (var documentPath in new[] { English, German })
        {
            var entry = ContentCurationRegister.Seeds
                .SingleOrDefault(seed => seed.DocumentPath == documentPath);

            entry.ShouldNotBeNull($"{documentPath} has no register entry naming who curates it.");
            entry!.Owner.ShouldBe(
                "M17",
                "M17 completes localisation and accessibility, which is where a native reviewer per " +
                "language exists. The mechanism is M4-10's; the words are not.");

            ContentCurationRegister.IsSeed(RepoData.Documents[documentPath]).ShouldBeTrue(
                $"{documentPath} does not declare curation.state = SEED, so nothing expires its entry.");
        }
    }

    /// <summary>
    /// 🔒 A curated document makes its entry go red — the register expires when it is <b>satisfied</b>.
    /// </summary>
    /// <remarks>
    /// Steering S4's real requirement, and the direction a register usually misses. Driven with a
    /// document built here rather than by editing a shipped one, so the proof never commits a
    /// violation.
    /// </remarks>
    [Fact]
    public void An_entry_whose_document_has_been_curated_is_reported()
    {
        var curated = RepoData.Documents[English].Replace(
            "\"state\": \"SEED\"", "\"state\": \"CURATED\"", StringComparison.Ordinal);

        curated.ShouldNotBe(RepoData.Documents[English], "the fixture must actually change the state.");

        var offenders = ContentCurationRegister.Satisfied(
            ContentCurationRegister.Seeds, With(English, curated));

        offenders.Count.ShouldBe(1);
        offenders[0].ShouldContain(English);
        offenders[0].ShouldContain("must be deleted");
    }

    /// <summary>
    /// 🔒 A list that outgrew its own ceiling while still claiming to be a seed is reported.
    /// </summary>
    /// <remarks>
    /// The direction the state flag alone cannot see: curating a list and forgetting to flip the
    /// flag looks exactly like a seed, and only the size gives it away.
    /// </remarks>
    [Fact]
    public void A_seed_that_outgrew_its_own_ceiling_is_reported()
    {
        var shrunkCeiling = RepoData.Documents[English].Replace(
            "\"maxSeedTerms\": 24", "\"maxSeedTerms\": 2", StringComparison.Ordinal);

        shrunkCeiling.ShouldNotBe(RepoData.Documents[English], "the fixture must actually move the ceiling.");

        var offenders = ContentCurationRegister.Overgrown(
            ContentCurationRegister.Seeds, With(English, shrunkCeiling));

        offenders.Count.ShouldBe(1);
        offenders[0].ShouldContain("against its own ceiling");
    }

    /// <summary>A seeded document nobody declared is reported.</summary>
    [Fact]
    public void A_seeded_document_with_no_entry_is_reported()
    {
        var documents = With("content/profanity/fr.json", RepoData.Documents[English]);

        var offenders = ContentCurationRegister.Undeclared(ContentCurationRegister.Seeds, documents);

        offenders.Count.ShouldBe(1);
        offenders[0].ShouldContain("content/profanity/fr.json");
    }

    /// <summary>An entry naming a document that is not there is reported.</summary>
    [Fact]
    public void An_entry_naming_an_absent_document_is_reported()
    {
        var offenders = ContentCurationRegister.Unanchored(
            [new ContentCurationRegister.Seed("content/profanity/xx.json", "M17", new string('x', 60))],
            RepoData.Documents);

        offenders.Count.ShouldBe(1);
        offenders[0].ShouldContain("not in the data set");
    }

    /// <summary>An entry with no owner or no reason is reported.</summary>
    [Fact]
    public void A_malformed_entry_is_reported()
    {
        ContentCurationRegister.Malformed(
                [new ContentCurationRegister.Seed(English, "  ", "too short")])
            .Count.ShouldBe(2, "one for the missing owner, one for the reason nobody can falsify.");
    }

    /// <summary>
    /// The four directions are not all satisfied by the same input — each reports its own case only.
    /// </summary>
    /// <remarks>
    /// The negative control across the register: a curated document must NOT read as overgrown or
    /// undeclared, or the four directions would be one direction wearing four names.
    /// </remarks>
    [Fact]
    public void Each_direction_reports_only_its_own_case()
    {
        var curated = With(English, RepoData.Documents[English].Replace(
            "\"state\": \"SEED\"", "\"state\": \"CURATED\"", StringComparison.Ordinal));

        ContentCurationRegister.Overgrown(ContentCurationRegister.Seeds, curated).ShouldBeEmpty(
            "a curated document is not a seed, so its size is no longer this register's business.");
        ContentCurationRegister.Undeclared(ContentCurationRegister.Seeds, curated).ShouldBeEmpty();
        ContentCurationRegister.Unanchored(ContentCurationRegister.Seeds, curated).ShouldBeEmpty();
    }

    private static IReadOnlyDictionary<string, string> With(string documentPath, string text)
    {
        var documents = new Dictionary<string, string>(RepoData.Documents, StringComparer.Ordinal)
        {
            [documentPath] = text,
        };

        return documents;
    }

    private static string[] Terms(string documentPath) =>
        Root(documentPath).GetProperty("terms").EnumerateArray().Select(t => t.GetString()!).ToArray();

    private static JsonElement Root(string documentPath)
    {
        RepoData.Documents.ContainsKey(documentPath).ShouldBeTrue(
            $"{documentPath} is not in the shipped data set. 27 §1 filters in EN and DE; a missing " +
            "language file is a filter that silently stopped running in that language.");

        using var document = JsonDocument.Parse(RepoData.Documents[documentPath]);

        return document.RootElement.Clone();
    }
}
