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
    /// Tools whose only reference is the asset register, and which take no package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SlayIdleRepeat.AssetProvenance</c> (M8-01a) keys a provenance record to every asset id
    /// M8-09's register holds, so it necessarily project-references
    /// <c>SlayIdleRepeat.AssetManifest</c> — which is what puts it in none of the three sets
    /// above. It is not <c>Core</c>-pinned (`30` §6 pins the two simulation tools and it is
    /// neither), it composes no adapter, and it is not dependency-free.
    /// </para>
    /// <para>
    /// 🔒 The category is stated as "the register, and nothing else" rather than as a bare
    /// exemption. Re-parsing <c>game-data/assets/*.json</c> in a second place is the duplicate
    /// mechanism steering S12 exists to prevent, and reaching past the register into
    /// <c>Application</c> or an adapter would make a production-pipeline tool part of the game's
    /// dependency graph.
    /// </para>
    /// <para>
    /// ⚠️ M8-06 and M8-10 consume the same register, so they land in this category too — and the
    /// day the second one arrives, whatever vocabulary the two share ("is this id art or audio",
    /// "what format is this medium delivered in") belongs in <c>SlayIdleRepeat.AssetManifest</c>
    /// beside the register itself, not copied into each consumer. This rule deliberately does not
    /// let one consumer reference another, so <b>AssetManifest is the only place that consolidation
    /// can go</b>. Deciding it is the milestone conductor's, not one consumer's.
    /// </para>
    /// </remarks>
    private static readonly string[] RegisterConsumerToolNames = ["SlayIdleRepeat.AssetProvenance"];

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
            .Concat(RegisterConsumerToolNames)
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
                    "ProductionAssemblies.ToolCompositionRootNames (composes an adapter itself, 23 §7), " +
                    "ToolProjectAccountabilityTests.DependencyFreeToolNames (reaches nothing), or " +
                    "ToolProjectAccountabilityTests.RegisterConsumerToolNames (reaches M8-09's asset " +
                    "register and nothing else) — whichever is true of it. Until then it may reference " +
                    "Application, or take a vendor package, with a green suite."));

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

    /// <summary>
    /// `23` §2.1 / `30` §6 — a register-consumer tool project-references
    /// <c>SlayIdleRepeat.AssetManifest</c> and nothing else, and takes no package. The
    /// counterpart of <see cref="The_dependency_free_tools_reference_nothing_at_all"/> for the
    /// tools that read M8-09's register.
    /// </summary>
    /// <remarks>
    /// The rule is what makes the category a rule rather than a label. Without it,
    /// <c>SlayIdleRepeat.AssetProvenance</c> would be "accounted for" by the check above while
    /// being free to reference <c>Application</c>, an adapter, or a vendor package.
    /// </remarks>
    [Fact]
    public void The_register_consumer_tools_reference_the_asset_register_and_nothing_else()
    {
        const string register = "SlayIdleRepeat.AssetManifest";
        var offenders = new List<string>();

        if (RegisterConsumerToolNames.Length == 0)
        {
            offenders.Add(
                "RegisterConsumerToolNames is empty. An empty category governs nothing, and the " +
                "accountability rule above would concat an empty list and report success over it.");
        }

        foreach (var tool in RegisterConsumerToolNames)
        {
            var projectFile = ToolProjectFiles().SingleOrDefault(
                p => RepoLayout.ProjectName(p).Equals(tool, StringComparison.Ordinal));

            if (projectFile is null)
            {
                offenders.Add(
                    $"'{tool}' has no .csproj under tools/. A rule keyed on a project that is not " +
                    "there governs nothing — remove the entry, or point it at where the project went.");
                continue;
            }

            var references = RepoLayout.ProjectReferences(projectFile);

            if (!references.Contains(register, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"{tool} does not reference '{register}'. It is listed as a consumer of M8-09's " +
                    "register; a consumer that does not reference it is re-parsing " +
                    "game-data/assets/*.json somewhere else, which is the duplicate mechanism S12 " +
                    "exists to prevent.");
            }

            offenders.AddRange(
                references
                    .Where(r => !r.Equals(register, StringComparison.Ordinal))
                    .Select(r => $"{tool} project-references '{r}' — a register consumer reaches " +
                                 $"'{register}' and nothing else (23 §2.1)"));

            offenders.AddRange(
                RepoLayout.PackageReferences(projectFile)
                    .Select(p => $"{tool} references the package '{p}' — a production-pipeline tool " +
                                 "runs on in-box net8.0 (23 §5 A9)"));
        }

        ArchRule.Empty(
            offenders,
            "The register-consumer tools reference SlayIdleRepeat.AssetManifest only (23 §2.1, 30 §6).");
    }

    /// <summary>Every production <c>.csproj</c> that lives under <c>tools/</c>.</summary>
    private static IReadOnlyList<string> ToolProjectFiles() =>
        RepoLayout.ProductionProjectFiles
            .Where(p => p.StartsWith(RepoLayout.ToolsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
