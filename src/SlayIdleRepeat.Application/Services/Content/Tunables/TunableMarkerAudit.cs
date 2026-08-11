using System.Globalization;
using System.Text.RegularExpressions;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content.Tunables;

/// <summary>Finds every 📐 TUNABLE marker in one design document.</summary>
public static partial class TunableMarkerScanner
{
    /// <summary>The marker itself. One character, and the whole rule hangs off it.</summary>
    public const string Marker = "\U0001F4D0";

    /// <summary>Scans one markdown document.</summary>
    /// <param name="documentFileName">e.g. <c>08_GEAR_AND_MERGING.md</c>; the leading digits are the doc id.</param>
    /// <param name="markdown">The document text.</param>
    public static IReadOnlyList<TunableMarker> Scan(string documentFileName, string markdown)
    {
        ArgumentNullException.ThrowIfNull(documentFileName);
        ArgumentNullException.ThrowIfNull(markdown);

        var name = DocumentId().Match(documentFileName);
        if (!name.Success)
        {
            return [];
        }

        var docId = name.Groups[1].Value;
        var markers = new List<TunableMarker>();
        var openSections = new List<(int Level, string Section)>();
        var section = string.Empty;

        var lines = markdown.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');

            var heading = Heading().Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                openSections.RemoveAll(s => s.Level >= level);

                // An H1 is the document's own title (`# 21 — Economy Simulator Spec`); its leading
                // number is the doc id, not a section, and reading it as one would file everything
                // before the first `##` under a section that does not exist.
                var numbered = level == 1
                    ? System.Text.RegularExpressions.Match.Empty
                    : SectionNumber().Match(heading.Groups[2].Value);

                if (numbered.Success)
                {
                    section = numbered.Groups[1].Value;
                    openSections.Add((level, section));
                }
                else
                {
                    // `### Rules` under `## 1. Board topology` belongs to §1, not to nothing.
                    section = openSections.Count > 0 ? openSections[^1].Section : string.Empty;
                }
            }

            var occurrences = CountMarkers(line);
            if (occurrences == 0)
            {
                continue;
            }

            var files = DataFile().Matches(line)
                .Select(m => TunableMarkerAudit.NormaliseDataFilePath(m.Value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

            for (var occurrence = 0; occurrence < occurrences; occurrence++)
            {
                markers.Add(new TunableMarker(new DocSection(docId, section), index + 1, line.Trim(), files));
            }
        }

        return markers;
    }

    private static int CountMarkers(string line)
    {
        var count = 0;
        var at = line.IndexOf(Marker, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = line.IndexOf(Marker, at + Marker.Length, StringComparison.Ordinal);
        }

        return count;
    }

    [GeneratedRegex(@"^(\d{2})_")]
    private static partial Regex DocumentId();

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\d+[a-z]?(?:\.\d+[a-z]?)*)\.?(?:\s|$)")]
    private static partial Regex SectionNumber();

    [GeneratedRegex(@"[A-Za-z0-9_./:]*[A-Za-z0-9_]\.json")]
    private static partial Regex DataFile();
}

/// <summary>Reads the doc-section citations out of a JSON Schema's <c>description</c> keywords.</summary>
public static partial class SchemaCitationScanner
{
    /// <summary>Scans one schema document.</summary>
    /// <param name="schemaPath">Snapshot-relative path, e.g. <c>schema/forge.schema.json</c>.</param>
    /// <param name="schema">The parsed schema.</param>
    /// <param name="governsTuningFile">Whether the file this schema governs lives in <c>tuning/</c>.</param>
    public static IReadOnlyList<SchemaCitation> Scan(string schemaPath, ContentValue schema, bool governsTuningFile)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var citations = new List<SchemaCitation>();
        Walk(schemaPath, schema, string.Empty, governsTuningFile, citations);
        return citations;
    }

    private static void Walk(
        string schemaPath, ContentValue value, string pointer, bool governsTuningFile, List<SchemaCitation> citations)
    {
        if (value.Kind == ContentValueKind.Array)
        {
            for (var i = 0; i < value.Items.Count; i++)
            {
                Walk(schemaPath, value.Items[i], $"{pointer}/{i}", governsTuningFile, citations);
            }

            return;
        }

        if (value.Kind != ContentValueKind.Object)
        {
            return;
        }

        if (value.TryGetMember("description", out var description) &&
            description!.Kind == ContentValueKind.Text)
        {
            var numeric = DeclaresANumber(value);
            foreach (var section in Sections(description.AsText()))
            {
                citations.Add(new SchemaCitation(schemaPath, pointer, section, governsTuningFile, numeric));
            }
        }

        foreach (var name in value.MemberNames)
        {
            value.TryGetMember(name, out var member);
            Walk(schemaPath, member!, $"{pointer}/{name}", governsTuningFile, citations);
        }
    }

    /// <summary>
    /// True when this schema object asserts a numeric type — directly, or as an array of numbers,
    /// or as a map whose values are numbers. Those are the keys that hold 📐 numbers.
    /// </summary>
    private static bool DeclaresANumber(ContentValue schema)
    {
        if (schema.TryGetMember("type", out var type))
        {
            var names = type!.Kind == ContentValueKind.Array
                ? type.Items.Where(i => i.Kind == ContentValueKind.Text).Select(i => i.AsText())
                : type.Kind == ContentValueKind.Text ? [type.AsText()] : Array.Empty<string>();

            foreach (var name in names)
            {
                if (name is "number" or "integer")
                {
                    return true;
                }

                if (name is "array" && schema.TryGetMember("items", out var items) && DeclaresANumber(items!))
                {
                    return true;
                }
            }
        }

        if (schema.TryGetMember("patternProperties", out var patternProperties))
        {
            foreach (var pattern in patternProperties!.MemberNames)
            {
                patternProperties.TryGetMember(pattern, out var member);
                if (DeclaresANumber(member!))
                {
                    return true;
                }
            }
        }

        // A $ref to a shared numeric $def (rarityCostMap, rate01, powerCurve) is still a number key.
        return schema.TryGetMember("$ref", out var reference) &&
               reference!.Kind == ContentValueKind.Text &&
               NumericDefinitionName().IsMatch(reference.AsText());
    }

    /// <summary>
    /// Every <c>&lt;doc&gt; §&lt;section&gt;</c> a description cites, ranges expanded.
    /// <c>"08 §4.1-4.3 (merge, enhance, salvage) and 24 §6.1-6.2"</c> yields five sections across
    /// two documents.
    /// </summary>
    internal static IReadOnlyList<DocSection> Sections(string text)
    {
        var found = new List<DocSection>();

        foreach (Match match in Citation().Matches(text))
        {
            var docId = match.Groups[1].Value;
            var from = match.Groups[2].Value;
            var to = match.Groups[3].Success ? match.Groups[3].Value : null;

            found.Add(new DocSection(docId, from));

            if (to is null)
            {
                continue;
            }

            found.Add(new DocSection(docId, to));
            found.AddRange(Between(docId, from, to));
        }

        return found.Distinct().ToArray();
    }

    /// <summary>The sections strictly between two endpoints of a range that share a parent.</summary>
    private static IEnumerable<DocSection> Between(string docId, string from, string to)
    {
        var fromParts = from.Split('.');
        var toParts = to.Split('.');

        if (fromParts.Length != toParts.Length ||
            !fromParts[..^1].SequenceEqual(toParts[..^1], StringComparer.Ordinal) ||
            !int.TryParse(fromParts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(toParts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var end))
        {
            yield break;
        }

        var prefix = fromParts.Length == 1 ? string.Empty : string.Join('.', fromParts[..^1]) + ".";
        for (var i = start + 1; i < end; i++)
        {
            yield return new DocSection(docId, prefix + i.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Shared <c>$defs</c> whose name says they hold numbers (<c>rarityCostMap</c>, <c>rate01</c>,
    /// <c>powerCurve</c>, <c>hardPityStep</c>). A <c>$ref</c> to one of these is a number key even
    /// though the referring object declares no <c>type</c> of its own.
    /// </summary>
    [GeneratedRegex(
        "(?i)(cost|rate|curve|scalar|share|weight|multiplier|pity|price|power|amount|map|value|assertion)")]
    private static partial Regex NumericDefinitionName();

    [GeneratedRegex(@"\b(\d{2})\s*§\s*(\d+[a-z]?(?:\.\d+[a-z]?)*)(?:\s*[-–]\s*(\d+[a-z]?(?:\.\d+[a-z]?)*))?")]
    private static partial Regex Citation();
}

/// <summary>
/// 🔒 `14` §6's build-time check: <em>"a build-time check enumerates every 📐 marker in the
/// documentation set against the schema keys and fails on a mismatch. That check is what stops the
/// tuning surface eroding over eighteen months."</em>
/// </summary>
/// <remarks>
/// It runs in three directions:
/// <list type="number">
/// <item><b>Marker → schema.</b> Every 📐 marker's doc section must be cited by some schema key.</item>
/// <item><b>Schema → marker.</b> Every doc section cited by a <c>tuning/</c> schema must carry a 📐
/// marker. A tuning key nobody marked tunable is the same erosion running the other way.</item>
/// <item><b>Location.</b> An economy-affecting 📐 number must name a file under <c>tuning/</c>.</item>
/// </list>
/// </remarks>
public static class TunableMarkerAudit
{
    private const string TuningDirectory = "tuning/";

    /// <summary>
    /// 🔒 The <b>only</b> data files a 📐 marker may name from outside <c>tuning/</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `14` §6's locked scope is <em>"every <b>economy-affecting</b> tunable lives specifically in
    /// <c>SlayIdleRepeat.Data/tuning/</c>"</em>. Balance and content-identity numbers are neither
    /// economy nor sweepable by `21`, and the `21` §3.1 catalogue — the authority on what
    /// <c>tuning/</c> contains — does not list them. Each entry below is a file the design docs
    /// name explicitly, with the milestone that authors it.
    /// </para>
    /// <para>
    /// This list is <b>code, and pinned by a test</b> (<c>TunableMarkerAuditTests</c>) that asserts
    /// its exact contents and its length. Growing it means editing production code <em>and</em>
    /// changing an assertion that spells out why — which is the point. An escape hatch that widens
    /// quietly defeats the entire rule; `SlayIdleRepeat.Data/README.md`: <em>"Eighteen months of
    /// small, reasonable exceptions is how a tuning surface stops existing."</em>
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> NonEconomyDataFiles { get; } =
    [
        // 05 §2 — the combat caps (crit, dodge, mitigation, ward). Balance, not economy: the 21
        // simulator grades income and progression, and no sweep of these changes a currency rate.
        // 21 §3.1's catalogue does not list the file. Authored by M2-07 under content/.
        "combat_caps.json",

        // 05 §6-6.2 — enemy archetype statlines, biome-status rows and elite assignments. Content
        // identity (which enemy is which), not an economic dial. Authored by M2 under
        // content/enemies/.
        "enemies.json",

        // 28 Part D — the feat catalogue. Achievement definitions are content identity; their
        // Crown payouts are economy and live in currencies.json. Authored by M11 under
        // content/feats/.
        "feats.json",

        // 19 Part D — the first-time-user-experience script: which tutorial beat fires when, and
        // the fixed tutorial-shop row 16 A7 rules is NOT derived from 03 §7. Sequencing, not
        // economy; 21 sweeps nothing in it. Authored by M10.
        "ftue.json",

        // 17 §1.2 — boss definitions: phases, mechanics, HP shape. Content identity and combat
        // balance; the Crown and material payouts for killing one are economy and live in
        // currencies.json. Authored by M3 under content/bosses/.
        "bosses.json",
    ];

    /// <summary>
    /// Normalises a data-file path as the design docs write it (<c>data/tuning/x.json</c>,
    /// <c>res://data/x.json</c>) to a <c>SlayIdleRepeat.Data</c>-relative path.
    /// </summary>
    /// <remarks>
    /// The docs predate the directory's rename and write the same file three ways. Normalising is
    /// not leniency: the alternative is a check that passes because it did not recognise the path.
    /// </remarks>
    public static string NormaliseDataFilePath(string asWrittenInDocs)
    {
        ArgumentNullException.ThrowIfNull(asWrittenInDocs);

        var path = asWrittenInDocs.Replace('\\', '/').Trim();

        foreach (var prefix in (string[])["res://", "SlayIdleRepeat.Data/", "data/"])
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                path = path[prefix.Length..];
            }
        }

        return path;
    }

    /// <summary>Runs the audit.</summary>
    /// <param name="markers">Every 📐 marker in the documentation set.</param>
    /// <param name="citations">Every doc-section citation in the schema set.</param>
    /// <param name="baseline">The committed record of accepted mismatches.</param>
    /// <param name="tuningFileNames">
    /// The bare file names of <c>tuning/</c> (e.g. <c>luck.json</c>). The design docs name a tuning
    /// file three ways — <c>data/tuning/luck.json</c>, <c>tuning/luck.json</c> and plain
    /// <c>luck.json</c> — so the location rule resolves a bare name against the real catalogue
    /// rather than assuming the docs write a path.
    /// </param>
    public static TunableAuditReport Run(
        IReadOnlyList<TunableMarker> markers,
        IReadOnlyList<SchemaCitation> citations,
        TunableBaseline baseline,
        IReadOnlyCollection<string>? tuningFileNames = null)
    {
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(baseline);

        var catalogue = (tuningFileNames ?? []).ToHashSet(StringComparer.Ordinal);
        var issues = new List<ContentIssue>();

        // 🔒 The reverse direction is stated over the schema keys that hold NUMBERS. A description
        // on a container, an id or a `_doc` field records provenance; demanding a 📐 marker for
        // every section a schema ever mentions would bury the real mismatches in citations.
        var tuningCitations = citations.Where(c => c.GovernsTuningFile && c.GovernsNumericKey).ToArray();

        var unmatchedMarkers = markers
            .Where(m => !citations.Any(c => c.Section.Overlaps(m.Section)))
            .Select(m => m.Section)
            .Distinct()
            .OrderBy(s => s.DocId, StringComparer.Ordinal)
            .ThenBy(s => s.Section, StringComparer.Ordinal)
            .ToArray();

        var unmarkedCitations = tuningCitations
            .Where(c => !markers.Any(m => m.Section.Overlaps(c.Section)))
            .Select(c => c.Section)
            .Distinct()
            .OrderBy(s => s.DocId, StringComparer.Ordinal)
            .ThenBy(s => s.Section, StringComparer.Ordinal)
            .ToArray();

        Report(
            unmatchedMarkers, baseline.UnmatchedMarkers, ContentIssueCode.TunableMarkerUnmatched,
            "carries a 📐 TUNABLE marker that no schema key claims. Either the number belongs in " +
            "a tuning file and its schema key is missing, or the marker is stale. Add it to " +
            "build/content/tunable-marker-baseline.json with a reason and the milestone that " +
            "closes it, or fix it.",
            issues);

        Report(
            unmarkedCitations, baseline.UnmarkedSchemaCitations, ContentIssueCode.TunableKeyUnmarked,
            "is cited by a tuning schema but carries no 📐 TUNABLE marker in the documentation. A " +
            "tuning key nobody marked tunable is the tuning surface eroding from the other side.",
            issues);

        var stale = Stale(baseline.UnmatchedMarkers, unmatchedMarkers)
            .Concat(Stale(baseline.UnmarkedSchemaCitations, unmarkedCitations))
            .ToArray();

        foreach (var entry in stale)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.StaleBaselineEntry, entry.Section.ToString(),
                $"is recorded in the 📐 baseline ('{entry.Reason}', closed by {entry.ClosedBy}) but " +
                "no longer describes a real mismatch. Remove the entry — a baseline that outlives " +
                "its debt is a place mismatches go to be forgotten."));
        }

        foreach (var marker in markers)
        {
            foreach (var file in marker.NamedDataFiles)
            {
                if (file.StartsWith(TuningDirectory, StringComparison.Ordinal) ||
                    catalogue.Contains(file) ||
                    NonEconomyDataFiles.Contains(file, StringComparer.Ordinal))
                {
                    continue;
                }

                issues.Add(new ContentIssue(
                    ContentIssueCode.TunableOutsideTuningDirectory,
                    $"{marker.Section} (line {marker.Line})",
                    $"a 📐 number names '{file}', which is outside SlayIdleRepeat.Data/tuning/. " +
                    "14 §6: every economy-affecting tunable lives specifically in tuning/. If this " +
                    "number is balance or content identity rather than economy, add the file to " +
                    "TunableMarkerAudit.NonEconomyDataFiles with the reason and the milestone that " +
                    "authors it — and expect the test that pins that list to argue with you."));
            }
        }

        return new TunableAuditReport(issues, unmatchedMarkers, unmarkedCitations, stale);
    }

    private static void Report(
        IReadOnlyList<DocSection> actual,
        IReadOnlyList<TunableBaselineEntry> accepted,
        ContentIssueCode code,
        string message,
        List<ContentIssue> issues)
    {
        foreach (var section in actual.Where(s => !accepted.Any(a => a.Section == s)))
        {
            issues.Add(new ContentIssue(code, section.ToString(), message));
        }
    }

    private static IEnumerable<TunableBaselineEntry> Stale(
        IReadOnlyList<TunableBaselineEntry> accepted, IReadOnlyList<DocSection> actual) =>
        accepted.Where(a => !actual.Contains(a.Section));
}
