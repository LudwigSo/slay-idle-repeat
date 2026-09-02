namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// Locates the repository on disk and enumerates the production project files, which is
/// how <see cref="ProductionAssemblies.AllNames"/> learns what the dependency rules govern
/// without a hand-maintained list.
/// </summary>
internal static class RepoLayout
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    /// <summary>Absolute path of the directory holding <c>SlayIdleRepeat.sln</c>.</summary>
    internal static string RepoRoot { get; } = FindRepoRoot();

    /// <summary><c>src/</c> — the production tree.</summary>
    internal static string SrcRoot { get; } = Path.Combine(RepoRoot, "src");

    /// <summary>Every production <c>.csproj</c> under <c>src/</c>, ordered.</summary>
    internal static IReadOnlyList<string> ProductionProjectFiles { get; } =
        (Directory.Exists(SrcRoot)
            ? Directory.GetFiles(SrcRoot, "*.csproj", SearchOption.AllDirectories)
            : Array.Empty<string>())
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>Assembly/project name of a <c>.csproj</c> path.</summary>
    internal static string ProjectName(string projectFile) => Path.GetFileNameWithoutExtension(projectFile);

    private static string FindRepoRoot()
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
