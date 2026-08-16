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

    /// <summary>The presenter the floor is stated over by name.</summary>
    internal const string AppRootPresenterName = "AppRootPresenter";

    /// <summary>The scene script the negative control is stated over by name.</summary>
    internal const string AppRootSceneName = "AppRoot";

    /// <summary>The engine's managed assembly.</summary>
    private const string EngineAssemblyName = "GodotSharp";

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
    /// </remarks>
    [Fact]
    public void The_presenter_subject_set_contains_AppRootPresenter()
    {
        Presenters.Select(type => type.Name).ShouldContain(
            AppRootPresenterName,
            $"'{AppRootPresenterName}' is not among the types under {PresentersNamespace}, so the IL arm " +
            "is quantifying over a set that no longer holds the presenters. Either the namespace moved " +
            "or the presenter did; point the rule at wherever they went rather than leaving it green.");

        RepoLayout.SourceFiles(PresenterSourceDirectory)
                  .Select(Path.GetFileNameWithoutExtension)
                  .ShouldContain(
                      AppRootPresenterName,
                      $"no '{AppRootPresenterName}.cs' under {RepoLayout.Relative(PresenterSourceDirectory)}, so " +
                      "the source arm is grepping a directory the presenters have left. That arm is the only " +
                      "one that can see an inlined const, and a grep over the wrong directory sees nothing.");
    }

    /// <summary>
    /// `14` §5 — the negative control: a scene script names the engine on purpose, and trips
    /// neither presenter rule. Scenes are driving adapters; that is the whole of the split.
    /// </summary>
    /// <remarks>
    /// Without this, both rules above could be passing because nothing anywhere in the client
    /// names the engine — which would make them true and worthless. The scene proves the
    /// detector fires on a real engine reference, and that the presenter rules are scoped to the
    /// half of the split that is supposed to be clean.
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

        Presenters.Select(type => type.FullName).ShouldNotContain(
            scene.FullName,
            "the scene has ended up inside the presenters' subject set, which would make the IL arm red for " +
            "a type that is supposed to name the engine. The split is by namespace; putting a Node subclass " +
            "in the presenters namespace erases it.");
    }

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
