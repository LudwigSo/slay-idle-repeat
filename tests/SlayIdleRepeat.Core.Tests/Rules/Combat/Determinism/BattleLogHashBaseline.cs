using System.Text;
using System.Text.Json;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism;

/// <summary>One individually pinned triple, as the committed table holds it.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Triple">The triple ordinal the row's property first holds at.</param>
/// <param name="LogHash">
/// `14` §8.2's own subject, written out in hexadecimal so the value the two architectures compare is
/// legible in the diff rather than folded into the wire hash beside it.
/// </param>
/// <param name="Hash">The wire hash of the whole simulated outcome.</param>
/// <param name="Why">Which property of the corpus this row defends. Hand-written, never generated.</param>
internal sealed record LogHashRow(string Id, int Triple, string LogHash, string Hash, string Why);

/// <summary>
/// Reads <c>BattleLogHashBaseline.json</c> — `14` §8.2's committed determinism corpus — out of this
/// assembly's embedded resources.
/// </summary>
/// <remarks>
/// Embedded rather than copied to the output directory so the cross-architecture determinism job
/// finds it on Android ARM64, where the working directory is not ours to predict.
/// <see cref="Validate"/> refuses an unreviewed table, an incomplete review block and any named row
/// with no <c>why</c> — so a regenerated table nobody hand-edited fails on the next run, which is the
/// whole point.
/// </remarks>
internal static class BattleLogHashBaseline
{
    private const string ResourceName =
        "SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism.BattleLogHashBaseline.json";

    /// <summary>
    /// The committed file's text, exactly as it is committed — what the regeneration round-trip
    /// compares its render against, so the writer's <em>format</em> is pinned and not only its values.
    /// </summary>
    internal static string RawText { get; } = ReadResource();

    private static readonly JsonDocument Document = Validate(JsonDocument.Parse(RawText));

    /// <summary>The header the committed file carries, as it carries it.</summary>
    internal static IReadOnlyList<string> Comment { get; } = Document.RootElement
        .GetProperty("$comment").EnumerateArray().Select(line => line.GetString()!).ToArray();

    /// <summary>The triple count the committed table was built over.</summary>
    internal static int Triples { get; } = Table.GetProperty("triples").GetInt32();

    /// <summary>How many triples one committed chunk hash covers.</summary>
    internal static int ChunkSize { get; } = Table.GetProperty("chunkSize").GetInt32();

    /// <summary>The corpus seed the committed table names, as hexadecimal.</summary>
    internal static string BaselineSeed { get; } = Table.GetProperty("baselineSeed").GetString()!;

    /// <summary>The one hash over all 100 chunks.</summary>
    internal static string Aggregate { get; } = Table.GetProperty("aggregate").GetString()!;

    /// <summary>The 100 chunk hashes, in ordinal order.</summary>
    internal static IReadOnlyList<string> Chunks { get; } = Table
        .GetProperty("chunks").EnumerateArray().Select(chunk => chunk.GetString()!).ToArray();

    /// <summary>The individually pinned rows.</summary>
    internal static IReadOnlyList<LogHashRow> Named { get; } = RowsIn(Document);

    /// <summary>The review block's status. Anything but <c>reviewed</c> has already thrown.</summary>
    internal static string ReviewStatus { get; } = Review.GetProperty("status").GetString()!;

    /// <summary>The date a human last reviewed a regeneration of this table.</summary>
    internal static string ReviewedOn { get; } = Review.GetProperty("reviewedOn").GetString()!;

    /// <summary>Which task's review this is. Blank has already thrown.</summary>
    internal static string ReviewedBy { get; } = Review.GetProperty("reviewedBy").GetString()!;

    /// <summary>Why the table holds the hashes it holds — hand-written, never generated.</summary>
    internal static string ReviewWhy { get; } = Review.GetProperty("why").GetString()!;

    /// <summary>The named row with this id.</summary>
    internal static LogHashRow Row(string id) =>
        Named.Single(row => row.Id.Equals(id, StringComparison.Ordinal));

    /// <summary>Every named row's <c>why</c>, keyed by id — what a regeneration carries over.</summary>
    internal static IReadOnlyDictionary<string, string> Reasons { get; } =
        Named.ToDictionary(row => row.Id, row => row.Why, StringComparer.Ordinal);

    private static JsonElement Table => Document.RootElement.GetProperty("table");

    private static JsonElement Review => Document.RootElement.GetProperty("review");

    private static string ReadResource()
    {
        using var stream = typeof(BattleLogHashBaseline).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The LogHash determinism baseline '{ResourceName}' is not embedded in " +
                $"{typeof(BattleLogHashBaseline).Assembly.GetName().Name}. Available: " +
                string.Join(", ", typeof(BattleLogHashBaseline).Assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        return reader.ReadToEnd();
    }

    /// <summary>
    /// The three refusals, in one place a test can reach.
    /// </summary>
    /// <remarks>
    /// Separated from the static initialiser deliberately: inline, the only thing that could exercise
    /// them would be the committed file, which passes — so the tests over them would be asserting
    /// conditions the loader had already guaranteed, and could not fail.
    /// </remarks>
    /// <exception cref="FormatException">The table is unreviewed, the review is incomplete, or a reason is missing.</exception>
    internal static JsonDocument Validate(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var review = document.RootElement.GetProperty("review");

        var status = review.GetProperty("status").GetString();
        if (!string.Equals(status, BattleLogHashBaselineWriter.ReviewedStatus, StringComparison.Ordinal))
        {
            throw new FormatException(
                $"BattleLogHashBaseline.json is still '{status}'. The regeneration command writes the " +
                "SHAPE — 10 000 triples, 100 chunk hashes and one aggregate — and stops there. Which " +
                "accumulation point moved, and whether it was meant to, is written by hand. A " +
                "generated reason is not a reason, and an unreviewed determinism break is a " +
                "determinism break that was accepted without anybody saying so. Read the diff, write " +
                "review.why, then set review.status to " +
                $"'{BattleLogHashBaselineWriter.ReviewedStatus}'.");
        }

        if (string.IsNullOrWhiteSpace(review.GetProperty("why").GetString()) ||
            string.IsNullOrWhiteSpace(review.GetProperty("reviewedOn").GetString()) ||
            string.IsNullOrWhiteSpace(review.GetProperty("reviewedBy").GetString()))
        {
            throw new FormatException(
                "BattleLogHashBaseline.json says it was reviewed and does not say who concluded what, " +
                "or when. review.why names the change that moved the hashes and the task that made it, " +
                "review.reviewedBy names the task and review.reviewedOn the date; without all three " +
                "the 'reviewed' status is a checkbox rather than a record.");
        }

        var unexplained = RowsIn(document)
            .Where(row => string.IsNullOrWhiteSpace(row.Why))
            .Select(row => row.Id)
            .ToArray();

        if (unexplained.Length > 0)
        {
            throw new FormatException(
                "These named rows of BattleLogHashBaseline.json carry no 'why': " +
                $"{string.Join(", ", unexplained)}. A named row exists to say which property of the " +
                "corpus it defends — 'the first triple whose fight ran to the tick cap', not 'triple " +
                "7'. A row that cannot say what it pins pins nothing a failure message can use. The " +
                "regeneration command carries existing reasons over and writes an empty one for a row " +
                "it has never seen; fill it in by hand.");
        }

        return document;
    }

    private static IReadOnlyList<LogHashRow> RowsIn(JsonDocument document) =>
        document.RootElement.GetProperty("table").GetProperty("named").EnumerateArray()
            .Select(row => new LogHashRow(
                row.GetProperty("id").GetString()!,
                row.GetProperty("triple").GetInt32(),
                row.GetProperty("logHash").GetString()!,
                row.GetProperty("hash").GetString()!,
                row.GetProperty("why").GetString() ?? string.Empty))
            .ToArray();
}
