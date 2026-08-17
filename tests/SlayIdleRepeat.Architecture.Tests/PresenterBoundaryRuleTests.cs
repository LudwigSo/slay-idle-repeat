using System.Text.RegularExpressions;
using Mono.Cecil;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `14` §5 / `23` §5 A10 — the presenter/scene split in the Godot client. Scenes are driving
/// adapters and may name the engine; presenters are plain C# and may not.
/// </summary>
public sealed class PresenterBoundaryRuleTests
{
    /// <summary>Where the plain-C# presenters live.</summary>
    internal const string PresentersNamespace = "SlayIdleRepeat.Client.Game.Presenters";

    /// <summary>Where the scene scripts live — driving adapters, and the negative control below.</summary>
    internal const string ScenesNamespace = "SlayIdleRepeat.Client.Game.Scenes";

    /// <summary>The client-side composition root — the half of the split that names adapters.</summary>
    internal const string CompositionNamespace = "SlayIdleRepeat.Client.Composition";

    /// <summary>The presenter the floor is stated over by name.</summary>
    internal const string AppRootPresenterName = "AppRootPresenter";

    /// <summary>
    /// The atlas outcome M7-03 put in the presenters namespace — a governed subject the stray arm
    /// below is structurally unable to recapture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 The stray arm asks "is this type spelled as a presenter and living outside the
    /// namespace?", and it recognises a presenter by the <see cref="PresenterTypeSuffix"/>. This
    /// type is not spelled that way. Both rules in this file govern it <em>today</em>, because both
    /// quantify over the namespace and the directory rather than over the name — but moving its
    /// file to <c>game/model/</c> and its namespace with it would take it out of both subject sets
    /// with every arm of both rules green, and a plain-C# type that had escaped the rule against
    /// naming the engine would then be free to name it.
    /// </para>
    /// <para>
    /// 🔒 Naming it does not make the stray arm self-growing for types written later; it holds the
    /// one that exists inside the set both rules quantify over. The same shape
    /// <see cref="SceneBoundaryRuleTests"/> settled on for its first non-node subject: population-wide
    /// for the types this repository spells as presenters, by name for everything else in the
    /// namespace.
    /// </para>
    /// </remarks>
    private const string BootAtlasResultName = "BootAtlasResult";

    /// <summary>
    /// The key→string resolver every screen reads its captions through — the second subject in the
    /// presenters namespace whose name does not end in <see cref="PresenterTypeSuffix"/>.
    /// </summary>
    /// <remarks>
    /// 🔴 It was <c>BootStringCatalogue</c> until M7-04 renamed it, and a type that has just been
    /// renamed once is exactly the one a later task renames again or files somewhere tidier. It
    /// reads the content snapshot and hands text to three presenters; out from under these rules it
    /// could reach for the engine's own translation server instead, and nothing here would notice.
    /// </remarks>
    private const string LocaleStringCatalogueName = "LocaleStringCatalogue";

    /// <summary>The scene script the negative control is stated over by name.</summary>
    internal const string AppRootSceneName = "AppRoot";

    /// <summary>The suffix this repository spells a presenter with.</summary>
    internal const string PresenterTypeSuffix = "Presenter";

    /// <summary>The engine's managed assembly.</summary>
    /// <remarks>
    /// Internal rather than private since M7-01's loop-back: <see cref="SceneBoundaryRuleTests"/>
    /// walks a base-type chain and has to know where to stop, and two spellings of "the engine's
    /// assembly" in two files is the shape steering S4 exists to prevent.
    /// </remarks>
    internal const string EngineAssemblyName = "GodotSharp";

    /// <summary>The engine's root namespace.</summary>
    private const string EngineNamespace = "Godot";

    /// <summary>
    /// The engine's name as written, for the source arm. Deliberately a bare word match rather
    /// than a type list: a presenter has no business writing it at all.
    /// </summary>
    private static readonly Regex EngineNameInSource = new(@"\bGodot\b", RegexOptions.Compiled);

    /// <summary>Every type declared under the presenters namespace of the client assembly.</summary>
    private static IReadOnlyList<TypeDefinition> Presenters { get; } =
        Il.TypesUnder(ProductionAssemblies.Module(ProductionAssemblies.ClientName), PresentersNamespace)
          .ToArray();

    /// <summary>The presenter source directory, spelled the way the filesystem spells it.</summary>
    private static string PresenterSourceDirectory { get; } =
        Path.Combine(RepoLayout.ProjectDirectory(ProductionAssemblies.ClientName), "game", "presenters");

    /// <summary>The scene source directory — where the source arm's positive control lives.</summary>
    /// <remarks>
    /// Internal rather than private since M7-01's loop-back: it is the subject directory of
    /// <see cref="SceneBoundaryRuleTests"/>'s source arm as well as the control directory of this
    /// file's, and one definition of "where the scenes are" is the point.
    /// </remarks>
    internal static string SceneSourceDirectory { get; } =
        Path.Combine(RepoLayout.ProjectDirectory(ProductionAssemblies.ClientName), "game", "scenes");

    /// <summary>
    /// `14` §5 / `23` §5 A10 — 🔒 a presenter references neither the engine nor an adapter.
    /// "Presenters are plain C# classes receiving their ports as constructor arguments, so they
    /// are unit-testable without booting Godot" is only true while nothing in that namespace
    /// reaches for either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Adapters are banned alongside the engine because a presenter naming a concrete adapter has
    /// taken a composition decision away from the composition root, which is the other half of
    /// the same rule: `23` §7 says the root is the only place concrete types are named.
    /// </para>
    /// <para>
    /// 🔒 This arm and the source arm below are complementary, not redundant. See the source
    /// arm's remarks for the shape this one cannot see.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_presenter_references_neither_the_engine_nor_an_adapter()
    {
        var offenders = Presenters.SelectMany(EngineOrAdapterReferences);

        ArchRule.Empty(
            offenders,
            "A presenter references neither the engine nor an adapter — IL scan (14 §5, 23 §5 A10).");
    }

    /// <summary>
    /// `14` §5 — 🔒 no presenter source file names the engine, checked as text with comments and
    /// string literals stripped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Not a duplicate of the IL arm.</b> A <c>const</c> is inlined at its use site, so
    /// <c>Godot.Mathf.Pi</c> or an engine <c>const string</c> leaves no reference in the compiled
    /// presenter at all — the IL arm sees nothing to object to and passes. The same is true of a
    /// <c>#if</c>-excluded branch, which never reaches the assembly. Text is the only mechanism
    /// that catches those; IL is the only mechanism that catches an aliased or fully-qualified
    /// reference the text arm would have to guess at. Each covers the other's blind spot, and
    /// deleting either leaves a real hole rather than tidying a duplicate.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_presenter_source_file_never_names_the_engine()
    {
        var offenders =
            from file in RepoLayout.SourceFiles(PresenterSourceDirectory)
            let source = SourceText.Read(file)
            from hit in source.Hits(EngineNameInSource)
            select $"{hit}  [a presenter must run with no engine loaded]";

        ArchRule.Empty(
            offenders,
            "No presenter source file names the engine — source grep (14 §5).");
    }

    /// <summary>
    /// `23` §6 — the floor under the two rules above, stated by NAMED MEMBER rather than by a
    /// count: <c>AppRootPresenter</c> is among the scanned subjects, in the assembly and on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both rules are "no member of set S does X", and both are written to pass over an empty S.
    /// S is a namespace filter and a directory path, so renaming either — <c>game/presenters</c>
    /// to <c>Presenters</c>, or the namespace to match the folder — empties it and takes both
    /// rules permanently green with it, with the presenters still sitting there naming whatever
    /// they like.
    /// </para>
    /// <para>
    /// 🔒 A count would not do. "At least one type" is satisfied by an enum, a record or a
    /// leftover helper long after every actual presenter has moved somewhere the rules do not
    /// look. Naming the member that must be there is what ties the floor to the thing being
    /// governed.
    /// </para>
    /// <para>
    /// 🔒 And naming ONE member is not enough on its own, which is the hole the third assertion
    /// closes. <c>AppRootPresenter</c> staying put says nothing about the presenters written
    /// after it: a later screen dropping <c>InventoryPresenter</c> into a namespace of its own
    /// leaves this floor satisfied, both rules above quantifying over a set that never grows,
    /// and the new presenter free to name the engine. The subject set is a namespace, so the
    /// floor has to be stated over the whole population — every type this repository spells as
    /// a presenter is inside it — rather than over the one that happened to be first.
    /// </para>
    /// <para>
    /// 🔒 <b>And "spelled as a presenter" is not all of the population.</b> The namespace holds two
    /// types whose names do not end in <see cref="PresenterTypeSuffix"/> — see
    /// <see cref="BootAtlasResultName"/> and <see cref="LocaleStringCatalogueName"/> — and the stray
    /// arm's own predicate cannot see either of them leave. Both are therefore named here, in both
    /// arms, because the type staying in the namespace and its file staying in the directory are
    /// two separate facts and each rule depends on a different one. A second such type owes itself
    /// the same pair of lines.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_presenter_subject_set_contains_AppRootPresenter()
    {
        var presenterTypeNames = Presenters.Select(type => type.Name).ToArray();

        presenterTypeNames.ShouldContain(
            AppRootPresenterName,
            $"'{AppRootPresenterName}' is not among the types under {PresentersNamespace}, so the IL arm " +
            "is quantifying over a set that no longer holds the presenters. Either the namespace moved " +
            "or the presenter did; point the rule at wherever they went rather than leaving it green.");

        presenterTypeNames.ShouldContain(
            BootAtlasResultName,
            $"'{BootAtlasResultName}' is not among the types under {PresentersNamespace}. Its name does not " +
            "end in '" + PresenterTypeSuffix + "', so the stray arm below cannot notice it has gone: it " +
            "would simply stop being scanned, in a namespace of its own, free to name the engine or an " +
            "adapter. Either it moved and must move back, or it was renamed — in which case rename it here " +
            "rather than deleting the assertion.");

        presenterTypeNames.ShouldContain(
            LocaleStringCatalogueName,
            $"'{LocaleStringCatalogueName}' is not among the types under {PresentersNamespace}, and the stray " +
            "arm cannot see it leave either. It is the resolver every screen reads its strings through, so " +
            "outside these rules it is one edit away from asking the engine for them.");

        var presenterFileNames = RepoLayout.SourceFiles(PresenterSourceDirectory)
                                           .Select(Path.GetFileNameWithoutExtension)
                                           .ToArray();

        presenterFileNames.ShouldContain(
            AppRootPresenterName,
            $"no '{AppRootPresenterName}.cs' under {RepoLayout.Relative(PresenterSourceDirectory)}, so " +
            "the source arm is grepping a directory the presenters have left. That arm is the only " +
            "one that can see an inlined const, and a grep over the wrong directory sees nothing.");

        presenterFileNames.ShouldContain(
            BootAtlasResultName,
            $"no '{BootAtlasResultName}.cs' under {RepoLayout.Relative(PresenterSourceDirectory)}. The type " +
            "may still be in the presenters namespace while its FILE has left the directory the source arm " +
            "greps — and that arm is the only one that can see an inlined const, so the type would keep half " +
            "its governance and lose the other half silently.");

        presenterFileNames.ShouldContain(
            LocaleStringCatalogueName,
            $"no '{LocaleStringCatalogueName}.cs' under {RepoLayout.Relative(PresenterSourceDirectory)}, for " +
            "the same reason: the namespace and the directory are pinned separately because a move can break " +
            "either one alone, and the source arm greps the directory.");

        var strays =
            from type in Il.AllTypes(ProductionAssemblies.Module(ProductionAssemblies.ClientName))
            where type.Name.EndsWith(PresenterTypeSuffix, StringComparison.Ordinal)
            where !Il.IsUnder(Il.NamespaceOf(type), PresentersNamespace)
            select $"{type.FullName} is spelled as a presenter and lives outside {PresentersNamespace}, " +
                   "where neither the IL arm nor the source arm looks — so nothing stops it naming the " +
                   "engine or an adapter. Move it under the presenters namespace and its file under " +
                   $"{RepoLayout.Relative(PresenterSourceDirectory)}, or stop calling it a presenter";

        ArchRule.Empty(
            strays,
            "Every presenter in the client assembly is inside the governed namespace (23 §6).");
    }

    /// <summary>
    /// `14` §5 — the negative control: a scene script names the engine on purpose, and trips
    /// neither presenter rule. Scenes are driving adapters; that is the whole of the split.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, both rules above could be passing because nothing anywhere in the client
    /// names the engine — which would make them true and worthless. The scene proves the
    /// detector fires on a real engine reference, and that the presenter rules are scoped to the
    /// half of the split that is supposed to be clean.
    /// </para>
    /// <para>
    /// 🔒 BOTH detectors are exercised here, not just the IL one. The source arm's detector is a
    /// regex over text that <c>SourceText</c> has already blanked comments and string literals
    /// out of, and a blanking bug, a changed pattern or a directory that no longer holds C# would
    /// each leave it matching nothing — with its rule reporting success over every presenter,
    /// forever. Running the same pattern over the file that is <em>supposed</em> to match is the
    /// only thing that distinguishes "no presenter names the engine" from "this grep cannot see
    /// the engine at all".
    /// </para>
    /// </remarks>
    [Fact]
    public void A_scene_script_names_the_engine_and_trips_neither_presenter_rule()
    {
        var scene = Il.TypesUnder(ProductionAssemblies.Module(ProductionAssemblies.ClientName), ScenesNamespace)
                      .FirstOrDefault(type => type.Name.Equals(AppRootSceneName, StringComparison.Ordinal));

        scene.ShouldNotBeNull(
            $"no '{AppRootSceneName}' under {ScenesNamespace}. The control is what shows the engine detector " +
            "fires at all; without it the two presenter rules could be green because the client names no " +
            "engine type anywhere, which is a different claim entirely.");

        EngineOrAdapterReferences(scene).ShouldNotBeEmpty(
            $"'{AppRootSceneName}' names no engine type, so it is not the control it is here to be. Either it " +
            "stopped being a scene script, or the detector has stopped recognising the engine — and in the " +
            "second case both presenter rules are passing over a predicate that never matches.");

        EngineHitsInScenes().ShouldNotBeEmpty(
            $"the source pattern matches nothing under {RepoLayout.Relative(SceneSourceDirectory)}, where a " +
            "script that writes 'Godot' at the top of the file lives. The pattern, the comment/literal " +
            "blanking or the directory has moved out from under the source arm, and that arm is now grepping " +
            "for something it can never find — which is indistinguishable, in its result, from every " +
            "presenter being clean.");

        Presenters.Select(type => type.FullName).ShouldNotContain(
            scene.FullName,
            "the scene has ended up inside the presenters' subject set, which would make the IL arm red for " +
            "a type that is supposed to name the engine. The split is by namespace; putting a Node subclass " +
            "in the presenters namespace erases it.");
    }

    /// <summary>
    /// `23` §7 — the other half of the IL arm's claim: the adapter detector fires, proven on the
    /// composition root, which is the one place `23` §7 lets a concrete adapter be named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <c>A_presenter_references_neither_the_engine_nor_an_adapter</c> is a disjunction, and
    /// the negative control above only ever demonstrates the engine half of it. If
    /// <c>ProductionAssemblies.IsAdapter</c> or the metadata scope lookup behind it stopped
    /// recognising an adapter, that rule would keep passing under a name promising twice what it
    /// still delivered, and a presenter could construct <c>AutoGrantRewardedAd</c> unnoticed.
    /// </para>
    /// <para>
    /// Stated over the whole composition namespace rather than one type, so it survives the root
    /// being reorganised — but not the root ceasing to name an adapter at all, which would mean
    /// the composition root had stopped composing.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_adapter_half_of_the_detector_fires_on_the_composition_root()
    {
        var compositionTypes =
            Il.TypesUnder(ProductionAssemblies.Module(ProductionAssemblies.ClientName), CompositionNamespace)
              .ToArray();

        compositionTypes.ShouldNotBeEmpty(
            $"no type under {CompositionNamespace}, so this control quantifies over nothing and the adapter " +
            "half of the detector is proven by no subject at all.");

        compositionTypes.SelectMany(EngineOrAdapterReferences).ShouldContain(
            offender => offender.Contains(ProductionAssemblies.AdapterPrefix, StringComparison.Ordinal),
            "the composition root names no adapter that the detector can see. Only the adapter arm of " +
            "IsEngineOrAdapter can put an assembly under " + ProductionAssemblies.AdapterPrefix + " into this " +
            "list — the engine arms match GodotSharp and the Godot namespace, and an adapter is neither. So " +
            "either the root has stopped wiring concrete adapters, or the arm that is supposed to catch a " +
            "presenter reaching for one has gone blind.");
    }

    /// <summary>Source-arm hits on the scene scripts — the positive control for the text detector.</summary>
    private static IEnumerable<string> EngineHitsInScenes() =>
        from file in RepoLayout.SourceFiles(SceneSourceDirectory)
        let source = SourceText.Read(file)
        from hit in source.Hits(EngineNameInSource)
        select hit;

    /// <summary>Engine and adapter references a type carries, described for a failure message.</summary>
    private static IEnumerable<string> EngineOrAdapterReferences(TypeDefinition type) =>
        Il.ReferencedTypeReferences(type)
          .Where(IsEngineOrAdapter)
          .Select(reference =>
              $"{type.FullName} names {reference.FullName} from {Describe(reference)} — a presenter is plain " +
              "C# that runs with no engine and knows no concrete adapter (14 §5, 23 §5 A10)")
          .Distinct(StringComparer.Ordinal);

    /// <summary>True for a reference into the engine assembly, the engine namespace, or any adapter.</summary>
    private static bool IsEngineOrAdapter(TypeReference reference)
    {
        var assembly = Il.AssemblyNameOf(reference);

        return assembly.Equals(EngineAssemblyName, StringComparison.Ordinal)
            || ProductionAssemblies.IsAdapter(assembly)
            || Il.IsUnder(reference.Namespace ?? string.Empty, EngineNamespace);
    }

    private static string Describe(TypeReference reference)
    {
        var assembly = Il.AssemblyNameOf(reference);
        return assembly.Length > 0 ? assembly : "an unresolved scope";
    }
}
