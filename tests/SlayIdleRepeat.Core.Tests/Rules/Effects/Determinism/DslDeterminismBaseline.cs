using System.Text;
using System.Text.Json;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>One individually pinned permutation, as the committed table holds it.</summary>
internal sealed record BaselineRow(string Id, int Permutation, string Hash, string Why);

/// <summary>Reads <c>DslDeterminismBaseline.json</c> — the committed determinism baseline — out of this assembly's embedded resources.</summary>
/// <remarks>
/// Not a reference-vector table: there is no published authority for build permutation → hash. This
/// table is self-generated and proves stability, not correctness. <see cref="Validate"/> refuses an
/// unreviewed table, an incomplete review block, and any named row with no <c>why</c> — so a
/// regenerated table nobody hand-edited fails on the next run, which is the whole point.
/// </remarks>
internal static class DslDeterminismBaseline
{
    private const string ResourceName =
        "SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism.DslDeterminismBaseline.json";

    /// <summary>
    /// The committed file's text, exactly as it is committed — what the regeneration round-trip
    /// compares its render against, so the writer's <em>format</em> is pinned and not only its values.
    /// </summary>
    internal static string RawText { get; } = ReadResource();

    private static readonly JsonDocument Document = Validate(JsonDocument.Parse(RawText));

    /// <summary>The header the committed file carries, as it carries it.</summary>
    internal static IReadOnlyList<string> Comment { get; } = Document.RootElement
        .GetProperty("$comment").EnumerateArray().Select(line => line.GetString()!).ToArray();

    /// <summary>The permutation count the committed table was built over.</summary>
    internal static int Permutations { get; } = Table.GetProperty("permutations").GetInt32();

    /// <summary>How many permutations one committed chunk hash covers.</summary>
    internal static int ChunkSize { get; } = Table.GetProperty("chunkSize").GetInt32();

    /// <summary>The corpus seed the committed table names, as hexadecimal.</summary>
    internal static string BaselineSeed { get; } = Table.GetProperty("baselineSeed").GetString()!;

    /// <summary>The one hash over all 100 chunks.</summary>
    internal static string Aggregate { get; } = Table.GetProperty("aggregate").GetString()!;

    /// <summary>The 100 chunk hashes, in ordinal order.</summary>
    internal static IReadOnlyList<string> Chunks { get; } = Table
        .GetProperty("chunks").EnumerateArray().Select(chunk => chunk.GetString()!).ToArray();

    /// <summary>The individually pinned rows.</summary>
    internal static IReadOnlyList<BaselineRow> Named { get; } = ReadNamed();

    /// <summary>The review block's status. Anything but <c>reviewed</c> has already thrown.</summary>
    internal static string ReviewStatus { get; } = Review.GetProperty("status").GetString()!;

    /// <summary>The date a human last reviewed a regeneration of this table.</summary>
    internal static string ReviewedOn { get; } = Review.GetProperty("reviewedOn").GetString()!;

    /// <summary>Which task's review this is. Blank has already thrown.</summary>
    internal static string ReviewedBy { get; } = Review.GetProperty("reviewedBy").GetString()!;

    /// <summary>Why the table holds the hashes it holds — hand-written, never generated.</summary>
    internal static string ReviewWhy { get; } = Review.GetProperty("why").GetString()!;

    /// <summary>The named row with this id.</summary>
    internal static BaselineRow Row(string id) =>
        Named.Single(row => row.Id.Equals(id, StringComparison.Ordinal));

    /// <summary>Every named row's <c>why</c>, keyed by id — what a regeneration carries over.</summary>
    internal static IReadOnlyDictionary<string, string> Reasons { get; } =
        Named.ToDictionary(row => row.Id, row => row.Why, StringComparer.Ordinal);

    private static JsonElement Table => Document.RootElement.GetProperty("table");

    private static JsonElement Review => Document.RootElement.GetProperty("review");

    private static string ReadResource()
    {
        using var stream = typeof(DslDeterminismBaseline).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The determinism baseline '{ResourceName}' is not embedded in " +
                $"{typeof(DslDeterminismBaseline).Assembly.GetName().Name}. Available: " +
                string.Join(", ", typeof(DslDeterminismBaseline).Assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        return reader.ReadToEnd();
    }

    /// <summary>
    /// The three refusals, in one place a test can reach. Separated from the static initialiser
    /// deliberately: while these lived inline in the loader, the only thing that could exercise them
    /// was the committed file, which passes — so the tests over them were asserting conditions the
    /// loader had already guaranteed, and could not fail.
    /// </summary>
    /// <exception cref="FormatException">The table is unreviewed, or a reason is missing.</exception>
    internal static JsonDocument Validate(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var review = document.RootElement.GetProperty("review");

        var status = review.GetProperty("status").GetString();
        if (!string.Equals(status, DslDeterminismBaselineWriter.ReviewedStatus, StringComparison.Ordinal))
        {
            throw new FormatException(
                $"DslDeterminismBaseline.json is still '{status}'. The regeneration command writes the " +
                "SHAPE — 10 000 permutations, 100 chunk hashes and one aggregate — and stops there. " +
                "Which `18` §8 step moved, and whether it was meant to, is written by hand. A " +
                "generated reason is not a reason, and an unreviewed determinism break is a " +
                "determinism break that was accepted without anybody saying so. Read the diff, write " +
                "review.why, then set review.status to " +
                $"'{DslDeterminismBaselineWriter.ReviewedStatus}'.");
        }

        if (string.IsNullOrWhiteSpace(review.GetProperty("why").GetString()) ||
            string.IsNullOrWhiteSpace(review.GetProperty("reviewedOn").GetString()) ||
            string.IsNullOrWhiteSpace(review.GetProperty("reviewedBy").GetString()))
        {
            throw new FormatException(
                "DslDeterminismBaseline.json says it was reviewed and does not say who concluded what, " +
                "or when. review.why names the change that moved the hashes and the task that made it, " +
                "review.reviewedBy names the task and review.reviewedOn the date; without all three the " +
                "'reviewed' status is a checkbox rather than a record.");
        }

        var unexplained = RowsIn(document)
            .Where(row => string.IsNullOrWhiteSpace(row.Why))
            .Select(row => row.Id)
            .ToArray();

        if (unexplained.Length > 0)
        {
            throw new FormatException(
                $"These named rows of DslDeterminismBaseline.json carry no 'why': " +
                $"{string.Join(", ", unexplained)}. A named row exists to say which property of the " +
                "corpus it defends — 'the first permutation above the introsort threshold that also " +
                "holds duplicate ids', not 'permutation 7'. A row that cannot say what it pins pins " +
                "nothing a failure message can use. The regeneration command carries existing reasons " +
                "over and writes an empty one for a row it has never seen; fill it in by hand.");
        }

        return document;
    }

    private static IReadOnlyList<BaselineRow> ReadNamed() => RowsIn(Document);

    private static IReadOnlyList<BaselineRow> RowsIn(JsonDocument document) =>
        document.RootElement.GetProperty("table").GetProperty("named").EnumerateArray()
            .Select(row => new BaselineRow(
                row.GetProperty("id").GetString()!,
                row.GetProperty("permutation").GetInt32(),
                row.GetProperty("hash").GetString()!,
                row.GetProperty("why").GetString() ?? string.Empty))
            .ToArray();
}
