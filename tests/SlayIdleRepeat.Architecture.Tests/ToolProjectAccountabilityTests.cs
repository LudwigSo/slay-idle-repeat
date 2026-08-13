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
    /// Tools that consume the asset-manifest register and reach nothing else in the repository.
    /// </summary>
    /// <remarks>
    /// <c>SlayIdleRepeat.AssetPipeline</c> (M8-06) is `15` §B4's seven-step post-processing
    /// pipeline plus Part F's acceptance checklist. It is not dependency-free — it takes
    /// SkiaSharp for imaging and project-references
    /// <c>SlayIdleRepeat.AssetManifest</c> for the delivery size, pivot and atlas that `15` §C
    /// and §D2 put in the manifest. It is not <c>CoreOnly</c> either: it references no project
    /// under <c>src/</c> at all, because no game rule is involved in resizing a PNG. And it
    /// composes no adapter, so `23` §7 does not describe it.
    /// <para>
    /// ⚠️ <b>This category and <see cref="RegisterConsumerToolNames"/> are deliberately separate,
    /// and the names are close enough to mislead.</b> Both permit exactly one project reference —
    /// the register. They differ on <i>packages</i>: a register-consumer takes none (M8-01a runs on
    /// in-box <c>net8.0</c>), a manifest-consumer may take an imaging package (M8-06 takes
    /// SkiaSharp). Collapsing them turns <c>Architecture.Tests</c> red immediately. Merged at the
    /// M8 wave-2 integration, 2026-08-13; renaming them to say package-free vs package-bearing is
    /// an M8 milestone-review item.
    /// </para>
    /// </remarks>
    private static readonly string[] ManifestConsumerToolNames = ["SlayIdleRepeat.AssetPipeline"];

    /// <summary>
    /// Tools that compose the three asset-production libraries — the register, the `15` §B4
    /// pipeline and the provenance format — and reach <b>nothing else in this repository</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SlayIdleRepeat.AssetPlaceholders</c> (M8-10) draws one placeholder per runtime art slot,
    /// drives every one through `15` §B4's seven steps and Part F's checklist, and writes a
    /// <c>procedural</c> provenance record beside each image. It is the first tool that needs all
    /// three at once, and none of the four categories above permits that:
    /// <c>CoreOnlyToolNames</c> is pinned to <c>Core</c> (`30` §6) and this touches no game rule;
    /// <c>ToolCompositionRootNames</c> composes an adapter (`23` §7) and this composes none;
    /// <c>DependencyFreeToolNames</c> reaches nothing; <c>RegisterConsumerToolNames</c> and
    /// <c>ManifestConsumerToolNames</c> each permit exactly ONE project reference, the register.
    /// </para>
    /// <para>
    /// 🔒 <b>Stated as a rule, not as a label.</b> A pipeline-consumer tool may reference
    /// <c>SlayIdleRepeat.AssetManifest</c>, <c>SlayIdleRepeat.AssetPipeline</c> and
    /// <c>SlayIdleRepeat.AssetProvenance</c>, and <b>nothing else</b>: no <c>Core</c>, no
    /// <c>Application</c>, no <c>Contracts</c>, no <c>Adapters.*</c>, and no other tool. The teeth
    /// are on the first four specifically — a placeholder generator that reached into
    /// <c>Application</c> "for just one service" would put a build-time asset tool on the game's
    /// use-case layer and make `30` §13's "the whole game is playable from Core alone" quietly
    /// harder to keep true.
    /// </para>
    /// <para>
    /// 🔒 <b>The three permitted references are exactly the three merged M8 dependencies, and that
    /// is the point.</b> Steering S12: a fourth manifest reader, a second `15` §D1 naming
    /// implementation or a parallel Part F path is a duplicate mechanism, and the way this
    /// repository prevents one is by making the real thing reachable rather than by asking nicely.
    /// The rule below therefore <em>requires</em> all three to be present as well as forbidding a
    /// fourth: a tool in this category that had dropped its reference to the pipeline would be
    /// running §B4 somewhere else.
    /// </para>
    /// <para>
    /// Package references are deliberately not restricted here, for the same reason
    /// <see cref="ManifestConsumerToolNames"/> does not restrict them:
    /// <c>Vendor_package_is_referenced_by_exactly_one_project</c> already governs those, and two
    /// rules for one decision is one rule too many. SkiaSharp reaches M8-10 transitively through
    /// <c>SlayIdleRepeat.AssetPipeline</c>, which is what keeps that rule armed.
    /// </para>
    /// </remarks>
    private static readonly string[] PipelineConsumerToolNames = ["SlayIdleRepeat.AssetPlaceholders"];

    /// <summary>
    /// Every project a pipeline-consumer tool may reference, and no other. The three merged M8
    /// asset-production libraries.
    /// </summary>
    private static readonly string[] PipelineConsumerPermittedReferences =
    [
        ManifestProjectName,
        "SlayIdleRepeat.AssetPipeline",
        "SlayIdleRepeat.AssetProvenance",
    ];

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
            .Concat(ManifestConsumerToolNames)
            .Concat(PipelineConsumerToolNames)
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
                    "register and nothing else, and takes no package), or " +
                    "ToolProjectAccountabilityTests.ManifestConsumerToolNames (reads the asset-manifest " +
                    "register and nothing else in the repository, but may take an imaging package), or " +
                    "ToolProjectAccountabilityTests.PipelineConsumerToolNames (composes the asset " +
                    "register, the 15 §B4 pipeline and the provenance format, and nothing else in the " +
                    "repository) — " +
                    "whichever is true of it. Until then it may reference Application, or take a vendor " +
                    "package, with a green suite."));

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

            if (!references.Contains(ManifestProjectName, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"{tool} does not reference '{ManifestProjectName}'. It is listed as a consumer of " +
                    "M8-09's register; a consumer that does not reference it is re-parsing " +
                    "game-data/assets/*.json somewhere else, which is the duplicate mechanism S12 " +
                    "exists to prevent.");
            }

            offenders.AddRange(
                references
                    .Where(r => !r.Equals(ManifestProjectName, StringComparison.Ordinal))
                    .Select(r => $"{tool} project-references '{r}' — a register consumer reaches " +
                                 $"'{ManifestProjectName}' and nothing else (23 §2.1)"));

            offenders.AddRange(
                RepoLayout.PackageReferences(projectFile)
                    .Select(p => $"{tool} references the package '{p}' — a production-pipeline tool " +
                                 "runs on in-box net8.0 (23 §5 A9)"));
        }

        ArchRule.Empty(
            offenders,
            "The register-consumer tools reference SlayIdleRepeat.AssetManifest only (23 §2.1, 30 §6).");
    }

    /// <summary>
    /// `23` §6 / `30` §6 — a manifest-consumer tool project-references
    /// <c>SlayIdleRepeat.AssetManifest</c> and <b>nothing else in this repository</b>: no
    /// <c>Core</c>, no <c>Application</c>, no <c>Contracts</c>, no <c>Adapters.*</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The category exists because M8-06 is the first tool that is neither dependency-free nor
    /// pinned to <c>Core</c>: `15` §B4 post-processing needs an imaging package and the delivery
    /// size/pivot/atlas the register carries, and needs no game rule whatsoever. Naming it
    /// without stating what the name MEANS would be an accountability entry that governs
    /// nothing — the exact defect the rule above this one exists to prevent — so this is the
    /// rule that makes the bucket load-bearing.
    /// </para>
    /// <para>
    /// 🔒 The teeth are on <c>Core</c>/<c>Application</c> specifically. A pipeline that reached
    /// into <c>Application</c> for "just one service" would put a build-time asset tool on the
    /// game's use-case layer and quietly make `30` §13's "the whole game is playable from Core
    /// alone" harder to keep true. Package references are deliberately NOT restricted here:
    /// <c>Vendor_package_is_referenced_by_exactly_one_project</c> already governs those, and
    /// duplicating it would mean two rules to update for one decision.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_manifest_consumer_tools_reference_the_register_and_nothing_else()
    {
        var offenders = new List<string>();

        // S3 — a rule whose subject set can silently empty passes forever.
        if (ManifestConsumerToolNames.Length == 0)
        {
            offenders.Add(
                "ManifestConsumerToolNames is empty, so this rule quantifies over nothing. Either a tool " +
                "belongs in the category or the category should be deleted along with its entry in " +
                "`governed` above — leaving it empty means the accountability rule hands out a free pass.");
        }

        foreach (var tool in ManifestConsumerToolNames)
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

            var references = RepoLayout.ProjectReferences(projectFile).ToArray();

            if (!references.Contains(ManifestProjectName, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"{tool} does not project-reference {ManifestProjectName}, but it is categorised as a " +
                    "consumer of the register. Either it grew its own manifest reader — which 15 §C, §D1 " +
                    "and §D2 must not be transcribed twice — or it belongs in a different category.");
            }

            offenders.AddRange(
                references
                    .Where(r => !r.Equals(ManifestProjectName, StringComparison.Ordinal))
                    .Select(r =>
                        $"{tool} project-references '{r}'. A manifest-consumer tool reaches " +
                        $"{ManifestProjectName} and nothing else in this repository — no Core, no " +
                        "Application, no Contracts, no adapter."));
        }

        ArchRule.Empty(
            offenders,
            "Manifest-consumer tools reference SlayIdleRepeat.AssetManifest and nothing else (23 §6, 30 §6).");
    }

    /// <summary>
    /// `23` §6 / `30` §6 — a pipeline-consumer tool project-references all three asset-production
    /// libraries (<c>SlayIdleRepeat.AssetManifest</c>, <c>SlayIdleRepeat.AssetPipeline</c>,
    /// <c>SlayIdleRepeat.AssetProvenance</c>) and <b>nothing else in this repository</b>: no
    /// <c>Core</c>, no <c>Application</c>, no <c>Contracts</c>, no <c>Adapters.*</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is what makes the category a rule rather than a label — see
    /// <see cref="PipelineConsumerToolNames"/> for why none of the other four describes M8-10.
    /// It has two halves and both are load-bearing. Forbidding a fourth reference keeps a
    /// build-time asset tool off the game's dependency graph. <b>Requiring all three</b> is steering
    /// S12: a placeholder generator that had stopped referencing the pipeline would be running `15`
    /// §B4 somewhere else, and one that had stopped referencing the provenance format would be
    /// writing a second record shape — both are duplicate mechanisms that an "at most these three"
    /// rule would wave through.
    /// </para>
    /// <para>
    /// 🔒 Two floors, both steering S3. An empty category quantifies over nothing and passes
    /// forever while still handing out a free pass in the accountability rule above; a category
    /// naming a project with no <c>.csproj</c> governs a project that is not there.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_pipeline_consumer_tools_reference_the_three_asset_libraries_and_nothing_else()
    {
        var offenders = new List<string>();

        // S3 — a rule whose subject set can silently empty passes forever.
        if (PipelineConsumerToolNames.Length == 0)
        {
            offenders.Add(
                "PipelineConsumerToolNames is empty, so this rule quantifies over nothing. Either a tool " +
                "belongs in the category or the category should be deleted along with its entry in " +
                "`governed` above — leaving it empty means the accountability rule hands out a free pass.");
        }

        // S3 — and the permitted set can empty just as silently, which would turn the "reaches
        // nothing else" half into "reaches nothing at all" and the "references all three" half into
        // a loop over no names.
        if (PipelineConsumerPermittedReferences.Length != 3)
        {
            offenders.Add(
                $"PipelineConsumerPermittedReferences names {PipelineConsumerPermittedReferences.Length} " +
                "projects and the category is defined over exactly three — M8-09's register, M8-06's " +
                "pipeline and M8-01a's provenance format. Adding a fourth is a decision about what an " +
                "asset-production tool may reach, and it belongs in this rule's remarks, not in a list " +
                "that grew by one.");
        }

        foreach (var tool in PipelineConsumerToolNames)
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

            var references = RepoLayout.ProjectReferences(projectFile).ToArray();

            offenders.AddRange(
                PipelineConsumerPermittedReferences
                    .Where(permitted => !references.Contains(permitted, StringComparer.Ordinal))
                    .Select(permitted =>
                        $"{tool} does not project-reference '{permitted}'. A pipeline-consumer tool " +
                        "composes all three asset-production libraries; one it does not reference is one " +
                        "it has grown its own copy of, which is the duplicate mechanism steering S12 " +
                        "exists to prevent."));

            offenders.AddRange(
                references
                    .Where(r => !PipelineConsumerPermittedReferences.Contains(r, StringComparer.Ordinal))
                    .Select(r =>
                        $"{tool} project-references '{r}'. A pipeline-consumer tool reaches " +
                        $"{string.Join(", ", PipelineConsumerPermittedReferences)} and nothing else in " +
                        "this repository — no Core, no Application, no Contracts, no adapter, and no " +
                        "other tool."));
        }

        ArchRule.Empty(
            offenders,
            "Pipeline-consumer tools reference the three asset-production libraries and nothing else " +
            "(23 §6, 30 §6).");
    }

    /// <summary>The register every register- and manifest-consumer tool reads (M8-09).</summary>
    private const string ManifestProjectName = "SlayIdleRepeat.AssetManifest";

    /// <summary>Every production <c>.csproj</c> that lives under <c>tools/</c>.</summary>
    private static IReadOnlyList<string> ToolProjectFiles() =>
        RepoLayout.ProductionProjectFiles
            .Where(p => p.StartsWith(RepoLayout.ToolsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
