using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>The regeneration half of the committed baseline — the shape, never the reasons.</summary>
/// <remarks>
/// The documented command, and the only supported way to move a hash in
/// <c>DslDeterminismBaseline.json</c>:
/// <code>
/// SIR_M2_17_BASELINE_OUT=tests/SlayIdleRepeat.Core.Tests/Rules/Effects/Determinism/DslDeterminismBaseline.json \
///   dotnet test tests/SlayIdleRepeat.Core.Tests \
///   --filter "FullyQualifiedName~DslDeterminismBaselineTests.The_text_the_documented_regeneration_command_writes"
/// </code>
/// Run by hand, never by CI.
/// <para>
/// The regenerated file does not pass: every render stamps <see cref="UnreviewedStatus"/> into the
/// review block and the reader refuses a table in that state. A regenerated table nobody reviewed is a
/// determinism break accepted without anybody saying why.
/// </para>
/// <para>
/// The reviewer must, in order: read the git diff (a change to <c>aggregate</c> alone is impossible,
/// so one moved chunk means the change is localised); establish which resolution step changed and
/// whether that was intended; write the <c>why</c>, naming the change and the task; then set
/// <c>status</c> and stamp <c>reviewedOn</c>.
/// </para>
/// <para>
/// Per-row <c>why</c> strings are carried over from the file being replaced: a row's reason describes
/// the property it pins, which a regeneration does not change.
/// </para>
/// </remarks>
internal static class DslDeterminismBaselineWriter
{
    /// <summary>The environment variable naming where a regenerated table is written.</summary>
    internal const string DestinationVariable = "SIR_M2_17_BASELINE_OUT";

    /// <summary>The status every render stamps, and the one the reader refuses.</summary>
    internal const string UnreviewedStatus = "unreviewed";

    /// <summary>
    /// The only file a regeneration may write, as a path suffix. Unlike a CLI flag on a tool run
    /// deliberately, this is a <c>[Fact]</c> that writes whenever <see cref="DestinationVariable"/>
    /// happens to be set — including during a plain <c>dotnet test</c> nobody intended as a
    /// regeneration. So the write branch checks the path too.
    /// </summary>
    internal const string CanonicalPath =
        "tests/SlayIdleRepeat.Core.Tests/Rules/Effects/Determinism/DslDeterminismBaseline.json";

    /// <summary>
    /// Whether a destination names the one committed baseline, on either platform's separator.
    /// </summary>
    internal static bool NamesTheCommittedBaseline(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        return Path.GetFullPath(destination).Replace('\\', '/')
            .EndsWith(CanonicalPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The status a human writes once they have read the diff and said why it moved.</summary>
    internal const string ReviewedStatus = "reviewed";

    /// <summary>
    /// The self-generated limitation, in the words the committed file carries. It lives on the writer
    /// because the writer renders it; the reader only checks the committed file still carries it word
    /// for word.
    /// </summary>
    internal static IReadOnlyList<string> HeaderLines { get; } = new List<string>
    {
        "M2-17 — the `18` §8 determinism baseline over 10 000 seeded build permutations.",

        "THIS TABLE IS SELF-GENERATED AND HAS NO EXTERNAL PUBLISHER. M0-06's Hash64 rows and " +
        "M0-07's canonical-encoding rows were validated against EXTERNALLY PUBLISHED vectors — " +
        "xxHash64's, and Landon Curt Noll's test_fnv.c — because xxHash64 and FNV-1a are published " +
        "algorithms. There is no published authority for 'build permutation -> hash', so these rows " +
        "were produced by the very resolver they guard.",

        "WHAT IT THEREFORE PROVES: STABILITY. The `18` §8 pipeline is a pure function of its inputs, " +
        "and its output cannot drift across a refactor or a compiler without a row here going red. " +
        "That is exactly the accidental order-dependence `18` §11's parity line exists to catch. " +
        "ACROSS A PLATFORM it proves nothing yet: .github/workflows/ci.yml's determinism job is " +
        "'if: false — gated: turns on with M5-12', so nothing runs this table on a second runtime " +
        "today. The claim becomes true when that job does.",

        "WHY IT IS NOT AN INDEPENDENT TRANSCRIPTION, WHICH IS THE BAR M2-15 MET. " +
        "Rules/Combat/CombatLogReferenceVectors.json faced the same absence of a publisher — nobody " +
        "publishes 'event list -> LogHash' vectors for this game — and still refused to generate from " +
        "the code under test: its rows came from an independent Python transcription of `14` §16.6's " +
        "encoding table and `05` §7's field list. That route was available there because the subject " +
        "was a BYTE ENCODING: one table, transcribable in an afternoon. THE REASON IT WAS NOT TAKEN " +
        "HERE IS COST, AND NOTHING MORE PRINCIPLED THAN COST. The subject is a ten-step resolution " +
        "pipeline — collection from ten sources, a 23-function condition gate, five arithmetic steps " +
        "in ordinal effect-id order, three kinds of cap override and per-step rounding — so a " +
        "transcription would have to re-derive the whole of M2-02..M2-07's arithmetic, and would be " +
        "roughly the size of the code it checks. That is a scope judgement M2-17 made and a later " +
        "task may unmake; it is NOT a rule against second implementations. `18` §8's headnote is " +
        "about divergence between SHIPPED implementations, and §11's parity line presupposes two — " +
        "neither M0-06 nor M2-15 kept its generator, both ran a throwaway once and committed only the " +
        "output, which is exactly what a Python transcription of §8 would be. So the provenance bar " +
        "here is genuinely lower than M2-15's, and this paragraph exists to say so rather than to " +
        "explain it away.",

        "WHAT IT DOES NOT PROVE: that the encoding or the resolution order is CORRECT. Correctness " +
        "comes from M2-02..M2-07's per-op and per-step unit tests, which assert `18` §8's arithmetic " +
        "against the document. A baseline generated from the code under test proves only " +
        "self-consistency, and calling these 'reference vectors' in M0-06's sense would claim a " +
        "validation that does not exist.",

        "WHAT WAS CONFIRMED FIRST: before a single row below was written, CanonicalStateWriter was " +
        "re-confirmed against FNV-1a's published vectors — Fnv1a64KnownAnswerTests and " +
        "CanonicalStateWriterReferenceVectorTests, driven by " +
        "Model/Snapshots/CanonicalStateWriterReferenceVectors.json. The hash function underneath " +
        "these rows is externally validated even though the rows are not.",

        "SUCCESSOR: real two-runtime parity is M5-12, on Linux x64 and Android ARM64 — the iOS ARM64 " +
        "leg is authored but gated off with iOS itself (`16` D34). There is one resolver in one " +
        "assembly today and no client build until M7, so `18` §11's 'client and server resolvers " +
        "agree' has nothing to compare against yet. NOTE THE OBLIGATION IS NOT YET BOOKED: M5-12's " +
        "tracker row names the LogHash table and the command-sequence parity test and does NOT name " +
        "this one. M2-17 may not edit the tracker; the carry-forward was reported to the conductor " +
        "instead, and until that row is amended nothing makes M5-12 pick this table up.",

        "A FAILURE HERE IS A DETERMINISM BREAK, NEVER A TEST FIX. Regenerate only through the " +
        "documented command in DslDeterminismBaselineWriter, and only after writing down why the " +
        "hash moved. The writer stamps review.status = 'unreviewed'; this file's reader refuses that, " +
        "so a regenerated table nobody reviewed stays red.",
    };

    /// <summary>
    /// The whole file, as text. LF line endings and no byte-order mark, matching the
    /// <c>.gitattributes</c> beside it — so the table is byte-identical on every platform M5-12 runs
    /// on and a one-hash change never diffs as a whole-file rewrite.
    /// </summary>
    internal static string Render(ResolvedCorpus corpus, IReadOnlyDictionary<string, string> reasons)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(reasons);

        var text = new StringBuilder();
        text.Append("{\n");
        RenderComment(text);
        RenderReview(text);
        RenderTable(text, corpus, reasons);
        text.Append("}\n");

        return text.ToString();
    }

    /// <summary>
    /// The per-row reasons held in the table file being replaced. Refuses a destination that does not
    /// exist, which is the whole safety of the carry-over — an empty map instead would let pointing
    /// the variable at a scratch path silently produce a table with blank <c>why</c>s. Regeneration
    /// replaces a table; it does not create one from nothing.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no table at that path to carry reasons over from.</exception>
    internal static IReadOnlyDictionary<string, string> ReasonsIn(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"There is no determinism baseline at '{path}' to regenerate. {DestinationVariable} names " +
                "the file being REPLACED — the committed " +
                "tests/SlayIdleRepeat.Core.Tests/Rules/Effects/Determinism/DslDeterminismBaseline.json — " +
                "because the hand-written 'why' of every named row is carried over from it. Writing to " +
                "a fresh path would blank all twelve, and the reader then refuses the result.",
                path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in document.RootElement.GetProperty("table").GetProperty("named").EnumerateArray())
        {
            reasons[row.GetProperty("id").GetString()!] = row.GetProperty("why").GetString() ?? string.Empty;
        }

        return reasons;
    }

    private static void RenderComment(StringBuilder text)
    {
        text.Append("  \"$comment\": [\n");
        foreach (var line in HeaderLines)
        {
            text.Append("    ").Append(Json(line)).Append(",\n");
        }

        // The trailing element carries no comma; rendered separately so the list above stays a
        // straight transcription of the header the reader asserts against.
        text.Length -= 2;
        text.Append("\n  ],\n");
    }

    private static void RenderReview(StringBuilder text)
    {
        text.Append("  \"review\": {\n");
        text.Append("    \"status\": ").Append(Json(UnreviewedStatus)).Append(",\n");
        text.Append("    \"reviewedOn\": \"\",\n");
        text.Append("    \"reviewedBy\": \"\",\n");
        text.Append("    \"why\": \"\"\n");
        text.Append("  },\n");
    }

    private static void RenderTable(
        StringBuilder text, ResolvedCorpus corpus, IReadOnlyDictionary<string, string> reasons)
    {
        text.Append("  \"table\": {\n");
        text.Append("    \"permutations\": ").Append(Number(BuildPermutationGenerator.PermutationCount)).Append(",\n");
        text.Append("    \"chunkSize\": ").Append(Number(BuildPermutationGenerator.ChunkSize)).Append(",\n");
        text.Append("    \"baselineSeed\": ")
            .Append(Json("0x" + BuildPermutationGenerator.BaselineSeed.ToString("x16", CultureInfo.InvariantCulture)))
            .Append(",\n");
        text.Append("    \"aggregate\": ").Append(Json(corpus.Aggregate)).Append(",\n");

        text.Append("    \"chunks\": [\n");
        for (var i = 0; i < corpus.ChunkWires.Count; i++)
        {
            text.Append("      ").Append(Json(corpus.ChunkWires[i]));
            text.Append(i == corpus.ChunkWires.Count - 1 ? "\n" : ",\n");
        }

        text.Append("    ],\n");

        text.Append("    \"named\": [\n");
        for (var i = 0; i < corpus.Named.Count; i++)
        {
            var named = corpus.Named[i];
            text.Append("      { \"id\": ").Append(Json(named.Id));
            text.Append(", \"permutation\": ").Append(Number(named.Index));
            text.Append(", \"hash\": ").Append(Json(corpus.WireOf(named)));
            text.Append(", \"why\": ")
                .Append(Json(reasons.TryGetValue(named.Id, out var why) ? why : string.Empty))
                .Append(" }");
            text.Append(i == corpus.Named.Count - 1 ? "\n" : ",\n");
        }

        text.Append("    ]\n");
        text.Append("  }\n");
    }

    /// <summary>
    /// One JSON string, with UTF-8 left as UTF-8.
    /// </summary>
    /// <remarks>
    /// The default encoder escapes every non-ASCII character to <c>\uXXXX</c>, which would render
    /// this file's own header — the part a reviewer most needs to read — as a wall of escapes. The
    /// relaxed encoder is safe here because the output is a committed file read by a JSON parser and
    /// by people, never interpolated into HTML.
    /// </remarks>
    private static string Json(string value) => JsonSerializer.Serialize(value, RelaxedUtf8);

    private static readonly JsonSerializerOptions RelaxedUtf8 = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
