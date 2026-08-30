using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SlayIdleRepeat.Application.Tests.Parity;

/// <summary>The regeneration half of the committed parity baseline — the shape, never the reasons.</summary>
/// <remarks>
/// The documented command, and the only supported way to move a hash in
/// <c>ParityCorpusBaseline.json</c>:
/// <code>
/// SIR_M5_12_PARITY_OUT="$PWD/tests/SlayIdleRepeat.Application.Tests/Parity/ParityCorpusBaseline.json" \
///   dotnet test tests/SlayIdleRepeat.Application.Tests \
///   --filter "FullyQualifiedName~ClientServerParityTests.The_text_the_documented_regeneration_command_writes"
/// </code>
/// Run by hand from the repository root, never by CI. The destination is absolute on purpose: the
/// test host's working directory is its own output folder rather than the repository, so a relative
/// path lands where nothing reads it — and <see cref="ReasonsIn"/> refuses a destination that does
/// not exist rather than writing a table with every reason blanked.
/// <para>
/// The regenerated file does not pass: every render stamps <see cref="UnreviewedStatus"/> into the
/// review block and the reader refuses a table in that state.
/// </para>
/// </remarks>
internal static class ParityBaselineWriter
{
    /// <summary>The environment variable naming where a regenerated table is written.</summary>
    internal const string DestinationVariable = "SIR_M5_12_PARITY_OUT";

    /// <summary>The status every render stamps, and the one the reader refuses.</summary>
    internal const string UnreviewedStatus = "unreviewed";

    /// <summary>The status a human writes once they have read the diff and said why it moved.</summary>
    internal const string ReviewedStatus = "reviewed";

    /// <summary>The only file a regeneration may write, as a path suffix.</summary>
    internal const string CanonicalPath =
        "tests/SlayIdleRepeat.Application.Tests/Parity/ParityCorpusBaseline.json";

    /// <summary>Whether a destination names the one committed baseline, on either platform's separator.</summary>
    internal static bool NamesTheCommittedBaseline(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        return Path.GetFullPath(destination).Replace('\\', '/')
            .EndsWith(CanonicalPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What this table is, what it proves and what it does not, in the words the committed file carries.</summary>
    internal static IReadOnlyList<string> HeaderLines { get; } = new List<string>
    {
        "M5-12 — `14` §13's client/server parity corpus: 1 000 command sequences driven through the " +
        "in-process host and through the wire gateway, their state hashes compared step by step, and " +
        "the whole comparison pinned by 10 chunk hashes plus one aggregate.",

        "CHANGING A ROW IN THIS FILE IS A DETERMINISM BREAK, NEVER A TEST FIX. The only legitimate " +
        "edits are ADDING rows and a reviewed regeneration through the documented command in " +
        "ParityBaselineWriter. The writer stamps review.status = 'unreviewed'; this file's reader " +
        "refuses that, so a regenerated table nobody reviewed stays red.",

        "WHAT IT COMPARES, AND WHY THAT IS NOT VACUOUS. Both sides reference the same " +
        "SlayIdleRepeat.Core.dll, so a 'parity' test that ran one host twice would be green by " +
        "construction. What is compared here is two DIFFERENT dispatch pipelines over that one rules " +
        "library: the in-process host builds its GameContext and calls the use case directly, while " +
        "the gateway decodes a JSON envelope, resolves a sequencing scope, consults an idempotency " +
        "ledger, a throttle and a content pin, builds its own GameContext, and splits decide / commit " +
        "/ publish across a unit of work. Those are the two paths a command actually reaches the " +
        "domain by.",

        "⚠️ WHAT IT DOES NOT COMPARE: two runtimes. One process on one architecture cannot show that " +
        "a phone and a server agree about floating point. That is ci.yml's `determinism` job, on " +
        "Linux x64 and Android ARM64 — the iOS ARM64 leg is authored and gated off with iOS itself " +
        "(`16` D34). This corpus rides those legs too, and says nothing about them on its own.",

        // No astral-plane character in a rendered header line: the relaxed JSON encoder passes BMP
        // characters through and escapes anything above it, so a 🔴 would land in the committed file
        // as 🔴 and make the paragraph a reader most needs unreadable.
        "⚠️ START_RUN IS EXCLUDED, DELIBERATELY AND VISIBLY. It is the one command the two hosts are " +
        "DESIGNED to disagree on: the gateway allocates the run id from its id generator, the " +
        "in-process host passes no AllocatedRunId at all, and the run id is inside the hashed " +
        "projection. The server is the authority on run identity, so this is correct behaviour rather " +
        "than a defect — and it is pinned by its own case in ClientServerParityTests rather than left " +
        "as an unexplained gap in the alphabet. Every other one of the 55 registered commands is " +
        "driven.",

        "THE SEQUENCES ARE NOT COMMITTED, THE SEED IS. Each is generated by a legality-aware walker " +
        "over GameRules from baselineSeed and its own ordinal: most of the registry is refused from " +
        "most states, so a uniform walk would be a thousand sequences of refusals. A moved chunk " +
        "localises a break to a hundred sequences, which is a diff a person can read.",

        "⚠️ THIS TABLE IS SCHEDULED TO MOVE, ONCE. D45 (a hero's HP persists across a run — owner " +
        "M7-06g) and D46 (DMG% and DR% become multiplier stats — owner M4-16d) both change state " +
        "these sequences reach. Re-baseline AFTER BOTH have landed, never between them, so these " +
        "hashes move once rather than twice. A SchemaVersion bump moves them too, since the hashes " +
        "are taken over the wire projection.",
    };

    /// <summary>
    /// The whole file, as text. LF line endings and no byte-order mark, matching the
    /// <c>.gitattributes</c> beside it.
    /// </summary>
    internal static string Render(ParityCorpus corpus, IReadOnlyDictionary<string, string> reasons)
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

    /// <summary>The per-row reasons held in the table file being replaced.</summary>
    /// <exception cref="FileNotFoundException">There is no table at that path to carry reasons over from.</exception>
    internal static IReadOnlyDictionary<string, string> ReasonsIn(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"There is no parity baseline at '{path}' to regenerate. {DestinationVariable} names " +
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
        StringBuilder text, ParityCorpus corpus, IReadOnlyDictionary<string, string> reasons)
    {
        text.Append("  \"table\": {\n");
        text.Append("    \"sequences\": ").Append(Number(ParitySequenceGenerator.SequenceCount)).Append(",\n");
        text.Append("    \"chunkSize\": ").Append(Number(ParitySequenceGenerator.ChunkSize)).Append(",\n");
        text.Append("    \"baselineSeed\": ")
            .Append(Json("0x" + ParitySequenceGenerator.BaselineSeed.ToString("x16", CultureInfo.InvariantCulture)))
            .Append(",\n");
        text.Append("    \"baselineState\": ").Append(Json(corpus.BaselineHash)).Append(",\n");
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
            text.Append(", \"sequence\": ").Append(Number(named.Index));
            text.Append(", \"steps\": ").Append(Number(corpus.Outcomes[named.Index].Steps.Count));
            text.Append(", \"hash\": ").Append(Json(corpus.Wires[named.Index]));
            text.Append(", \"why\": ")
                .Append(Json(reasons.TryGetValue(named.Id, out var why) ? why : string.Empty))
                .Append(" }");
            text.Append(i == corpus.Named.Count - 1 ? "\n" : ",\n");
        }

        text.Append("    ]\n");
        text.Append("  }\n");
    }

    /// <summary>One JSON string, with UTF-8 left as UTF-8.</summary>
    private static string Json(string value) => JsonSerializer.Serialize(value, RelaxedUtf8);

    private static readonly JsonSerializerOptions RelaxedUtf8 = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
