using System.Xml.Linq;

namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// Locates the repository on disk and parses the project files.
/// Several rules in <c>23</c> §6 are stated over <c>.csproj</c> content rather than
/// over IL (vendor package uniqueness, composition-root isolation), so the suite
/// needs the source tree, not just the build output.
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
        Directory.GetFiles(SrcRoot, "*.csproj", SearchOption.AllDirectories)
                 .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                 .ToArray();

    /// <summary>Assembly/project name of a <c>.csproj</c> path.</summary>
    internal static string ProjectName(string projectFile) => Path.GetFileNameWithoutExtension(projectFile);

    /// <summary>Path of a production project directory, by project name.</summary>
    internal static string ProjectDirectory(string projectName)
    {
        var file = ProductionProjectFiles.FirstOrDefault(
            p => string.Equals(ProjectName(p), projectName, StringComparison.OrdinalIgnoreCase));
        return file is null
            ? throw new InvalidOperationException($"No production project named '{projectName}' under {SrcRoot}.")
            : Path.GetDirectoryName(file)!;
    }

    /// <summary>The <c>Include</c> values of every <c>PackageReference</c> in a project file.</summary>
    internal static IReadOnlyList<string> PackageReferences(string projectFile) =>
        ItemIncludes(projectFile, "PackageReference");

    /// <summary>The referenced project <em>names</em> of every <c>ProjectReference</c> in a project file.</summary>
    internal static IReadOnlyList<string> ProjectReferences(string projectFile) =>
        ItemIncludes(projectFile, "ProjectReference")
            .Select(i => Path.GetFileNameWithoutExtension(i.Replace('\\', Path.DirectorySeparatorChar)))
            .ToArray();

    /// <summary>Hand-written <c>.cs</c> files under a directory, excluding <c>bin/</c> and <c>obj/</c>.</summary>
    internal static IReadOnlyList<string> SourceFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                        .Where(f => !IsGenerated(f))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
    }

    /// <summary>A repo-relative path, for readable failure messages.</summary>
    internal static string Relative(string absolutePath) =>
        Path.GetRelativePath(RepoRoot, absolutePath).Replace('\\', '/');

    private static bool IsGenerated(string file)
    {
        var relative = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
        return relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ItemIncludes(string projectFile, string itemName)
    {
        var document = XDocument.Load(projectFile);
        return document.Descendants()
                       .Where(e => e.Name.LocalName == itemName)
                       .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
                       .Where(i => i.Length > 0)
                       .ToArray();
    }

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
