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
    /// different document is not. The <c>"."</c> in the prefix test is what stops <c>§4</c>
    /// swallowing <c>§40</c>.
    /// </remarks>
    public bool Overlaps(DocSection other)
    {
        if (!string.Equals(DocId, other.DocId, StringComparison.Ordinal))
        {
            return false;
        }

        if (Section.Length == 0 || other.Section.Length == 0 ||
            string.Equals(Section, other.Section, StringComparison.Ordinal))
        {
            return true;
        }

        return other.Section.StartsWith(Section + ".", StringComparison.Ordinal)
            || Section.StartsWith(other.Section + ".", StringComparison.Ordinal);
    }
}

/// <summary>One 📐 TUNABLE marker found in the documentation set.</summary>
/// <param name="Section">Where it sits.</param>
/// <param name="Line">1-based line number, for the report.</param>
/// <param name="Text">The line it was found on, trimmed.</param>
/// <param name="NamedDataFiles">
/// Data files the marker names (<c>data/tuning/currencies.json</c>, <c>res://data/combat_caps.json</c>),
/// normalised to a <c>game-data</c>-relative path.
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
/// <param name="GovernsNumericKey">
/// True when the schema object carrying this description declares a numeric type. `14` §6's check
/// is stated over <em>the schema keys</em> that hold 📐 numbers; a description on a container
/// object, an id string or a <c>_doc</c> field records provenance, not a tunable, and holding it to
/// the reverse direction would demand a 📐 marker for every section a schema ever mentions.
/// </param>
public sealed record SchemaCitation(
    string SchemaPath,
    string PropertyPointer,
    DocSection Section,
    bool GovernsTuningFile,
    bool GovernsNumericKey = false);

/// <summary>
/// 🔒 What sort of mismatch a baseline entry records. The two are not interchangeable.
/// </summary>
public enum TunableBaselineKind
{
    /// <summary>
    /// A real hole that a real milestone task closes. Carries a <c>closedBy</c> naming a task id
    /// that exists in <c>IMPLEMENTATION_TRACKER.md</c>.
    /// </summary>
    SpecDebt = 0,

    /// <summary>
    /// The marker is not a tunable at all — a glossary row, prose about the rule itself, a
    /// verification instruction, an art-budget line, server configuration. Nothing closes it
    /// because there is nothing to close, so it carries no <c>closedBy</c>.
    /// </summary>
    OutOfScope = 1,
}

/// <summary>One accepted, dated, reasoned mismatch.</summary>
/// <param name="Section">The doc section that does not match.</param>
/// <param name="Kind">Spec debt with an owner, or a permanent scope exclusion.</param>
/// <param name="Reason">Why, in one line.</param>
/// <param name="ClosedBy">
/// The milestone task that removes this entry. Empty — and required to be empty — for
/// <see cref="TunableBaselineKind.OutOfScope"/>.
/// </param>
public sealed record TunableBaselineEntry(
    DocSection Section, TunableBaselineKind Kind, string Reason, string ClosedBy);

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
    public static TunableBaseline FromContent(ContentValue root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new TunableBaseline(
            Text(root, "recordedOn"),
            Entries(root, "unmatchedMarkers"),
            Entries(root, "unmarkedSchemaCitations"));
    }

    /// <summary>
    /// The literal a regenerated-but-unreviewed entry carries. The reader refuses it, which is how
    /// <c>--write-baseline</c> can emit a shape without emitting an owner nobody wrote.
    /// </summary>
    public const string UnreviewedKind = "unreviewed";

    private static string Text(ContentValue value, string member) =>
        value.TryGetMember(member, out var found) && found!.Kind == ContentValueKind.Text
            ? found.AsText()
            : throw new FormatException(
                $"The 📐 baseline has no '{member}'. A baseline without a date is a baseline nobody " +
                "can tell is stale.");

    private static IReadOnlyList<TunableBaselineEntry> Entries(ContentValue root, string member)
    {
        if (!root.TryGetMember(member, out var list) || list!.Kind != ContentValueKind.Array)
        {
            return [];
        }

        return list.Items.Select(Entry).ToArray();
    }

    /// <summary>
    /// 🔒 The two kinds are enforced here rather than left to a convention.
    /// </summary>
    /// <remarks>
    /// <c>--write-baseline</c> emits <c>kind: "unreviewed"</c> and no owner, so a regenerated file
    /// that nobody hand-edited fails the very next run by name. The alternative it replaced — a
    /// literal <c>"TODO"</c> owner — passed every check, because the only assertion in reach was
    /// <c>ClosedBy.Length &gt; 0</c>.
    /// </remarks>
    private static TunableBaselineEntry Entry(ContentValue item)
    {
        var section = new DocSection(Text(item, "doc"), Text(item, "section"));
        var kindName = Text(item, "kind");

        var kind = kindName switch
        {
            "specDebt" => TunableBaselineKind.SpecDebt,
            "outOfScope" => TunableBaselineKind.OutOfScope,
            UnreviewedKind => throw new FormatException(
                $"The 📐 baseline entry for {section} is still 'unreviewed'. --write-baseline writes " +
                "the SHAPE; the reason and the owning milestone task are written by hand. A generated " +
                "reason is not a reason."),
            _ => throw new FormatException(
                $"The 📐 baseline entry for {section} has kind '{kindName}'. It is either 'specDebt' " +
                "(a hole a milestone task closes) or 'outOfScope' (not a tunable at all, so nothing " +
                "closes it). Those are different facts and the file records which."),
        };

        var hasOwner = item.TryGetMember("closedBy", out var closedBy) &&
                       closedBy!.Kind == ContentValueKind.Text;

        return kind switch
        {
            TunableBaselineKind.SpecDebt when !hasOwner => throw new FormatException(
                $"The 📐 baseline entry for {section} is spec debt with no 'closedBy'. Spec debt " +
                "nobody owns is spec debt nobody closes."),
            TunableBaselineKind.OutOfScope when hasOwner => throw new FormatException(
                $"The 📐 baseline entry for {section} is out of scope but names a closing task " +
                $"'{closedBy!.AsText()}'. Nothing closes it — that is what out of scope means. If a " +
                "milestone really does close it, it is spec debt."),
            _ => new TunableBaselineEntry(
                section, kind, Text(item, "reason"), hasOwner ? closedBy!.AsText() : string.Empty),
        };
    }
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
