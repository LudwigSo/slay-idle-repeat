using SlayIdleRepeat.Adapters.InMemory;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Loads the real <c>SlayIdleRepeat.Data</c> tree off disk and hands it to the in-memory fake.
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
/// 40-line stand-in has not been shown to survive 16 real tuning files, 19 real schemas and 98
/// deliberate <c>null</c>s.
/// </para>
/// </remarks>
internal static class RepoData
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    /// <summary>The directory holding <c>SlayIdleRepeat.sln</c>.</summary>
    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The <c>SlayIdleRepeat.Data</c> root.</summary>
    internal static string DataRoot { get; } = Path.Combine(RepositoryRoot, "SlayIdleRepeat.Data");

    /// <summary>The <c>game-design</c> documentation root.</summary>
    internal static string DesignDocsRoot { get; } = Path.Combine(RepositoryRoot, "game-design");

    /// <summary>Every real data document, keyed by its snapshot-relative path.</summary>
    internal static IReadOnlyDictionary<string, string> Documents { get; } = ReadDocuments();

    /// <summary>A source over the real data set.</summary>
    internal static InMemoryContentSource Source()
    {
        var source = new InMemoryContentSource();
        foreach (var (path, text) in Documents)
        {
            source.Set(path, text);
        }

        return source;
    }

    /// <summary>The real data set with one document's text edited — a single-edit mutation.</summary>
    internal static InMemoryContentSource SourceWithEdit(string documentPath, string find, string replaceWith)
    {
        var original = Documents[documentPath];
        if (!original.Contains(find, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{find}' does not occur in {documentPath}, so this negative case would silently " +
                "test nothing. The data moved — fix the mutation, do not delete the case.");
        }

        return Source().Set(documentPath, original.Replace(find, replaceWith, StringComparison.Ordinal));
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
