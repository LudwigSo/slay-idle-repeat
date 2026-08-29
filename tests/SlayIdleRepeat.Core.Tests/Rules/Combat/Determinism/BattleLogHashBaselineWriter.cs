using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism;

/// <summary>The regeneration half of the committed <c>LogHash</c> baseline — the shape, never the reasons.</summary>
/// <remarks>
/// The documented command, and the only supported way to move a hash in
/// <c>BattleLogHashBaseline.json</c>:
/// <code>
/// SIR_M5_12_LOGHASH_OUT="$PWD/tests/SlayIdleRepeat.Core.Tests/Rules/Combat/Determinism/BattleLogHashBaseline.json" \
///   dotnet test tests/SlayIdleRepeat.Core.Tests \
///   --filter "FullyQualifiedName~BattleLogHashBaselineTests.The_text_the_documented_regeneration_command_writes"
/// </code>
/// Run by hand from the repository root, never by CI. The destination is absolute on purpose: the
/// test host's working directory is its own output folder rather than the repository, so a relative
/// path lands where nothing reads it — and <see cref="ReasonsIn"/> refuses a destination that does
/// not exist rather than writing a table with every reason blanked.
/// <para>
/// The regenerated file does not pass: every render stamps <see cref="UnreviewedStatus"/> into the
/// review block and the reader refuses a table in that state. A regenerated table nobody reviewed is
/// a determinism break accepted without anybody saying why.
/// </para>
/// <para>
/// The reviewer must, in order: read the git diff (a change to <c>aggregate</c> alone is impossible,
/// so one moved chunk means the break is localised to a hundred triples); establish which
/// accumulation point changed and whether that was intended; write the <c>why</c>, naming the change
/// and the task; then set <c>status</c> and stamp <c>reviewedOn</c>.
/// </para>
/// <para>
/// Per-row <c>why</c> strings are carried over from the file being replaced: a row's reason describes
/// the property of the corpus it pins, which a regeneration does not change.
/// </para>
/// </remarks>
internal static class BattleLogHashBaselineWriter
{
    /// <summary>The environment variable naming where a regenerated table is written.</summary>
    internal const string DestinationVariable = "SIR_M5_12_LOGHASH_OUT";

    /// <summary>The status every render stamps, and the one the reader refuses.</summary>
    internal const string UnreviewedStatus = "unreviewed";

    /// <summary>The status a human writes once they have read the diff and said why it moved.</summary>
    internal const string ReviewedStatus = "reviewed";

    /// <summary>
    /// The only file a regeneration may write, as a path suffix. The write branch lives inside a
    /// <c>[Fact]</c> that runs whenever <see cref="DestinationVariable"/> happens to be set —
    /// including during a plain <c>dotnet test</c> nobody intended as a regeneration — so it checks
    /// the path too.
    /// </summary>
    internal const string CanonicalPath =
        "tests/SlayIdleRepeat.Core.Tests/Rules/Combat/Determinism/BattleLogHashBaseline.json";

    /// <summary>Whether a destination names the one committed baseline, on either platform's separator.</summary>
    internal static bool NamesTheCommittedBaseline(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        return Path.GetFullPath(destination).Replace('\\', '/')
            .EndsWith(CanonicalPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What this table is, what it proves and what it does not, in the words the committed file
    /// carries. It lives on the writer because the writer renders it; the reader only checks the
    /// committed file still carries it word for word.
    /// </summary>
    internal static IReadOnlyList<string> HeaderLines { get; } = new List<string>
    {
        "M5-12 — `14` §8.2's determinism corpus: 10 000 fixed (seed, build, enemy) triples, simulated " +
        "through CombatSimulator.SimulateEncounter against the shipped game-data tree, and pinned by " +
        "100 chunk hashes over their per-triple LogHash rows plus one aggregate.",

        "CHANGING A ROW IN THIS FILE IS A DETERMINISM BREAK, NEVER A TEST FIX. The only legitimate " +
        "edits are ADDING rows and a reviewed regeneration through the documented command in " +
        "BattleLogHashBaselineWriter. The writer stamps review.status = 'unreviewed'; this file's " +
        "reader refuses that, so a regenerated table nobody reviewed stays red.",

        "THIS TABLE IS SELF-GENERATED AND HAS NO EXTERNAL PUBLISHER, exactly as M2-17's " +
        "Rules/Effects/Determinism/DslDeterminismBaseline.json is. Nobody publishes '(seed, build, " +
        "enemy) -> battle log hash' vectors for this game. What IS externally validated is the " +
        "arithmetic underneath every row: xxHash64 against its published known-answer vectors " +
        "(Rng/Hash64ReferenceVectors.json) and FNV-1a against Landon Curt Noll's test_fnv.c " +
        "(Model/Snapshots/CanonicalStateWriterReferenceVectors.json, " +
        "Rules/Combat/CombatLogReferenceVectors.json). The hash functions under these rows are " +
        "published; the rows are not.",

        "WHAT IT PROVES, AND THIS IS THE PART THAT IS NEW. Run on ONE machine it proves STABILITY: " +
        "the combat pipeline is a pure function of (seed, build, enemy) and cannot drift across a " +
        "refactor or a compiler without a chunk going red. Run by ci.yml's `determinism` job it " +
        "proves what `14` §8.2 actually asks for — that Linux x64 and Android ARM64 agree on every " +
        "one of these 10 000 LogHash values. That job is the reason M2-17's baseline header says its " +
        "cross-platform claim 'becomes true when that job does'; M5-12 is the task that turned the " +
        "job on, and this table rides the same legs.",

        "THE LIVE LEGS ARE LINUX x64 AND ANDROID ARM64. The iOS ARM64 leg is authored and gated off " +
        "with iOS itself (`16` D34) and is the first thing to re-enable if iOS returns, because " +
        "NativeAOT is a different runtime from the Mono/CoreCLR path the other two legs exercise.",

        "WHAT IT DOES NOT PROVE: that the combat arithmetic is CORRECT. Correctness comes from the " +
        "per-step attack-pipeline, stat-aggregation and enemy-derivation unit tests, which assert the " +
        "arithmetic against the design. A baseline generated from the code under test proves only " +
        "self-consistency, and calling these 'reference vectors' in the sense of the Hash64 and " +
        "CanonicalStateWriter tables would claim a validation that does not exist.",

        "⚠️ THIS TABLE IS SCHEDULED TO MOVE, ONCE, AND THE DATE IS NOT NEGOTIABLE. Two rulings " +
        "re-baseline every LogHash in the repository: D45 (a hero's HP persists across a whole run, " +
        "and revive restores 66% on first death — owner M7-06g) and D46 (DMG% and DR% become " +
        "multiplier stats consumed bare — owner M4-16d). Both are still open. Re-baseline AFTER BOTH " +
        "have landed, never between them, so these hashes move once rather than twice. Until then, " +
        "these numbers are correct and impermanent, and neither is a reason to treat a red chunk as " +
        "expected.",

        "THE CORPUS IS NOT COMMITTED, THE SEED IS. Every triple is a pure function of baselineSeed " +
        "and its own ordinal, drawn through Hash64 and DeterministicRng. Ten thousand raw rows would " +
        "be unreviewable; one aggregate would say only that something moved. A moved chunk localises " +
        "a break to a hundred triples, which is a diff a person can read.",
    };

    /// <summary>
    /// The whole file, as text. LF line endings and no byte-order mark, matching the
    /// <c>.gitattributes</c> beside it — so the table is byte-identical on both architectures the
    /// determinism job compares, and a one-hash change never diffs as a whole-file rewrite.
    /// </summary>
    internal static string Render(SimulatedCorpus corpus, IReadOnlyDictionary<string, string> reasons)
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
    /// the variable at a scratch path silently produce a table with blank <c>why</c>s.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no table at that path to carry reasons over from.</exception>
    internal static IReadOnlyDictionary<string, string> ReasonsIn(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"There is no LogHash baseline at '{path}' to regenerate. {DestinationVariable} names " +
                $"the file being REPLACED — the committed {CanonicalPath} — because the hand-written " +
                "'why' of every named row is carried over from it. Writing to a fresh path would blank " +
                "them all, and the reader then refuses the result.",
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
        StringBuilder text, SimulatedCorpus corpus, IReadOnlyDictionary<string, string> reasons)
    {
        text.Append("  \"table\": {\n");
        text.Append("    \"triples\": ").Append(Number(BattleTripleGenerator.TripleCount)).Append(",\n");
        text.Append("    \"chunkSize\": ").Append(Number(BattleTripleGenerator.ChunkSize)).Append(",\n");
        text.Append("    \"baselineSeed\": ")
            .Append(Json("0x" + BattleTripleGenerator.BaselineSeed.ToString("x16", CultureInfo.InvariantCulture)))
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
            var outcome = corpus.Outcomes[named.Index];
            text.Append("      { \"id\": ").Append(Json(named.Id));
            text.Append(", \"triple\": ").Append(Number(named.Index));
            text.Append(", \"logHash\": ")
                .Append(Json("0x" + outcome.LogHash.ToString("x16", CultureInfo.InvariantCulture)));
            text.Append(", \"hash\": ").Append(Json(corpus.WireOf(named)));
            text.Append(", \"why\": ")
                .Append(Json(reasons.TryGetValue(named.Id, out var why) ? why : string.Empty))
                .Append(" }");
            text.Append(i == corpus.Named.Count - 1 ? "\n" : ",\n");
        }

        text.Append("    ]\n");
        text.Append("  }\n");
    }

    /// <summary>One JSON string, with UTF-8 left as UTF-8.</summary>
    /// <remarks>
    /// The default encoder escapes every non-ASCII character to <c>\uXXXX</c>, which would render this
    /// file's own header — the part a reviewer most needs to read — as a wall of escapes. The relaxed
    /// encoder is safe here because the output is a committed file read by a JSON parser and by
    /// people, never interpolated into HTML.
    /// </remarks>
    private static string Json(string value) => JsonSerializer.Serialize(value, RelaxedUtf8);

    private static readonly JsonSerializerOptions RelaxedUtf8 = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
