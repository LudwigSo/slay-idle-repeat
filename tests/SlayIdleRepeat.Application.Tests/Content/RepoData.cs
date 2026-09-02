using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Loads the real <c>game-data</c> tree off disk and hands it to the in-memory fake.
/// </summary>
/// <remarks>
/// <para>
/// This suite still runs against <c>SlayIdleRepeat.Adapters.InMemory</c> exclusively (`23` §3) —
/// the bytes just came from the repository rather than from a string literal. There is no adapter
/// reference, no filesystem port, no container, no network: `System.IO` reads the checkout the
/// test is running from, exactly as <c>SlayIdleRepeat.Architecture.Tests</c> already does for
/// <c>.csproj</c> files.
/// </para>
/// <para>
/// It earns its place because the miniature fixture cannot: a validator that has only ever seen a
/// 40-line stand-in has not been shown to survive 16 real tuning files, 19 real schemas and 96
/// deliberate <c>null</c>s.
/// </para>
/// </remarks>
internal static class RepoData
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    // Lazy, not static initialisers: a run where SlayIdleRepeat.sln is not an ancestor of the test
    // output (a published build, a container holding only bin/) would otherwise fail every test in
    // three classes with a TypeInitializationException wrapping a .sln message, instead of one
    // legible assertion saying this suite needs a checkout.
    private static readonly Lazy<string> LazyRepositoryRoot = new(FindRepositoryRoot);
    private static readonly Lazy<IReadOnlyDictionary<string, string>> LazyDocuments = new(ReadDocuments);

    /// <summary>The directory holding <c>SlayIdleRepeat.sln</c>.</summary>
    internal static string RepositoryRoot => LazyRepositoryRoot.Value;

    /// <summary>The <c>game-data</c> root.</summary>
    internal static string DataRoot => Path.Combine(RepositoryRoot, "game-data");

    /// <summary>Every real data document, keyed by its snapshot-relative path.</summary>
    internal static IReadOnlyDictionary<string, string> Documents => LazyDocuments.Value;

    /// <summary>A source over the real data set.</summary>
    internal static InMemoryContentSource Source()
    {
        Directory.Exists(DataRoot).ShouldBeTrue(
            $"these cases read the real game-data; searched upward from {AppContext.BaseDirectory}");

        var source = new InMemoryContentSource();
        foreach (var (path, text) in Documents)
        {
            source.Set(path, text);
        }

        return source;
    }

    /// <summary>The real data set with one document's text edited — a single-edit mutation.</summary>
    /// <param name="documentPath">The shipped document to corrupt.</param>
    /// <param name="find">The anchor. 🔒 Must occur exactly once unless <paramref name="occurrences"/> says otherwise.</param>
    /// <param name="replaceWith">What to put there instead.</param>
    /// <param name="occurrences">
    /// How many times the anchor is expected to occur. Defaults to one, because
    /// <c>string.Replace</c> replaces them all: <c>"cap": 1,</c> occurs ten times in
    /// <c>ads.json</c>, so a case that reads as one edit was corrupting ten placements and could
    /// have been passing on any of them. Raise it deliberately, per case, or narrow the anchor.
    /// </param>
    internal static InMemoryContentSource SourceWithEdit(
        string documentPath, string find, string replaceWith, int occurrences = 1)
    {
        var original = Documents[documentPath];
        var found = Occurrences(original, find);

        if (found == 0)
        {
            throw new InvalidOperationException(
                $"'{find}' does not occur in {documentPath}, so this negative case would silently " +
                "test nothing. The data moved — fix the mutation, do not delete the case.");
        }

        if (found != occurrences)
        {
            throw new InvalidOperationException(
                $"'{find}' occurs {found} time(s) in {documentPath}, not the {occurrences} this case " +
                "declares. string.Replace hits every one of them, so the mutation is not the single " +
                "edit it reads as — and the rule that fires may not be the rule the case names. " +
                "Narrow the anchor, or pass the count deliberately.");
        }

        return Source().Set(documentPath, original.Replace(find, replaceWith, StringComparison.Ordinal));
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var at = text.IndexOf(value, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static IReadOnlyDictionary<string, string> ReadDocuments()
    {
        var documents = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(DataRoot, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(DataRoot, file).Replace('\\', '/');
            documents[relative] = File.ReadAllText(file);
        }

        return documents;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} in any ancestor of {AppContext.BaseDirectory}.");
    }
}
