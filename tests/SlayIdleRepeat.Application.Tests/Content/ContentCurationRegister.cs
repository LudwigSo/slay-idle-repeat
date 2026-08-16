using System.Text.Json;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 The repo's register of shipped content that is deliberately a <b>seed</b> rather than a
/// finished list, with the milestone that curates it and a decidable predicate that expires the
/// entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists, and why it is not <c>GapRegister</c>.</b> <c>GapRegister</c> holds design
/// surface that has <em>not been built</em>, keyed on a <c>Core</c> TYPE that must not yet exist.
/// This holds surface that <em>has</em> been built and shipped in a deliberately incomplete form,
/// keyed on a DOCUMENT — the name filter's mechanism is complete and its word lists are twelve terms
/// each. Neither register can hold the other's subject: a type-keyed entry cannot describe a JSON
/// file, and a document-keyed entry cannot expire when a type arrives. It is the third register in
/// this repository, beside <c>ContentLoader.SchemasAwaitingContent</c> (a schema with no content)
/// and <c>build/ci/test-suites.json</c>'s <c>knownEmpty</c> (a suite with no tests), and a fourth
/// seeded document joins <see cref="Seeds"/> rather than growing a sibling file.
/// </para>
/// <para>
/// ⚠️ <b>Why it is here and not beside <c>ContentLoader.SchemasAwaitingContent</c>, which is the
/// closer sibling.</b> That register is production code and its rule runs inside the content load,
/// so a stale entry fails <c>tools/ContentValidator</c> and therefore the build — which is the
/// stronger place to be. The trade is that <c>ContentLoader</c> would then have to know what a
/// <c>curation</c> block is, and `14` §6's loader is deliberately generic: it pairs a document with
/// a schema and validates it, while every type-specific rule already lives in
/// <c>ContentInvariants</c> or in a suite. Keeping it here costs the build failure and keeps the
/// loader generic. <b>If a second seeded content type appears, move it</b> — one seeded type is a
/// note, three are a convention, and a convention belongs beside the other cross-file rules.
/// </para>
/// <para>
/// <b>The mechanism, and it fails in four directions</b> — <c>GapRegister</c>'s shape, applied to
/// data:
/// </para>
/// <list type="number">
///   <item><b>Satisfied.</b> A document whose own <c>curation.state</c> is no longer <c>SEED</c> has
///   been curated, and its entry must go. Removing it is <i>forced</i> by the commit that curates the
///   list rather than remembered at some later kickoff — which is steering S4's real requirement:
///   an exemption must fail when it stops being true, <em>including when it has been satisfied</em>.</item>
///   <item><b>Overgrown.</b> A document still claiming <c>SEED</c> whose list has outgrown its own
///   declared ceiling is a curated list whose state nobody flipped. This is the direction that
///   catches the honest mistake, and the one a state flag alone cannot see.</item>
///   <item><b>Undeclared.</b> A document that declares itself a <c>SEED</c> and has no entry here is
///   a deferral nobody owns. This is the direction usually skipped, and the one that makes the
///   register more than a comment.</item>
///   <item><b>Unanchored.</b> An entry naming a document the data set does not hold can never be
///   satisfied, only deleted by hand — the state this register replaces.</item>
/// </list>
/// <para>
/// ⚠️ <b>The known limit, stated so nobody assumes otherwise.</b> What is decidable here is the
/// SHAPE: the state flipped, the list outgrew its ceiling, the owner is missing. What is not
/// decidable is whether a list of the right SIZE is a list of the right WORDS — a seed that is
/// small and wrong looks exactly like a seed that is small and right. Re-read these entries at each
/// milestone kickoff; CI is not doing it for you.
/// </para>
/// </remarks>
internal static class ContentCurationRegister
{
    /// <summary>A shipped document that is a seed, and the milestone task that curates it.</summary>
    /// <param name="DocumentPath">The snapshot-relative path, e.g. <c>content/profanity/en.json</c>.</param>
    /// <param name="Owner">The milestone task that replaces the seed, e.g. <c>M17</c>.</param>
    /// <param name="Why">Why it ships as a seed. Something a later reader can falsify.</param>
    internal sealed record Seed(string DocumentPath, string Owner, string Why);

    /// <summary>The member a seeded document declares its curation state under.</summary>
    internal const string CurationMember = "curation";

    /// <summary>The value a document carries while it is still a seed.</summary>
    internal const string SeedState = "SEED";

    /// <summary>🔒 Everything shipped as a seed. Each entry expires by itself.</summary>
    internal static readonly Seed[] Seeds =
    {
        new("content/profanity/en.json", "M17",
            "27 §1 is the design set's only profanity specification and it states the STANDARD — " +
            "EN and DE, at creation and on every edit — without authoring a single word. M4-10 " +
            "shipped the mechanism (normalisation, substring matching, which language reported the " +
            "match) and twelve high-precision English terms, chosen so the substring match they " +
            "drive has as few innocent collisions as possible. A curated list is a localisation " +
            "deliverable, not a code change: it needs a native reviewer per language, a false-" +
            "positive pass against real player names, and a decision about the Scunthorpe class of " +
            "collision the file's own _doc records. M17 completes localisation and accessibility " +
            "and is where that reviewer exists."),

        new("content/profanity/de.json", "M17",
            "The German half, and it is a SEPARATE entry rather than a note on the English one " +
            "because 16 D20 makes it a separate obligation: nothing ships machine-translated, and " +
            "German profanity does not map word-for-word onto English profanity — 'hurensohn', " +
            "'kanake', 'spast' and 'missgeburt' have no counterpart in the English seed. The seed " +
            "was authored, not translated, and the curated list needs a native German reviewer " +
            "rather than the English one working through a dictionary. Same owner, same reason, " +
            "different work."),
    };

    /// <summary>What a satisfied entry means, said once.</summary>
    internal const string SatisfiedConsequence =
        "This document is no longer a seed, so the register entry has been SATISFIED and must be " +
        "deleted in the same commit that curated it. An exemption that outlives what it excused is " +
        "the failure this register exists to make impossible — it stops describing anything and " +
        "starts hiding the next one.";

    /// <summary>What an overgrown entry means, said once.</summary>
    internal const string OvergrownConsequence =
        "It still declares itself a SEED and has outgrown its own declared ceiling, which is what a " +
        "curated list whose state nobody flipped looks like. Either flip curation.state to CURATED " +
        "and delete the register entry, or — if this really is still a seed — raise maxSeedTerms " +
        "deliberately, in a commit that says why.";

    /// <summary>What an undeclared seed means, said once.</summary>
    internal const string UndeclaredConsequence =
        "A document declares itself a SEED and no entry here names who finishes it. A seed with no " +
        "owner is indistinguishable from a list somebody thought was complete.";

    /// <summary>What an unanchored entry means, said once.</summary>
    internal const string UnanchoredConsequence =
        "The register defers a document the data set does not hold. Such an entry can never be " +
        "satisfied — only deleted by hand, which is the state this register replaces. Fix the path, " +
        "or delete the entry in the same commit that deleted the document.";

    /// <summary>Every entry whose document is no longer a seed. Empty means the register holds.</summary>
    /// <remarks>
    /// Takes its entries and its documents as parameters rather than reading the statics, so the
    /// self-tests can drive it with a deliberately satisfied entry and prove it bites — without ever
    /// committing one. The same construction as <c>GapRegister.Expired</c>.
    /// </remarks>
    internal static IReadOnlyList<string> Satisfied(
        IEnumerable<Seed> seeds, IReadOnlyDictionary<string, string> documents) =>
        seeds
            .Where(seed => documents.ContainsKey(seed.DocumentPath))
            .Where(seed => !IsSeed(documents[seed.DocumentPath]))
            .Select(seed =>
                $"'{seed.DocumentPath}' is declared a seed owned by {seed.Owner}, and the document no " +
                $"longer says so. {SatisfiedConsequence}")
            .ToArray();

    /// <summary>Every entry whose document has outgrown its own ceiling. Empty means the register holds.</summary>
    internal static IReadOnlyList<string> Overgrown(
        IEnumerable<Seed> seeds, IReadOnlyDictionary<string, string> documents)
    {
        var offenders = new List<string>();

        foreach (var seed in seeds)
        {
            if (!documents.TryGetValue(seed.DocumentPath, out var text) || !IsSeed(text))
            {
                continue;
            }

            var (terms, ceiling) = Measure(text);

            if (terms > ceiling)
            {
                offenders.Add(
                    $"'{seed.DocumentPath}' carries {terms} term(s) against its own ceiling of " +
                    $"{ceiling}. {OvergrownConsequence}");
            }
        }

        return offenders;
    }

    /// <summary>Every seeded document no entry names. Empty means the register holds.</summary>
    internal static IReadOnlyList<string> Undeclared(
        IEnumerable<Seed> seeds, IReadOnlyDictionary<string, string> documents)
    {
        var declared = seeds.Select(seed => seed.DocumentPath).ToHashSet(StringComparer.Ordinal);

        return documents
            .Where(document => IsSeed(document.Value))
            .Where(document => !declared.Contains(document.Key))
            .Select(document => $"'{document.Key}' declares itself a seed. {UndeclaredConsequence}")
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Every entry naming a document that is not there. Empty means the register holds.</summary>
    internal static IReadOnlyList<string> Unanchored(
        IEnumerable<Seed> seeds, IReadOnlyDictionary<string, string> documents) =>
        seeds
            .Where(seed => !documents.ContainsKey(seed.DocumentPath))
            .Select(seed => $"'{seed.DocumentPath}' is not in the data set. {UnanchoredConsequence}")
            .ToArray();

    /// <summary>Every entry that is not well formed: no owner, or no reason worth falsifying.</summary>
    internal static IReadOnlyList<string> Malformed(IEnumerable<Seed> seeds)
    {
        var offenders = new List<string>();

        foreach (var seed in seeds)
        {
            if (string.IsNullOrWhiteSpace(seed.Owner))
            {
                offenders.Add(
                    $"'{seed.DocumentPath}' names no owning task. An entry with no owner has no " +
                    "expiry a reader can check.");
            }

            if (string.IsNullOrWhiteSpace(seed.Why) || seed.Why.Length < 40)
            {
                offenders.Add(
                    $"'{seed.DocumentPath}' carries no written reason worth falsifying. The reason " +
                    "going stale while the predicate still holds is the one case CI cannot catch.");
            }
        }

        return offenders;
    }

    /// <summary>Whether a document declares itself a seed.</summary>
    /// <remarks>
    /// Reads the document's own <c>curation</c> block rather than a list of paths held here, which
    /// is what makes the <b>undeclared</b> direction decidable at all: a new seeded file is visible
    /// to this register the moment it lands, without anybody remembering to say so.
    /// </remarks>
    internal static bool IsSeed(string documentText)
    {
        using var document = Parse(documentText);

        return document is not null &&
               document.RootElement.ValueKind == JsonValueKind.Object &&
               document.RootElement.TryGetProperty(CurationMember, out var curation) &&
               curation.ValueKind == JsonValueKind.Object &&
               curation.TryGetProperty("state", out var state) &&
               state.ValueKind == JsonValueKind.String &&
               string.Equals(state.GetString(), SeedState, StringComparison.Ordinal);
    }

    /// <summary>How many terms a seeded document carries, and the ceiling it declares for itself.</summary>
    private static (int Terms, int Ceiling) Measure(string documentText)
    {
        using var document = Parse(documentText);

        var root = document!.RootElement;
        var terms = root.TryGetProperty("terms", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.GetArrayLength()
            : 0;

        var ceiling = root.GetProperty(CurationMember).TryGetProperty("maxSeedTerms", out var max)
            ? max.GetInt32()
            : 0;

        return (terms, ceiling);
    }

    /// <summary>Parses a document, or <see langword="null"/> when the bytes are not JSON at all.</summary>
    /// <remarks>
    /// A non-JSON document is not this register's failure to report — <c>ContentLoader</c> refuses
    /// it with a located <c>MalformedJson</c> issue long before anything here runs — so swallowing
    /// the parse failure here keeps one defect reporting once.
    /// </remarks>
    private static JsonDocument? Parse(string documentText)
    {
        try
        {
            return JsonDocument.Parse(documentText);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
