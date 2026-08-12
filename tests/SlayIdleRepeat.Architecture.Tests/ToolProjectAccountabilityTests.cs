using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `23` §6 — the dependency rules over <c>tools/</c> are stated per project, by name, and this
/// is what stops a new project appearing beside them with no rule pointing at it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ProjectFileTests"/> governs <c>tools/</c> by three named sets:
/// <c>CoreOnlyToolNames</c> pins <c>EconomySim</c> and <c>BalanceHarness</c> to <c>Core</c>
/// (`30` §6, `21` §2), and <c>ToolCompositionRootNames</c> exempts <c>ContentValidator</c> from
/// the adapter-isolation rule because it genuinely is a composition root (`23` §7). A project
/// in neither set is covered only by <c>Only_composition_roots_reference_adapter_projects</c> —
/// which fires on an <c>Adapters.*</c> reference and on nothing else. It could
/// project-reference <c>SlayIdleRepeat.Application</c>, or take a vendor package no other
/// project has, and the whole suite would stay green.
/// </para>
/// <para>
/// 🔒 That was live the moment <c>tools/AssetManifest</c> landed. Its own <c>.csproj</c>
/// declares 🔒 "a LIBRARY, and a dependency-free one … it project-references nothing … it
/// package-references nothing either" — a stated invariant that nothing enforced. A comment is
/// not a rule.
/// </para>
/// </remarks>
public sealed class ToolProjectAccountabilityTests
{
    /// <summary>
    /// Tools that reach nothing at all: no <c>ProjectReference</c>, no <c>PackageReference</c>.
    /// </summary>
    /// <remarks>
    /// <c>SlayIdleRepeat.AssetManifest</c> (M8-09) is a typed reader over
    /// <c>game-data/assets/*.json</c> plus the reconciliation rules that hold between the
    /// manifest and the counts `15` §E1 and `20` §4 claim. It holds no game rule, ships to
    /// nobody, and needs nothing in-box <c>net8.0</c> does not already give it. Listing it here
    /// is what turns that from a claim in a comment into a rule.
    /// </remarks>
    private static readonly string[] DependencyFreeToolNames = ["SlayIdleRepeat.AssetManifest"];

    /// <summary>
    /// `23` §6 / `30` §6 — every project under <c>tools/</c> is named by one of the sets the
    /// dependency rules are stated over. A tool in none of them has no reference rule at all.
    /// </summary>
    /// <remarks>
    /// Stated as "accounted for" rather than as a fixed list so that adding a tool is a
    /// one-line decision about which rule governs it — and so that adding one and deciding
    /// nothing is a build failure rather than a silent gap.
    /// </remarks>
    [Fact]
    public void Every_tools_project_is_governed_by_a_named_reference_rule()
    {
        var governed = ProductionAssemblies.CoreOnlyToolNames
            .Concat(ProductionAssemblies.ToolCompositionRootNames)
            .Concat(DependencyFreeToolNames)
            .ToArray();

        var toolProjects = ToolProjectFiles();
        var offenders = new List<string>();

        if (toolProjects.Count == 0)
        {
            offenders.Add(
                $"no .csproj found under {RepoLayout.Relative(RepoLayout.ToolsRoot)} — this rule and the four " +
                "in ProjectFileTests that quantify over the same list would all pass over nothing.");
        }

        offenders.AddRange(
            toolProjects
                .Select(RepoLayout.ProjectName)
                .Where(name => !governed.Contains(name, StringComparer.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name =>
                    $"'{name}' is a project under tools/ that no reference rule names. Add it to " +
                    "ProductionAssemblies.CoreOnlyToolNames (pinned to Core), " +
                    "ProductionAssemblies.ToolCompositionRootNames (composes an adapter itself, 23 §7), or " +
                    "ToolProjectAccountabilityTests.DependencyFreeToolNames (reaches nothing) — whichever is " +
                    "true of it. Until then it may reference Application, or take a vendor package, with a " +
                    "green suite."));

        ArchRule.Empty(
            offenders,
            "Every project under tools/ is governed by a named reference rule (23 §6, 30 §6).");
    }

    /// <summary>
    /// `23` §2.1 / `23` §5 A9 — a dependency-free tool references nothing: no project, no
    /// package. The project-file counterpart of <c>Core_project_references_nothing</c>, for the
    /// tools that make the same claim about themselves.
    /// </summary>
    /// <remarks>
    /// A <c>PackageReference</c> here would also be the first row in
    /// <c>Directory.Packages.props</c> and <c>build/ci/non-vendor-packages.json</c> that nobody
    /// added deliberately — <c>Vendor_package_is_referenced_by_exactly_one_project</c> only
    /// fires on the second project to take a package, so the first one is free.
    /// </remarks>
    [Fact]
    public void The_dependency_free_tools_reference_nothing_at_all()
    {
        var offenders = new List<string>();

        foreach (var tool in DependencyFreeToolNames)
        {
            var projectFile = ToolProjectFiles().SingleOrDefault(
                p => RepoLayout.ProjectName(p).Equals(tool, StringComparison.Ordinal));

            if (projectFile is null)
            {
                offenders.Add(
                    $"'{tool}' has no .csproj under tools/. A rule keyed on a project that is not there " +
                    "governs nothing — remove the entry, or point it at where the project went.");
                continue;
            }

            offenders.AddRange(
                RepoLayout.ProjectReferences(projectFile)
                    .Select(r => $"{tool} project-references '{r}' — it is declared dependency-free"));

            offenders.AddRange(
                RepoLayout.PackageReferences(projectFile)
                    .Select(p => $"{tool} references the package '{p}' — it is declared dependency-free"));
        }

        ArchRule.Empty(
            offenders,
            "The dependency-free tools reference no project and no package (23 §2.1, §5 A9).");
    }

    /// <summary>Every production <c>.csproj</c> that lives under <c>tools/</c>.</summary>
    private static IReadOnlyList<string> ToolProjectFiles() =>
        RepoLayout.ProductionProjectFiles
            .Where(p => p.StartsWith(RepoLayout.ToolsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
