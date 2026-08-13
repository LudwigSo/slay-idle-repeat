using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// 🔒 The harness's own <c>game-data</c> → <see cref="ContentSnapshot"/> loader — the one place in
/// <c>tools/BalanceHarness</c> that knows JSON exists.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this exists at all, given that <c>SlayIdleRepeat.Application</c> already has a content
/// pipeline.</b> `30` §6 and `21` §2 pin this tool to <c>SlayIdleRepeat.Core</c> and nothing else —
/// no Application, no adapters, no ports, no packages — and
/// <c>ProjectFileTests.The_simulation_tools_reference_Core_only</c> fails the build on any other
/// reference. So the harness cannot reach <c>JsonContentReader</c>, and `05` §9's mass simulation
/// still has to read the authored numbers. <c>System.Text.Json</c> is in the shared framework, so
/// this adds no dependency.
/// </para>
/// <para>
/// ⚠️ <b>This is not a second content pipeline and must not become one.</b> It does no schema
/// validation, resolves no cross-document reference and reports no finding set — that is the
/// pipeline's job and the pipeline is what CI runs over the shipped tree. What this does is the
/// narrow thing the harness needs: bytes on disk to an immutable, version-stamped snapshot the
/// <c>Core</c> catalogues can read.
/// </para>
/// <para>
/// 🔒 <b>The two behaviours it shares with the pipeline, because they are the load path's whole
/// contract.</b> A JSON <c>null</c> becomes <see cref="ContentValue.Unauthorised"/> and never a
/// zero — <c>game-data/README.md</c>: <em>"a hole that is null is greppable, and a hole filled with
/// a plausible-looking number is invisible"</em>. And a duplicate key <b>throws</b>: the object
/// model keeps one of the two and the other simply disappears, which is `14` §6's duplicate-id
/// failure class arriving in silence.
/// </para>
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
    /// ⚠️ A method rather than a static initialiser, on
    /// <c>SlayIdleRepeat.Application.Tests</c>' <c>RepoData</c>'s precedent and for its reason: a run
    /// where the solution file is not an ancestor of the output directory — a published build, a
    /// container holding only <c>bin/</c> — would otherwise fail with a
    /// <c>TypeInitializationException</c> wrapping a <c>.sln</c> message instead of the one legible
    /// sentence below.
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
    /// 🔒 The tree with named documents replaced by supplied JSON text — an in-memory experiment,
    /// never an edit to <c>game-data/</c>.
    /// </summary>
    /// <remarks>
    /// `21` §3.3: an override never edits the canonical files. A harness run that answered "what if
    /// the adds fraction were 0.25 instead of 0.35?" by writing to <c>game-data/</c> would leave the
    /// repository holding a number nobody authored the moment it crashed, and the shipped answer
    /// would depend on whether the last run cleaned up after itself.
    /// </remarks>
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
    /// 🔒 The deterministic stamp of the loaded documents — a real SHA-256 over ordinal-sorted
    /// <c>path + text</c>, never a constant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fixed stamp would make two different data trees indistinguishable, and
    /// <see cref="ContentVersion"/>'s whole reason for existing is that <em>"a replayed command
    /// reproduces its original outcome after a balance patch"</em>. An experiment run through
    /// <see cref="LoadWith"/> must therefore stamp differently from the shipped tree, or the two
    /// runs are one run as far as anything downstream can tell.
    /// </para>
    /// <para>
    /// 🔴 <b>It is NOT the game's <see cref="ContentVersion"/> for the same tree, and the report line
    /// that prints it must not be matched against a server's.</b> The shipped stamp is
    /// <c>ContentHashing.Compute</c> in <c>SlayIdleRepeat.Application</c>, which hashes canonical
    /// bytes behind a canonical-format-version prefix; this one hashes <c>path + "\n" + text + "\n"</c>
    /// over the raw files. Same tree, two different hexes — deliberately, because reproducing the
    /// canonical form here would mean a third copy of the canonicaliser in a tool that
    /// <c>ProjectFileTests.The_simulation_tools_reference_Core_only</c> forbids from referencing
    /// <c>Application</c>. What this stamp is for is <em>distinguishing harness runs from each other</em>
    /// (shipped tree vs. <see cref="LoadWith"/> experiment), which it does exactly; what it is not for
    /// is identifying a content build.
    /// </para>
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

            // 🔒 The whole point. `game-data/README.md`: null means "the design docs do not
            // authorise a value here". It is never zero and never a default.
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
            // 🔒 JsonDocument keeps a duplicate silently and one of the two values simply vanishes,
            // which is 14 §6's duplicate-id failure class arriving with nothing to see. Refuse it
            // here rather than let ContentValue.Object refuse it without naming the document.
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
