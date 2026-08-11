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
    /// Scoped to <c>src/</c>. Test projects legitimately share xUnit, FluentAssertions and
    /// the test SDK — `23` §3 has `Contract.Tests` running one suite against every adapter,
    /// so a solution-wide uniqueness rule would contradict the document it enforces.
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
    /// Scoped to <c>src/</c> for the same reason as A9: `Contract.Tests` runs the shared
    /// port suite against every adapter and `Application.Tests` uses the in-memory fakes
    /// (`23` §3), both by design.
    /// </remarks>
    [Fact]
    public void Only_composition_roots_reference_adapter_projects()
    {
        var offenders =
            from projectFile in RepoLayout.ProductionProjectFiles
            let project = RepoLayout.ProjectName(projectFile)
            where !ProductionAssemblies.CompositionRootNames.Contains(project, StringComparer.Ordinal)
            from referenced in RepoLayout.ProjectReferences(projectFile)
            where ProductionAssemblies.IsAdapter(referenced)
            select $"{project} references the adapter {referenced}";

        ArchRule.Empty(
            offenders,
            "Only SlayIdleRepeat.Server and SlayIdleRepeat.Client reference SlayIdleRepeat.Adapters.* (23 §6, §7).");
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
