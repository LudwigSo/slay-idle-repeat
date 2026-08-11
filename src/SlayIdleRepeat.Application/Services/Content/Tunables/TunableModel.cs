using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content.Tunables;

/// <summary>
/// A doc-section citation — the join between a 📐 marker in the documentation and the schema key
/// that holds the number.
/// </summary>
/// <remarks>
/// M0-10 authored this link deliberately: every schema property description opens with the section
/// that owns its numbers (<c>"08 §4.1 — …"</c>), and every 📐 marker sits under a numbered heading
/// in a numbered document. The pair <c>(08, 4.1)</c> is therefore addressable from both sides,
/// which is the only reason the `14` §6 build check can exist at all.
/// </remarks>
/// <param name="DocId">The two-digit document number, e.g. <c>08</c>.</param>
/// <param name="Section">The section number, e.g. <c>4.1</c> or <c>7a.3</c>. Empty means whole-document.</param>
public readonly record struct DocSection(string DocId, string Section)
{
    /// <summary>The <c>08 §4.1</c> form.</summary>
    public override string ToString() => Section.Length == 0 ? DocId : $"{DocId} §{Section}";

    /// <summary>
    /// True when this section is the same as, an ancestor of, or a descendant of
    /// <paramref name="other"/> — <c>08 §4</c> matches <c>08 §4.1</c> in both directions.
    /// </summary>
    /// <remarks>
    /// Deliberately generous in that one axis and strict everywhere else. A schema that cites the
    /// parent section of the marker it implements is right, not wrong; a schema that cites a
    /// different document is not.
    /// </remarks>
    public bool Overlaps(DocSection other) => throw new NotImplementedException();
}

/// <summary>One 📐 TUNABLE marker found in the documentation set.</summary>
/// <param name="Section">Where it sits.</param>
/// <param name="Line">1-based line number, for the report.</param>
/// <param name="Text">The line it was found on, trimmed.</param>
/// <param name="NamedDataFiles">
/// Data files the marker names (<c>data/tuning/currencies.json</c>, <c>res://data/combat_caps.json</c>),
/// normalised to a <c>SlayIdleRepeat.Data</c>-relative path.
/// </param>
public sealed record TunableMarker(
    DocSection Section,
    int Line,
    string Text,
    IReadOnlyList<string> NamedDataFiles);

/// <summary>A doc-section citation read out of a schema property description.</summary>
/// <param name="SchemaPath">Snapshot-relative schema path, e.g. <c>schema/forge.schema.json</c>.</param>
/// <param name="PropertyPointer">Where in the schema the description sits.</param>
/// <param name="Section">The section it cites.</param>
/// <param name="GovernsTuningFile">
/// True when this schema governs a file in <c>tuning/</c> — only those are economy-affecting and
/// only those are held to the reverse direction of the check.
/// </param>
public sealed record SchemaCitation(
    string SchemaPath,
    string PropertyPointer,
    DocSection Section,
    bool GovernsTuningFile);

/// <summary>One accepted, dated, reasoned mismatch.</summary>
/// <param name="Section">The doc section that does not match.</param>
/// <param name="Reason">Why, in one line.</param>
/// <param name="ClosedBy">The milestone task that removes this entry.</param>
public sealed record TunableBaselineEntry(DocSection Section, string Reason, string ClosedBy);

/// <summary>
/// 🔒 The committed record of every known 📐 mismatch — spec debt, measured rather than hidden.
/// </summary>
/// <remarks>
/// The check fails on anything <em>not</em> in here, and equally on an entry in here that no
/// longer describes a real mismatch. Both halves matter: without the first the rule does not bite,
/// without the second the baseline becomes a place mismatches go to be forgotten. Every entry
/// carries a reason and the milestone that closes it, and the file carries the date it was taken.
/// </remarks>
/// <param name="RecordedOn">ISO-8601 date the baseline was taken. Authored, never <c>UtcNow</c>.</param>
/// <param name="UnmatchedMarkers">📐 markers with no schema key yet.</param>
/// <param name="UnmarkedSchemaCitations">Tuning schema citations with no 📐 marker.</param>
public sealed record TunableBaseline(
    string RecordedOn,
    IReadOnlyList<TunableBaselineEntry> UnmatchedMarkers,
    IReadOnlyList<TunableBaselineEntry> UnmarkedSchemaCitations)
{
    /// <summary>An empty baseline — every mismatch fails.</summary>
    public static TunableBaseline None { get; } = new("0001-01-01", [], []);

    /// <summary>The total number of accepted mismatches. The headline spec-debt number.</summary>
    public int Count => UnmatchedMarkers.Count + UnmarkedSchemaCitations.Count;

    /// <summary>Reads a baseline from its committed JSON form.</summary>
    public static TunableBaseline FromContent(ContentValue root) => throw new NotImplementedException();
}

/// <summary>What the 📐 audit found.</summary>
/// <param name="Issues">Findings that fail the build.</param>
/// <param name="UnmatchedMarkers">📐 markers no schema key claims, baseline included.</param>
/// <param name="UnmarkedCitations">Tuning schema citations with no 📐 marker, baseline included.</param>
/// <param name="StaleBaselineEntries">Baseline entries that no longer describe a real mismatch.</param>
public sealed record TunableAuditReport(
    IReadOnlyList<ContentIssue> Issues,
    IReadOnlyList<DocSection> UnmatchedMarkers,
    IReadOnlyList<DocSection> UnmarkedCitations,
    IReadOnlyList<TunableBaselineEntry> StaleBaselineEntries)
{
    /// <summary>True when nothing outside the baseline is wrong.</summary>
    public bool Succeeded => Issues.Count == 0;
}
