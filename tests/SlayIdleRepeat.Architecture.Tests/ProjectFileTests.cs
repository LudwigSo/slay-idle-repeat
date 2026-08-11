using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `23` §6 "Additional CI checks" and `23` §5 — the rules stated over `.csproj` content.
/// They live here rather than in a shell step because a parsed project file is cheaper
/// and more reliable than a grep, and it fails the build with the same message locally.
/// </summary>
public sealed class ProjectFileTests
{
    /// <summary>
    /// `23` §5 A9 / §6 — vendor package uniqueness: parse every production `.csproj` and
    /// fail if a vendor `PackageReference` appears in more than one project. One adapter
    /// project per external dependency (A1); two projects referencing the same SDK means
    /// one of them is wrong.
    /// </summary>
    /// <remarks>
    /// Scoped to <c>src/</c> and <c>tools/</c>. Test projects legitimately share xUnit,
    /// FluentAssertions and the test SDK — `23` §3 has `Contract.Tests` running one suite
    /// against every adapter, so a solution-wide uniqueness rule would contradict the
    /// document it enforces. <c>tools/</c> is in scope because nothing about "one adapter per
    /// vendor SDK" stops applying to a project that happens not to ship.
    /// </remarks>
    [Fact]
    public void Vendor_package_is_referenced_by_exactly_one_project()
    {
        var offenders =
            from projectFile in RepoLayout.ProductionProjectFiles
            from package in RepoLayout.PackageReferences(projectFile)
            group RepoLayout.ProjectName(projectFile) by package into byPackage
            where byPackage.Count() > 1
            select $"'{byPackage.Key}' is referenced by {byPackage.Count()} projects: " +
                   string.Join(", ", byPackage.OrderBy(p => p, StringComparer.Ordinal));

        ArchRule.Empty(
            offenders,
            "Vendor packages are referenced by exactly one project (23 §5 A9, §6).");
    }

    /// <summary>
    /// `23` §6 — composition-root isolation: only `SlayIdleRepeat.Server` and
    /// `SlayIdleRepeat.Client` may reference `SlayIdleRepeat.Adapters.*`.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not scoped to test projects: `Contract.Tests` runs the shared port suite against every
    /// adapter and `Application.Tests` uses the in-memory fakes (`23` §3), both by design.
    /// </para>
    /// <para>
    /// 🔒 <c>tools/</c> IS in scope, and <c>tools/ContentValidator</c> is exempted BY NAME
    /// rather than by the rule not looking. It composes <c>Adapters.Content.LocalFile</c>
    /// onto the content services in <c>Application</c> so that CI validates content through
    /// the same loader the game uses — which makes it a composition root in the sense `23` §7
    /// means, even though it is neither `Server` nor `Client`. While this rule was scoped to
    /// <c>src/</c> it passed because it never read the file; that is not the same as passing.
    /// </para>
    /// </remarks>
    [Fact]
    public void Only_composition_roots_reference_adapter_projects()
    {
        var allowed = ProductionAssemblies.CompositionRootNames
            .Concat(ProductionAssemblies.ToolCompositionRootNames)
            .ToArray();

        var offenders =
            from projectFile in RepoLayout.ProductionProjectFiles
            let project = RepoLayout.ProjectName(projectFile)
            where !allowed.Contains(project, StringComparer.Ordinal)
            from referenced in RepoLayout.ProjectReferences(projectFile)
            where ProductionAssemblies.IsAdapter(referenced)
            select $"{project} references the adapter {referenced} — only {string.Join(", ", allowed)} may (23 §6, §7)";

        ArchRule.Empty(
            offenders,
            "Only the composition roots reference SlayIdleRepeat.Adapters.* (23 §6, §7).");
    }

    /// <summary>
    /// `30` §6 / `21` §2 — 🔒 the economy simulator and the balance harness reference
    /// `SlayIdleRepeat.Core` and nothing else: 180 days × 14 profiles run headless, with no
    /// Application, no ports and no adapters at all.
    /// </summary>
    /// <remarks>
    /// This is the project-file counterpart of
    /// <c>The_whole_game_is_playable_from_Core_alone</c>, and it is the reason that rule is
    /// worth having: if the simulator needed a port to run, "the whole game is playable from
    /// Core alone" would be false in the one place it is supposed to be demonstrated. Until
    /// <c>tools/</c> came into scope nothing enforced it, and a future agent could point
    /// <c>EconomySim</c> at <c>Application</c> and break `21` §2 and `30` §13 with a green
    /// suite.
    /// </remarks>
    [Fact]
    public void The_simulation_tools_reference_Core_only()
    {
        var offenders = new List<string>();

        foreach (var tool in ProductionAssemblies.CoreOnlyToolNames)
        {
            var projectFile = RepoLayout.ProductionProjectFiles.SingleOrDefault(
                p => RepoLayout.ProjectName(p).Equals(tool, StringComparison.Ordinal));

            if (projectFile is null)
            {
                offenders.Add(
                    $"'{tool}' has no .csproj under src/ or tools/. 30 §6 pins it to Core; a rule keyed on a " +
                    "project that is not there governs nothing.");
                continue;
            }

            offenders.AddRange(
                RepoLayout.ProjectReferences(projectFile)
                    .Where(r => !r.Equals(ProductionAssemblies.CoreName, StringComparison.Ordinal))
                    .Select(r => $"{tool} project-references '{r}' — 30 §6 pins it to {ProductionAssemblies.CoreName} only"));

            offenders.AddRange(
                RepoLayout.PackageReferences(projectFile)
                    .Select(p => $"{tool} references the package '{p}' — 21 §2 runs it headless with no dependencies"));
        }

        ArchRule.Empty(
            offenders,
            "EconomySim and BalanceHarness reference SlayIdleRepeat.Core and nothing else (30 §6, 21 §2).");
    }

    /// <summary>`23` §5 A7 — adapters never project-reference each other; composition happens only at the root.</summary>
    [Fact]
    public void Adapters_never_project_reference_each_other()
    {
        var offenders =
            from projectFile in RepoLayout.ProductionProjectFiles
            let project = RepoLayout.ProjectName(projectFile)
            where ProductionAssemblies.IsAdapter(project)
            from referenced in RepoLayout.ProjectReferences(projectFile)
            where ProductionAssemblies.IsAdapter(referenced)
            select $"{project} project-references {referenced}";

        ArchRule.Empty(offenders, "Adapters never reference other adapters (23 §5 A7).");
    }

    /// <summary>
    /// `23` §2.1 — `Core` references nothing: no `ProjectReference`, no `PackageReference`,
    /// not even a logging abstraction. This is the project-file counterpart of
    /// <c>The_whole_game_is_playable_from_Core_alone</c>, and it fails one step earlier.
    /// </summary>
    [Fact]
    public void Core_project_references_nothing()
    {
        var projectFile = RepoLayout.ProductionProjectFiles.Single(
            p => RepoLayout.ProjectName(p).Equals(ProductionAssemblies.CoreName, StringComparison.Ordinal));

        var offenders = RepoLayout.PackageReferences(projectFile).Select(p => $"PackageReference '{p}'")
            .Concat(RepoLayout.ProjectReferences(projectFile).Select(p => $"ProjectReference '{p}'"));

        ArchRule.Empty(offenders, "SlayIdleRepeat.Core references nothing at all (23 §2.1, §6).");
    }

    /// <summary>
    /// `23` §2.1 — `Application` references `Core` and `Contracts`, and nothing else. It
    /// defines every port and wraps no vendor, so a package reference here is a leak.
    /// </summary>
    [Fact]
    public void Application_references_only_Core_and_Contracts()
    {
        var projectFile = RepoLayout.ProductionProjectFiles.Single(
            p => RepoLayout.ProjectName(p).Equals(ProductionAssemblies.ApplicationName, StringComparison.Ordinal));

        var allowed = new[] { ProductionAssemblies.CoreName, ProductionAssemblies.ContractsName };

        var offenders = RepoLayout.ProjectReferences(projectFile)
            .Where(r => !allowed.Contains(r, StringComparer.Ordinal))
            .Select(r => $"SlayIdleRepeat.Application references the project '{r}'")
            .Concat(RepoLayout.PackageReferences(projectFile)
                .Select(p => $"SlayIdleRepeat.Application references the package '{p}'"));

        ArchRule.Empty(offenders, "Application references Core and Contracts only (23 §2.1).");
    }

    /// <summary>
    /// `23` §2.1 — an adapter references `Application` (to implement its ports) and its own
    /// vendor package, nothing else. It never reaches past `Application` into `Core` — an
    /// adapter that needs a domain rule is an adapter with business logic in it (`23` §5 A6)
    /// — and it never reaches sideways into another adapter or up into a composition root.
    /// </summary>
    /// <remarks>
    /// `Contracts` is permitted alongside `Application`: it is wire envelopes only
    /// (`30` §11.6), `Application` itself references it, and a transport adapter is exactly
    /// the project that serialises them.
    /// </remarks>
    [Fact]
    public void Adapters_project_reference_Application_only()
    {
        var allowed = new[] { ProductionAssemblies.ApplicationName, ProductionAssemblies.ContractsName };

        var offenders =
            from projectFile in RepoLayout.ProductionProjectFiles
            let project = RepoLayout.ProjectName(projectFile)
            where ProductionAssemblies.IsAdapter(project)
            from referenced in RepoLayout.ProjectReferences(projectFile)
            where !allowed.Contains(referenced, StringComparer.Ordinal)
            select $"{project} project-references {referenced} — an adapter references Application (and Contracts) only";

        ArchRule.Empty(offenders, "Adapters reference Application and their vendor package, nothing else (23 §2.1).");
    }
}
