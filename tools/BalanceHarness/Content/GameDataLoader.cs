using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// The harness's own <c>game-data</c> → <see cref="ContentSnapshot"/> loader — the one place in
/// <c>tools/BalanceHarness</c> that knows JSON exists.
/// </summary>
/// <remarks>
/// This tool is pinned to reference <c>SlayIdleRepeat.Core</c> only (enforced by
/// <c>ProjectFileTests.The_simulation_tools_reference_Core_only</c>), so it cannot reach
/// <c>JsonContentReader</c> and has to parse its own. It is deliberately not a second content
/// pipeline: no schema validation, no cross-document resolution, no finding set — just bytes on disk
/// to an immutable, version-stamped snapshot. Like the real pipeline, a JSON <c>null</c> becomes
/// <see cref="ContentValue.Unauthorised"/> (never a silent zero) and a duplicate key throws rather
/// than silently discarding one of the two values.
/// </remarks>
public static class GameDataLoader
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";
    private const string DataDirectoryName = "game-data";

    /// <summary>The <c>game-data</c> root of the checkout this process is running from.</summary>
    public static string DataRoot => Path.Combine(FindRepositoryRoot(), DataDirectoryName);

    /// <summary>
    /// The directory holding <c>SlayIdleRepeat.sln</c>, found by walking up from the running
    /// assembly's directory.
    /// </summary>
    /// <remarks>
    /// A method rather than a static initialiser: a run where the solution file is not an ancestor of
    /// the output directory (a published build, a container holding only <c>bin/</c>) would otherwise
    /// fail with a <c>TypeInitializationException</c> wrapping the message, instead of the message.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No ancestor holds the solution file.</exception>
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} in any ancestor of {AppContext.BaseDirectory}. " +
                "The balance harness reads the authored game-data out of the checkout it is running " +
                "from; without one there is nothing to simulate.");
    }

    /// <summary>Loads the <c>game-data</c> tree of the checkout this process is running from.</summary>
    public static ContentSnapshot Load() => Load(DataRoot);

    /// <summary>Loads every <c>*.json</c> under <paramref name="dataRoot"/>, recursively.</summary>
    /// <param name="dataRoot">The directory the document paths are relative to.</param>
    /// <exception cref="DirectoryNotFoundException">The directory is not there.</exception>
    public static ContentSnapshot Load(string dataRoot) => Build(ReadDocuments(dataRoot));

    /// <summary>
    /// The tree with named documents replaced by supplied JSON text — an in-memory experiment, never
    /// an edit to <c>game-data/</c> (a crash mid-write must not leave the repo holding an unauthored
    /// number).
    /// </summary>
    /// <param name="dataRoot">The directory the document paths are relative to.</param>
    /// <param name="replacements">Document path (e.g. <c>content/bosses/bosses.json</c>) → JSON text.</param>
    /// <exception cref="ArgumentException">A named path is not in the tree.</exception>
    public static ContentSnapshot LoadWith(
        string dataRoot, IReadOnlyDictionary<string, string> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);

        var documents = ReadDocuments(dataRoot);

        foreach (var (path, text) in replacements)
        {
            if (!documents.ContainsKey(path))
            {
                throw new ArgumentException(
                    $"'{path}' is not a document under '{dataRoot}', so this override would replace " +
                    "nothing and the run would silently be the unmodified one. The tree holds " +
                    $"{documents.Count.ToString(CultureInfo.InvariantCulture)} " +
                    "document(s).",
                    nameof(replacements));
            }

            documents[path] = text;
        }

        return Build(documents);
    }

    private static SortedDictionary<string, string> ReadDocuments(string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);

        if (!Directory.Exists(dataRoot))
        {
            throw new DirectoryNotFoundException(
                $"'{dataRoot}' does not exist. The balance harness reads the authored content out of " +
                "the checkout it is running from.");
        }

        var documents = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(dataRoot, "*.json", SearchOption.AllDirectories))
        {
            documents[Path.GetRelativePath(dataRoot, file).Replace('\\', '/')] = File.ReadAllText(file);
        }

        return documents;
    }

    private static ContentSnapshot Build(SortedDictionary<string, string> documents)
    {
        var parsed = new List<ContentDocument>(documents.Count);

        foreach (var (path, text) in documents)
        {
            using var json = JsonDocument.Parse(text);

            parsed.Add(new ContentDocument(path, Map(json.RootElement, path, string.Empty)));
        }

        return new ContentSnapshot(Stamp(documents), parsed);
    }

    /// <summary>
    /// The deterministic stamp of the loaded documents — a real SHA-256 over ordinal-sorted
    /// <c>path + text</c>, never a constant, so an <see cref="LoadWith"/> experiment stamps
    /// differently from the shipped tree.
    /// </summary>
    /// <remarks>
    /// This is NOT the game's <see cref="ContentVersion"/> for the same tree and must not be matched
    /// against a server's: the shipped stamp hashes canonical bytes behind a format-version prefix
    /// (<c>ContentHashing.Compute</c>, unreachable here — see the type remarks), while this hashes the
    /// raw <c>path + text</c>. Same tree, deliberately different hex; this stamp only distinguishes
    /// harness runs from each other, not content builds.
    /// </remarks>
    private static ContentVersion Stamp(SortedDictionary<string, string> documents)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var (path, text) in documents)
        {
            sha.AppendData(Encoding.UTF8.GetBytes($"{path}\n{text}\n"));
        }

        return ContentVersion.FromHex(Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    private static ContentValue Map(JsonElement element, string documentPath, string pointer) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => MapObject(element, documentPath, pointer),
            JsonValueKind.Array => MapArray(element, documentPath, pointer),
            JsonValueKind.String => ContentValue.Text(element.GetString()!),
            JsonValueKind.Number => MapNumber(element, documentPath, pointer),
            JsonValueKind.True => ContentValue.True,
            JsonValueKind.False => ContentValue.False,

            // null means "not authorised" — never zero, never a default.
            JsonValueKind.Null => ContentValue.Unauthorised,

            _ => throw new InvalidOperationException(
                $"{Locate(documentPath, pointer)} is a {element.ValueKind} JSON value, which is not " +
                "one of the six kinds content is made of."),
        };

    private static ContentValue MapObject(JsonElement element, string documentPath, string pointer)
    {
        var members = new List<KeyValuePair<string, ContentValue>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in element.EnumerateObject())
        {
            // JsonDocument keeps a duplicate silently and one value simply vanishes; refuse it here,
            // where the document and pointer are still known, rather than downstream.
            if (!seen.Add(member.Name))
            {
                throw new InvalidOperationException(
                    $"{Locate(documentPath, pointer)} declares the key '{member.Name}' more than " +
                    "once. JSON object models keep one of the two and discard the other in silence, " +
                    "so whichever value is wrong would never be read and never be reported.");
            }

            members.Add(new KeyValuePair<string, ContentValue>(
                member.Name, Map(member.Value, documentPath, $"{pointer}/{member.Name}")));
        }

        return ContentValue.Object(members);
    }

    private static ContentValue MapArray(JsonElement element, string documentPath, string pointer)
    {
        var items = new List<ContentValue>();

        foreach (var item in element.EnumerateArray())
        {
            items.Add(Map(
                item, documentPath, $"{pointer}/{items.Count.ToString(CultureInfo.InvariantCulture)}"));
        }

        return ContentValue.Array(items);
    }

    private static ContentValue MapNumber(JsonElement element, string documentPath, string pointer) =>
        element.TryGetDecimal(out var number)
            ? ContentValue.Number(number)
            : throw new InvalidOperationException(
                $"{Locate(documentPath, pointer)} holds a number that does not fit an exact decimal. " +
                "Content numbers are held exactly so the load path never rounds; a value needing " +
                "binary floating point does not belong in the data.");

    private static string Locate(string documentPath, string pointer) =>
        pointer.Length == 0 ? documentPath : $"{documentPath}#{pointer}";
}
