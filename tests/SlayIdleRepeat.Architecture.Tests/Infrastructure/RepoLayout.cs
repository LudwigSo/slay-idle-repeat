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

    /// <summary><c>tools/</c> — <c>EconomySim</c>, <c>BalanceHarness</c>, <c>ContentValidator</c>.</summary>
    internal static string ToolsRoot { get; } = Path.Combine(RepoRoot, "tools");

    /// <summary>
    /// 🔒 <c>IMPLEMENTATION_TRACKER.md</c>'s raw text — the one read for the whole assembly
    /// (steering S4).
    /// </summary>
    /// <remarks>
    /// Four registers in this suite ask the tracker whether an owner is still open, and M5 added
    /// three of them. Each arrived with its own private <c>Tracker()</c> reading the same file from
    /// the same root: harmless while they agree, and four places to change the day the document
    /// moves or the read needs an encoding. The owner-status <em>predicate</em> was already shared
    /// (<c>PortCatalogue.OwnersNoLongerOpen</c>); this is the input to it, shared for the same
    /// reason.
    /// </remarks>
    internal static string TrackerText() =>
        File.ReadAllText(Path.Combine(RepoRoot, "IMPLEMENTATION_TRACKER.md"));

    /// <summary>
    /// The roots holding hand-written, non-test C# that the dependency rules govern.
    /// </summary>
    /// <remarks>
    /// <c>tools/</c> is in here, and it was not before. Every <c>.csproj</c> rule in
    /// <see cref="ProjectFileTests"/> reads <see cref="ProductionProjectFiles"/>, so while
    /// that list was <c>src/</c>-only, four projects — <c>EconomySim</c>,
    /// <c>BalanceHarness</c>, <c>ContentValidator</c> and anything added beside them — had
    /// no dependency-rule coverage at all. <c>tools/ContentValidator</c> already
    /// project-references an adapter from outside a composition root, and <c>30</c> §6 pins
    /// <c>EconomySim</c> and <c>BalanceHarness</c> to <c>Core</c> only: correct today,
    /// enforced by nothing. A future agent could point <c>EconomySim</c> at
    /// <c>Application</c> and break <c>21</c> §2 / <c>30</c> §13 with a green suite.
    /// </remarks>
    internal static IReadOnlyList<string> ProductionSourceRoots { get; } = new[] { SrcRoot, ToolsRoot };

    /// <summary>Every production <c>.csproj</c> under <c>src/</c> and <c>tools/</c>, ordered.</summary>
    internal static IReadOnlyList<string> ProductionProjectFiles { get; } =
        ProductionSourceRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
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
    /// <remarks>
    /// 🔒 Throws on a missing directory rather than returning an empty list. Every caller
    /// passes a path it believes exists, and every caller is a rule that greps the returned
    /// files: silently returning nothing turns "this directory moved" into "this rule holds
    /// over zero files, forever". That is precisely how a renamed test suite would stop the
    /// placeholder meta-rule from ever seeing a <c>#if false</c> again.
    /// </remarks>
    internal static IReadOnlyList<string> SourceFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"'{Relative(directory)}' does not exist, so a rule that greps it would pass over zero " +
                "files instead of failing. Whatever moved or was renamed, point the rule at the new " +
                "location — do not let it grep nothing.");
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
