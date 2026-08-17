using System.Text.RegularExpressions;
using Mono.Cecil;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `14` §5 / `23` §9 — the other half of the presenter/scene split. <see cref="PresenterBoundaryRuleTests"/>
/// pins what a presenter may not name; this pins what a SCENE may not name.
/// </summary>
/// <remarks>
/// <para>
/// `14` §5: "Godot scenes are driving adapters. They render and forward input; they hold no rules
/// and <b>no port references</b>." `23` §9 states the same recommendation from the other side —
/// "presenters receive their ports as constructor arguments from the root; scenes only ever talk
/// to presenters" — and `23` §7 reserves naming a concrete adapter to the composition root alone.
/// </para>
/// <para>
/// 🔒 <b>Why this file exists at all.</b> Until M7-01's loop-back the scene half was pinned by
/// nothing: <see cref="PresenterBoundaryRuleTests"/> uses the root scene only as a CONTROL — proof
/// that its engine detector fires — so a later screen could have held a port, called
/// <c>GodotClientComposition.BuildCapabilities</c> itself, or composed a second graph, with the
/// whole suite green. M7-01 is the precedent M7-03 through M7-08 and M7-11 inherit, and a
/// precedent that only one half of is mechanical is a precedent half of which is a suggestion.
/// </para>
/// <para>
/// ⚠️ <b>What a scene IS allowed to hold is the shape this branch shipped, and no more.</b>
/// <c>AppRoot</c> holds the composed graph — <c>ComposedGodotClient</c>, which is the client's own
/// composition type, not an adapter — for the reason that hand-rolled composition has no container
/// to keep the graph alive, and it hands its presenter one thing out of it: <c>IGameHost</c>, an
/// Application <em>hosting</em> interface under <c>SlayIdleRepeat.Application.Hosting</c>. That is
/// not a port, and the rules below are scoped at <c>SlayIdleRepeat.Application.Ports</c> precisely
/// so it stays permitted. <see cref="The_root_scene_holds_the_host_it_shipped_with_and_nothing_more"/>
/// is what proves that is a deliberate boundary rather than a blind detector.
/// </para>
/// </remarks>
public sealed class SceneBoundaryRuleTests
{
    /// <summary>The engine base class every scene script ultimately derives from.</summary>
    /// <remarks>
    /// This — not a name suffix — is what makes "is this a scene?" mechanical. Scenes have no
    /// shared suffix the way presenters do (<c>AppRoot</c>, and `14` §5's layout promises
    /// <c>boot</c>, <c>home</c>, <c>board</c>, <c>battle</c>…), so the namespace-escape arm below
    /// asks the engine's own question instead: a script attached to a node in a scene tree is a
    /// <c>Godot.Node</c> subclass, and nothing else in the client is.
    /// </remarks>
    private const string EngineNodeTypeName = "Godot.Node";

    /// <summary>An engine type that is NOT a node — the negative half of the classifier's control.</summary>
    private const string EngineNonNodeTypeName = "Godot.Resource";

    /// <summary>
    /// The hosting interface the root scene is sanctioned to hand its presenter — looked up by
    /// SIMPLE name so that moving it under the ports namespace is caught rather than hidden.
    /// </summary>
    /// <remarks>
    /// 🔒 A full name would make the boundary assertion a comparison of two literals, which cannot
    /// fail and is therefore a defect rather than a weak test (steering S1). The simple name still
    /// resolves after a move; the namespace it resolves INTO is the thing being asserted.
    /// </remarks>
    private const string HostingInterfaceName = "IGameHost";

    /// <summary>
    /// The scene-side helper the floor is stated over by name — the one governed subject the escape
    /// arm below is structurally unable to recapture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 M7-04 put the first free-standing type that is NOT a node into the scenes namespace: a
    /// static helper that resolves the display server's safe area for every screen drawing to the
    /// edge. Both rules in this file govern it <em>today</em>, because both quantify over the
    /// namespace and the directory rather than over the node classifier. Nothing holds it there.
    /// </para>
    /// <para>
    /// 🔴 The escape arm asks "does this type derive from <c>Godot.Node</c>?", and the answer for a
    /// static helper is no. So moving its file to <c>game/util/</c> and its namespace with it would
    /// take it out of both subject sets with every arm of this file green — and a helper that
    /// already names <c>DisplayServer</c> would then be free to name a port beside it. That is the
    /// hole the escape arm's own remarks disclose; naming the member is the only thing that closes
    /// it for the member that exists.
    /// </para>
    /// </remarks>
    private const string SceneHelperName = "SafeAreaInsets";

    /// <summary>An engine node reached only through intermediate engine types — the walk's control.</summary>
    /// <remarks>
    /// <c>Godot.Control</c> is <c>CanvasItem</c> is <c>Node</c>. Every screen M7-03 onwards adds
    /// will derive from this rather than from <c>Node</c> directly, so the classifier's ability to
    /// walk THROUGH GodotSharp — resolving across an assembly boundary, not just comparing one
    /// base-type name — is what decides whether this file governs the scenes still to be written.
    /// </remarks>
    private const string EngineIndirectNodeTypeName = "Godot.Control";

    /// <summary>
    /// The two namespaces a scene may not name, as written. Deliberately the namespace roots
    /// rather than a list of port or adapter type names: `14` §5 bans the category, not a census
    /// of it, and a census goes stale on the commit that adds the next port.
    /// </summary>
    private static readonly Regex PortOrAdapterNameInSource = new(
        @"\bSlayIdleRepeat\.(?:Application\.Ports|Adapters)\b",
        RegexOptions.Compiled);

    /// <summary>The client assembly's metadata.</summary>
    private static ModuleDefinition ClientModule => ProductionAssemblies.Module(ProductionAssemblies.ClientName);

    /// <summary>Every type declared under the scenes namespace of the client assembly.</summary>
    /// <remarks>
    /// Nested types are in, and that is load-bearing rather than incidental: the body of an
    /// <c>async</c> scene method lives in a compiler-generated state machine nested inside the
    /// scene, so a rule over the outer type alone would see none of the IL that actually runs —
    /// and <c>AppRoot.ComposeAndStartAsync</c>, the one method that touches the composed graph, is
    /// exactly such a method.
    /// </remarks>
    private static IReadOnlyList<TypeDefinition> Scenes { get; } =
        Il.TypesUnder(ClientModule, PresenterBoundaryRuleTests.ScenesNamespace).ToArray();

    /// <summary>The composition source directory — where the source arm's positive control lives.</summary>
    private static string CompositionSourceDirectory { get; } =
        Path.Combine(RepoLayout.ProjectDirectory(ProductionAssemblies.ClientName), "Composition");

    /// <summary>
    /// `14` §5 / `23` §9 — 🔒 a scene names no port and no concrete adapter. "They render and
    /// forward input; they hold no rules and no port references" is only true while nothing under
    /// the scenes namespace reaches for either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ports and adapters are banned together because they are the same mistake at two depths. A
    /// scene holding <c>IRewardedAdPort</c> has taken a decision that `23` §9 puts in the
    /// presenter; a scene naming <c>AutoGrantRewardedAd</c> has additionally taken the choice of
    /// implementation away from the composition root, which `23` §7 makes the only place concrete
    /// types are named.
    /// </para>
    /// <para>
    /// 🔒 <b>Reachability, not just declaration.</b> The detector reads base types, interfaces,
    /// fields, properties, method signatures, locals and every operand of every instruction, so it
    /// catches the realistic shape as well as the obvious one: not only <c>private IGameApiPort
    /// _api;</c> but <c>_composed.Client.RewardedAds.Port</c> — a scene that declares nothing and
    /// simply reads a port off the graph it is allowed to own. That second shape is the one this
    /// branch made possible by giving the root scene the whole <c>ComposedGodotClient</c>, and it
    /// is invisible to any rule that only inspects declarations.
    /// </para>
    /// <para>
    /// 🔒 This arm and the source arm below are complementary, not redundant — see the source
    /// arm's remarks for the two shapes this one cannot see.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_scene_names_no_port_and_no_adapter()
    {
        var offenders = Scenes.SelectMany(PortOrAdapterReferences);

        ArchRule.Empty(
            offenders,
            "A scene names no port and no adapter — IL scan (14 §5, 23 §7, 23 §9).");
    }

    /// <summary>
    /// `14` §5 — 🔒 no scene source file writes the ports or the adapters namespace, checked as
    /// text with comments and string literals stripped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Not a duplicate of the IL arm, and the reason differs from the presenter file's.</b>
    /// There the blind spot is an inlined <c>const</c> (steering S18). A port is an interface, so
    /// that particular shape barely arises — but two others do, and both are live risks here
    /// rather than theoretical ones:
    /// </para>
    /// <para>
    /// <b>1. Platform conditionals.</b> `23` §7.2 writes the client's own composition with
    /// <c>#if ANDROID / #elif IOS</c>, so conditional compilation is an established idiom in
    /// exactly this project. A <c>#if IOS</c> branch in a scene naming a port compiles into
    /// nothing on the machine CI runs on: the IL arm has no reference to object to and reports
    /// success, and the violation ships to the one platform nobody built.
    /// </para>
    /// <para>
    /// <b>2. <c>nameof</c>.</b> <c>nameof(IRewardedAdPort)</c> folds to a string literal at
    /// compile time and leaves no type reference at all — the same inlining that made
    /// <c>Core_internal_layering_holds</c> blind to a cross-layer constant for three milestones.
    /// </para>
    /// <para>
    /// The traffic runs the other way too, which is why neither arm is sufficient alone: the IL
    /// arm sees a port reached through a property chain (<c>_composed.Client.RewardedAds.Port</c>)
    /// where no banned word appears in the file at all, and it resolves a <c>using</c> alias the
    /// text could only guess at. Deleting either leaves a real hole.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_scene_source_file_never_names_the_ports_or_adapters_namespace()
    {
        var offenders =
            from file in RepoLayout.SourceFiles(PresenterBoundaryRuleTests.SceneSourceDirectory)
            let source = SourceText.Read(file)
            from hit in source.Hits(PortOrAdapterNameInSource)
            select $"{hit}  [a scene talks to its presenter, never to a port or an adapter]";

        ArchRule.Empty(
            offenders,
            "No scene source file names the ports or adapters namespace — source grep (14 §5).");
    }

    /// <summary>
    /// `23` §6 — the floor under the two rules above, stated by NAMED MEMBER rather than by a
    /// count, plus the namespace-escape arm that keeps the subject set growing with the game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both rules are "no member of set S does X" and both pass over an empty S. S is a namespace
    /// filter and a directory path, so renaming either — <c>game/scenes</c> to <c>Scenes</c>, or
    /// the namespace to match the folder — empties it and takes both rules permanently green,
    /// with the scenes still sitting there naming whatever they like.
    /// </para>
    /// <para>
    /// 🔒 A count would not do, and on this project that is a fact rather than a caution: a count
    /// floor has been satisfied here by a populated WRONG directory. So <c>AppRoot</c> is named,
    /// in the assembly and on disk.
    /// </para>
    /// <para>
    /// 🔒 <b>And naming one member closes nothing on its own — the escape arm is the point.</b> A
    /// later screen dropping <c>BoardScene</c> into a namespace of its own leaves this floor
    /// satisfied, both rules quantifying over a set that never grows, and the new scene free to
    /// hold a port. The presenter file closes the same hole with a name suffix; scenes have no
    /// suffix, so this arm asks the stronger question instead — <b>does the type derive from
    /// <c>Godot.Node</c>?</b> — which is what a scene script mechanically IS, walked through
    /// intermediate engine types so that a <c>Control</c> or a <c>Node2D</c> subclass counts.
    /// </para>
    /// <para>
    /// ⚠️ <b>What it still cannot catch, stated plainly because a later task will rely on it.</b>
    /// A scene written in GDScript is not in this assembly and no C# metadata rule will ever see
    /// it. A scene-side helper that is not itself a node — a static formatter, a struct of view
    /// data — is not classified as a scene, so the escape arm cannot drag it back once it leaves.
    /// And a <c>.tscn</c> with no script at all is outside every arm of this file. The escape arm's
    /// claim is bounded to: every C# type in the client assembly that derives from
    /// <c>Godot.Node</c>.
    /// </para>
    /// <para>
    /// 🔒 <b>Which is why the named half of the floor carries the helper too.</b> M7-04 shipped the
    /// first of them — <see cref="SceneHelperName"/> — and a rule whose disclosed blind spot has
    /// just been populated is a rule with a live hole, not a documented one. Naming it does not
    /// make the arm self-growing for helpers written later; it holds the one that exists inside the
    /// set both rules quantify over. A second such helper owes itself the same line, and until this
    /// project finds a mechanical answer to "is this a scene-side helper?", that is the honest
    /// shape: population-wide for nodes, by name for everything else in the namespace.
    /// </para>
    /// <para>
    /// A base type this arm cannot RESOLVE is reported as an offender rather than waved through.
    /// A classifier that answers "not a node" when it means "I could not tell" is the same silence
    /// as an empty subject set, one indirection out.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_scene_subject_set_contains_AppRoot_and_no_engine_node_sits_outside_it()
    {
        Scenes.Select(type => type.Name).ShouldContain(
            PresenterBoundaryRuleTests.AppRootSceneName,
            $"'{PresenterBoundaryRuleTests.AppRootSceneName}' is not among the types under " +
            $"{PresenterBoundaryRuleTests.ScenesNamespace}, so the IL arm is quantifying over a set that no " +
            "longer holds the scenes. Either the namespace moved or the scene did; point the rule at wherever " +
            "they went rather than leaving it green.");

        Scenes.Select(type => type.Name).ShouldContain(
            SceneHelperName,
            $"'{SceneHelperName}' is not among the types under {PresenterBoundaryRuleTests.ScenesNamespace}. It " +
            "is not a node, so the escape arm below cannot notice it has gone: it would simply stop being " +
            "scanned, in a namespace of its own, still naming the engine and free to name a port. Either it " +
            "moved and must move back, or it was renamed — in which case rename it here rather than deleting " +
            "the assertion.");

        var sceneFileNames = RepoLayout.SourceFiles(PresenterBoundaryRuleTests.SceneSourceDirectory)
                                       .Select(Path.GetFileNameWithoutExtension)
                                       .ToArray();

        sceneFileNames.ShouldContain(
            PresenterBoundaryRuleTests.AppRootSceneName,
            $"no '{PresenterBoundaryRuleTests.AppRootSceneName}.cs' under " +
            $"{RepoLayout.Relative(PresenterBoundaryRuleTests.SceneSourceDirectory)}, so the source arm " +
            "is grepping a directory the scenes have left. That arm is the only one that can see a " +
            "#if-excluded branch or a nameof, and a grep over the wrong directory sees neither.");

        sceneFileNames.ShouldContain(
            SceneHelperName,
            $"no '{SceneHelperName}.cs' under " +
            $"{RepoLayout.Relative(PresenterBoundaryRuleTests.SceneSourceDirectory)}. The type may still be in " +
            "the scenes namespace while its FILE has left the directory the source arm greps — and that arm is " +
            "the only one that can see a #if-excluded branch or a nameof, so the helper would keep half its " +
            "governance and lose the other half silently.");

        var offenders = new List<string>();

        foreach (var type in Il.AllTypes(ClientModule))
        {
            var (verdict, detail) = ClassifyAsEngineNode(type);

            if (verdict == NodeVerdict.Unknown)
            {
                offenders.Add(
                    $"{type.FullName} has a base type '{detail}' this rule could not resolve, so it cannot say " +
                    "whether the type is a scene. GodotSharp.dll must be in the test output directory for the " +
                    "base-type walk to reach Godot.Node — without it every screen deriving from Control reads " +
                    "as 'not a scene' and this arm governs nothing.");
                continue;
            }

            if (verdict == NodeVerdict.No)
            {
                continue;
            }

            if (Il.IsUnder(Il.NamespaceOf(type), PresenterBoundaryRuleTests.ScenesNamespace))
            {
                continue;
            }

            offenders.Add(
                $"{type.FullName} derives from {EngineNodeTypeName} and lives outside " +
                $"{PresenterBoundaryRuleTests.ScenesNamespace}, where neither the IL arm nor the source arm " +
                "looks — so nothing stops it holding a port or naming an adapter. Move it under the scenes " +
                $"namespace and its file under {RepoLayout.Relative(PresenterBoundaryRuleTests.SceneSourceDirectory)}, " +
                "or stop making it a node");
        }

        ArchRule.Empty(
            offenders,
            "Every engine node in the client assembly is inside the governed scenes namespace (23 §6, 14 §5).");
    }

    /// <summary>
    /// `23` §7 / `23` §9 — the teeth: both halves of both detectors are proven to fire, on the
    /// composition root, which is the one place `23` §7 lets a port and a concrete adapter be
    /// named together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Each rule above is a disjunction, and a passing disjunction proves nothing about the
    /// disjunct that never matched.</b> If the ports namespace were renamed, or
    /// <c>ProductionAssemblies.IsAdapter</c> stopped recognising an adapter, or the regex lost an
    /// alternative, the corresponding rule would keep passing under a name promising twice what it
    /// still delivered — and a scene could construct <c>AutoGrantRewardedAd</c> unnoticed. Phase 2
    /// of this project found two presenter rules in exactly that state: detectors with no
    /// permanent control, green because they could not fire at all.
    /// </para>
    /// <para>
    /// So all four are exercised here: IL/port, IL/adapter, source/port, source/adapter. The
    /// composition root names <c>IRewardedAdPort</c> and constructs <c>AutoGrantRewardedAd</c>,
    /// and its source carries a <c>using</c> for each namespace — which is not a coincidence to be
    /// relied on quietly but the definition of a composition root doing its job. Stated over the
    /// whole composition namespace rather than one type, so it survives the root being
    /// reorganised, but not the root ceasing to compose.
    /// </para>
    /// </remarks>
    [Fact]
    public void Both_detectors_fire_on_the_composition_root_for_a_port_and_for_an_adapter()
    {
        var compositionTypes =
            Il.TypesUnder(ClientModule, PresenterBoundaryRuleTests.CompositionNamespace).ToArray();

        compositionTypes.ShouldNotBeEmpty(
            $"no type under {PresenterBoundaryRuleTests.CompositionNamespace}, so this control quantifies over " +
            "nothing and neither half of the IL detector is proven by any subject at all.");

        compositionTypes.Select(type => type.FullName).ShouldNotContain(
            name => Scenes.Any(scene => scene.FullName.Equals(name, StringComparison.Ordinal)),
            "the composition root has ended up inside the scenes' subject set, which would make the IL arm red " +
            "for the one type 23 §7 requires to name adapters. The split is by namespace; putting the root in " +
            "the scenes namespace erases it.");

        var references = compositionTypes.SelectMany(PortOrAdapterReferences).ToArray();

        references.ShouldContain(
            offender => offender.Contains(Domain.PortsNamespace, StringComparison.Ordinal),
            $"the composition root names no type under {Domain.PortsNamespace} that the detector can see. Only " +
            "the port arm of IsPortOrAdapter can put one into this list, so either the root has stopped wiring " +
            "ports, or the arm that is supposed to catch a scene holding one has gone blind — at which point " +
            "A_scene_names_no_port_and_no_adapter is enforcing half of what its name says.");

        references.ShouldContain(
            offender => offender.Contains(ProductionAssemblies.AdapterPrefix, StringComparison.Ordinal),
            "the composition root names no adapter that the detector can see. Only the adapter arm of " +
            "IsPortOrAdapter can put an assembly under " + ProductionAssemblies.AdapterPrefix + " into this " +
            "list, so either the root has stopped wiring concrete adapters, or the arm that is supposed to " +
            "catch a scene reaching for one has gone blind.");

        var sourceHits = SourceHitsIn(CompositionSourceDirectory);

        sourceHits.ShouldContain(
            hit => hit.Contains(Domain.PortsNamespace, StringComparison.Ordinal),
            $"the source pattern matches no ports namespace under {RepoLayout.Relative(CompositionSourceDirectory)}, " +
            "where a file that writes 'using SlayIdleRepeat.Application.Ports.Client;' at the top lives. The " +
            "pattern, the comment/literal blanking or the directory has moved out from under the source arm, " +
            "and that arm is now grepping for something it can never find — indistinguishable, in its result, " +
            "from every scene being clean.");

        sourceHits.ShouldContain(
            hit => hit.Contains(ProductionAssemblies.AdapterPrefix.TrimEnd('.'), StringComparison.Ordinal),
            $"the source pattern matches no adapters namespace under {RepoLayout.Relative(CompositionSourceDirectory)}, " +
            "where a file that writes 'using SlayIdleRepeat.Adapters.Ads.AutoGrant;' at the top lives. A regex " +
            "with two alternatives needs both proven: one dead alternative is a rule enforcing half its name.");
    }

    /// <summary>
    /// `14` §5 / `23` §9 — 🔒 the negative control, and the pin on the shape this branch actually
    /// shipped: the root scene holds the composed graph and hands its presenter <c>IGameHost</c>,
    /// and the rules above permit exactly that because the boundary is drawn at
    /// <c>SlayIdleRepeat.Application.Ports</c> — not because they cannot see it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>A rule that passes is not the same as a rule that permits.</b> Everything above would
    /// also be green if the detector never reached <c>AppRoot</c> at all — wrong namespace, empty
    /// reference walk, a scene set built from the wrong module. This test asserts the detector
    /// DOES reach into the root scene, by naming a type it demonstrably carries, and that the type
    /// it carries falls on the permitted side of the line for a stated reason:
    /// <c>SlayIdleRepeat.Application.Hosting</c> is a hosting interface, not a port, and `23` §9's
    /// "scenes only ever talk to presenters" is satisfied by a root that hands the host on and
    /// reads nothing else out of the graph.
    /// </para>
    /// <para>
    /// ⚠️ <b>Widening the ban by one namespace segment would make this shape illegal.</b> That is
    /// the whole content of the assertion: the green result on <c>AppRoot</c> is a scoping
    /// decision this file is accountable for, and if <c>IGameHost</c> ever moves under
    /// <c>Ports</c>, the root scene must change — not the rule.
    /// </para>
    /// <para>
    /// 🔴 <b>Stated over the scene AND the types nested inside it, and the first run of this test
    /// is why.</b> It failed asserting that <c>AppRoot</c> names <c>IGameHost</c> — because it does
    /// not: the whole of <c>ComposeAndStartAsync</c> lives in a compiler-generated state machine
    /// nested inside the scene, and the outer type names only <c>ComposedGodotClient</c> and
    /// <c>AppRootPresenter</c>. See <see cref="PartsOf"/>. The RULES above were never affected —
    /// <c>Il.TypesUnder</c> enumerates nested types as subjects in their own right — but a control
    /// written the obvious way would have been proving the detector reaches the half of the scene
    /// where nothing happens.
    /// </para>
    /// <para>
    /// 🔒 The scene classifier gets its control here too. <c>AppRoot</c> must classify as a node
    /// and <c>AppRootPresenter</c> must not, or the escape arm is quantifying over everything or
    /// nothing. And because every screen after this one derives from <c>Control</c> rather than
    /// from <c>Node</c>, the multi-hop walk is proven on the engine's own types: <c>Control</c>
    /// reaches <c>Node</c> across an assembly boundary and through two intermediates, while
    /// <c>Resource</c> — engine, resolvable, several hops deep, and not a scene — does not.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_root_scene_holds_the_host_it_shipped_with_and_nothing_more()
    {
        var scene = Scenes.FirstOrDefault(
            type => type.Name.Equals(PresenterBoundaryRuleTests.AppRootSceneName, StringComparison.Ordinal));

        scene.ShouldNotBeNull(
            $"no '{PresenterBoundaryRuleTests.AppRootSceneName}' under {PresenterBoundaryRuleTests.ScenesNamespace}. " +
            "Without it this control asserts nothing and the rules above could be green because the scan reaches " +
            "no scene at all, which is a different claim entirely.");

        var hosts = Domain.ApplicationTypes
                          .Where(type => type.Name.Equals(HostingInterfaceName, StringComparison.Ordinal))
                          .ToArray();

        hosts.ShouldHaveSingleItem(
            $"'{HostingInterfaceName}' is not a single type in {ProductionAssemblies.ApplicationName}, so the " +
            "sanctioned shape cannot be checked against the type it is a claim about. The root scene's whole " +
            "reason to touch the composed graph is to hand this on.");

        var host = hosts[0];

        Il.IsUnder(Il.NamespaceOf(host), Domain.PortsNamespace).ShouldBeFalse(
            $"{host.FullName} now lives under {Domain.PortsNamespace}, so the shape this branch shipped has " +
            "become a port reference. 14 §5 is 🔒 that a scene holds no port references: the root scene has to " +
            "stop naming it — do NOT narrow the rule to keep the scene legal.");

        var rootSceneParts = PartsOf(PresenterBoundaryRuleTests.AppRootSceneName);

        rootSceneParts.SelectMany(Il.ReferencedTypeNames).ShouldContain(
            host.FullName,
            $"the root scene names no {host.FullName}. Either it stopped handing the host to its presenter — in " +
            "which case the composition seam this milestone is about has moved — or the reference walk no " +
            "longer reaches into the scene, in which case every rule in this file is passing because it can see " +
            "nothing rather than because there is nothing to see.");

        rootSceneParts.SelectMany(PortOrAdapterReferences).ShouldBeEmpty(
            "the root scene trips its own rule. The shape 23 §9 sanctions is a scene that owns the composed " +
            "graph and reads one hosting interface out of it; anything else it now names is either a port or " +
            "an adapter, and the fix is in the scene, not here.");

        var engineModule = scene!.BaseType.Resolve().Module;

        ClassifyAsEngineNode(scene).Verdict.ShouldBe(
            NodeVerdict.Yes,
            "the root scene does not classify as an engine node, so the namespace-escape arm is quantifying " +
            "over a set that excludes the one scene the repository has. A classifier that recognises no scene " +
            "reports every stray scene as compliant.");

        ClassifyAsEngineNode(engineModule.GetType(EngineIndirectNodeTypeName)).Verdict.ShouldBe(
            NodeVerdict.Yes,
            $"{EngineIndirectNodeTypeName} does not classify as an engine node, so the walk is comparing one " +
            "base-type name rather than following the chain. Every screen from M7-03 on derives from it, and " +
            "under this failure every one of them sits outside the escape arm with nothing saying so.");

        ClassifyAsEngineNode(engineModule.GetType(EngineNonNodeTypeName)).Verdict.ShouldBe(
            NodeVerdict.No,
            $"{EngineNonNodeTypeName} classifies as an engine node, so the walk is answering 'yes' for engine " +
            "types generally rather than for nodes. A classifier that cannot say no would drag every " +
            "engine-derived type in the client into the scenes namespace and prove nothing about scenes.");

        var presenter = Il.TypesUnder(ClientModule, PresenterBoundaryRuleTests.PresentersNamespace)
                          .FirstOrDefault(type => type.Name.Equals(
                              PresenterBoundaryRuleTests.AppRootPresenterName, StringComparison.Ordinal));

        presenter.ShouldNotBeNull(
            $"no '{PresenterBoundaryRuleTests.AppRootPresenterName}' to state the classifier's negative over. " +
            "The presenter floor in PresenterBoundaryRuleTests is what should have caught this first.");

        ClassifyAsEngineNode(presenter!).Verdict.ShouldBe(
            NodeVerdict.No,
            "the presenter classifies as an engine node. It is plain C# deriving from object, so the classifier " +
            "is returning Yes for everything — at which point the escape arm demands the whole client assembly " +
            "move into the scenes namespace and says nothing about scenes at all.");
    }

    /// <summary>
    /// Every type in <see cref="Scenes"/> belonging to one scene script — the script itself and
    /// the compiler-generated types nested inside it.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Written because the control that uses it failed the first time it ran, correctly.</b>
    /// <c>Il.ReferencedTypeReferences</c> is stated over ONE type and does not descend into nested
    /// types, and <c>AppRoot</c>'s only contact with the composed graph is inside an <c>async</c>
    /// method — whose body the compiler moves wholesale into a nested state machine. So the outer
    /// type names <c>ComposedGodotClient</c> and <c>AppRootPresenter</c> and nothing else: no
    /// <c>IGameHost</c>, no <c>ComposedClient</c>. A control asserting over the outer type alone
    /// would have been asserting over the half of the scene that does nothing.
    /// </remarks>
    private static IReadOnlyList<TypeDefinition> PartsOf(string sceneName) =>
        Scenes.Where(type => OutermostName(type).Equals(sceneName, StringComparison.Ordinal)).ToArray();

    /// <summary>The outermost declaring type's name, so a state machine is attributed to its scene.</summary>
    private static string OutermostName(TypeDefinition type)
    {
        var outer = type;
        while (outer.DeclaringType is not null)
        {
            outer = outer.DeclaringType;
        }

        return outer.Name;
    }

    /// <summary>Source-arm hits under a directory, as <c>relative/path.cs:line -&gt; match</c>.</summary>
    private static IReadOnlyList<string> SourceHitsIn(string directory) =>
        (from file in RepoLayout.SourceFiles(directory)
         let source = SourceText.Read(file)
         from hit in source.Hits(PortOrAdapterNameInSource)
         select hit).ToArray();

    /// <summary>Port and adapter references a type carries, described for a failure message.</summary>
    private static IEnumerable<string> PortOrAdapterReferences(TypeDefinition type) =>
        Il.ReferencedTypeReferences(type)
          .Where(IsPortOrAdapter)
          .Select(reference =>
              $"{type.FullName} names {reference.FullName} from {Describe(reference)} — a scene renders and " +
              "forwards input, holds no port references, and leaves concrete adapters to the composition root " +
              "(14 §5, 23 §7, 23 §9)")
          .Distinct(StringComparer.Ordinal);

    /// <summary>True for a reference to a port interface or to any type from an adapter assembly.</summary>
    private static bool IsPortOrAdapter(TypeReference reference) =>
        Il.IsUnder(NamespaceOfReference(reference), Domain.PortsNamespace) ||
        ProductionAssemblies.IsAdapter(Il.AssemblyNameOf(reference));

    /// <summary>The namespace of a type REFERENCE, walked out to the outermost declaring type.</summary>
    /// <remarks>
    /// Cecil leaves <c>Namespace</c> empty on a nested type reference and keeps it on the outer
    /// one, so a port's nested type would otherwise read as being in the global namespace and slip
    /// past the ports check.
    /// </remarks>
    private static string NamespaceOfReference(TypeReference reference)
    {
        var outer = reference;
        while (outer.DeclaringType is not null)
        {
            outer = outer.DeclaringType;
        }

        return outer.Namespace ?? string.Empty;
    }

    private static string Describe(TypeReference reference)
    {
        var assembly = Il.AssemblyNameOf(reference);
        return assembly.Length > 0 ? assembly : "an unresolved scope";
    }

    /// <summary>
    /// Walks a type's base chain looking for <see cref="EngineNodeTypeName"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk stops the moment the chain leaves GodotSharp and the client. Everything past that
    /// point is BCL — <c>object</c>, <c>Enum</c>, <c>ValueType</c> — which is neither a node nor on
    /// the way to one, and resolving it would need reference assemblies this suite does not ship
    /// in its output directory. Stopping there is what keeps the walk from throwing on
    /// <c>System.Object</c>.
    /// </para>
    /// <para>
    /// 🔒 <c>Unknown</c> is a third answer on purpose. Cecil's <c>Resolve</c> returns null when the
    /// assembly cannot be found, and collapsing that into "not a node" would turn a missing
    /// GodotSharp.dll into a silently vacuous escape arm — the exact failure mode the arm exists
    /// to prevent, relocated into its own helper.
    /// </para>
    /// </remarks>
    private static (NodeVerdict Verdict, string Detail) ClassifyAsEngineNode(TypeDefinition type)
    {
        var baseType = type.BaseType;

        while (baseType is not null)
        {
            if (baseType.FullName.Equals(EngineNodeTypeName, StringComparison.Ordinal))
            {
                return (NodeVerdict.Yes, EngineNodeTypeName);
            }

            var assembly = Il.AssemblyNameOf(baseType);

            if (!assembly.Equals(PresenterBoundaryRuleTests.EngineAssemblyName, StringComparison.Ordinal) &&
                !assembly.Equals(ProductionAssemblies.ClientName, StringComparison.Ordinal))
            {
                return (NodeVerdict.No, baseType.FullName);
            }

            TypeDefinition? definition;
            try
            {
                definition = baseType.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                definition = null;
            }

            if (definition is null)
            {
                return (NodeVerdict.Unknown, baseType.FullName);
            }

            baseType = definition.BaseType;
        }

        return (NodeVerdict.No, "object");
    }

    /// <summary>Whether a type is an engine node, is not, or could not be decided.</summary>
    private enum NodeVerdict
    {
        /// <summary>The base chain was walked to its end and never reached the engine's node type.</summary>
        No = 1,

        /// <summary>The base chain reaches <c>Godot.Node</c>, so the type is a scene script.</summary>
        Yes = 2,

        /// <summary>A base type could not be resolved, so the question was not answered.</summary>
        Unknown = 3,
    }
}
