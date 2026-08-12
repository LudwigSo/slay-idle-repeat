using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `30` §11 — the accessibility boundary. The only public way to change state in this
/// game is <c>GameRules.Apply</c>; everything else the outside world can see is a getter.
/// </summary>
public sealed class AccessibilityBoundaryTests
{
    /// <summary>
    /// `30` §11.2 — `Apply` is the only public mutation: no public setter, public mutating
    /// method or public constructor on any type under `Core/Model/`. The persistence DTOs
    /// under `Model/Snapshots/` are exempt by `30` §11.3 — an adapter must be able to build
    /// a `PlayerSnapshot` to call `Rehydrate`.
    /// </summary>
    [Fact]
    public void Apply_is_the_only_public_mutation()
    {
        var offenders = new List<string>();

        foreach (var type in AggregateTypes())
        {
            offenders.AddRange(
                type.Methods
                    .Where(m => m.IsConstructor && m.IsPublic && !m.IsStatic)
                    .Select(m => $"{type.FullName} has a public constructor — aggregates are constructed only inside Core"));

            offenders.AddRange(
                type.Properties
                    .Where(p => p.SetMethod is { IsPublic: true } && !IsInitOnly(p.SetMethod))
                    .Select(p => $"{type.FullName}.{p.Name} has a public setter"));

            offenders.AddRange(
                type.Fields
                    .Where(f => f.IsPublic && !f.IsInitOnly && !f.IsLiteral && !Domain.IsCompilerGenerated(f))
                    .Select(f => $"{Il.Describe(f)} is a public mutable field"));

            offenders.AddRange(
                type.Methods
                    .Where(IsPublicMutator)
                    .Select(m => $"{Il.Describe(m)} is a public method that writes to the aggregate's own state"));
        }

        // The single documented public mutation must itself be public and static (30 §11.2).
        //
        // Every overload, not the first one: a `public CommandResult Apply(WorldSlice,
        // GameCommand)` convenience overload added for tests would have slipped past a
        // FirstOrDefault entirely. And GameRules existing WITHOUT an Apply is a violation in
        // its own right — a rule named Apply_is_the_only_public_mutation that stays silent
        // when Apply is renamed to Handle promises an invariant it is no longer checking,
        // and so does `30` §11.2.
        var gameRules = Domain.FindInCore(Domain.GameRulesType);
        if (gameRules is not null)
        {
            var applies = gameRules.Methods
                .Where(m => m.Name.Equals(Domain.ApplyMethod, StringComparison.Ordinal))
                .ToArray();

            if (applies.Length == 0)
            {
                offenders.Add(
                    $"{gameRules.FullName} declares no method named '{Domain.ApplyMethod}' — the single mutation " +
                    "entry point of 30 §11.2 either was renamed or never existed, and this rule was about to " +
                    "report success over its absence");
            }

            offenders.AddRange(
                applies
                    .Where(apply => !(apply.IsPublic && apply.IsStatic))
                    .Select(apply => $"{Il.Describe(apply)} must be public static — it is the single mutation entry point"));
        }

        ArchRule.Empty(
            offenders,
            "GameRules.Apply is the only public mutation — Core/Model/ exposes getters only (30 §11.2).");
    }

    /// <summary>
    /// `30` §11.2 — handlers and rules are `internal`, with exactly two documented
    /// exceptions: `CombatSimulator` (the client's local battle simulation, `14` §2.4) and
    /// `PowerCalculator` (the Hero screen, `29` §1).
    /// </summary>
    [Fact]
    public void Handlers_and_Rules_are_internal()
    {
        var offenders = Domain.CoreTypes
            .Where(t => t.DeclaringType is null)
            .Where(t => Il.IsUnder(Il.NamespaceOf(t), Domain.HandlersNamespace) ||
                        Il.IsUnder(Il.NamespaceOf(t), Domain.RulesNamespace))
            .Where(t => t.IsPublic && !Domain.IsCompilerGenerated(t))
            .Where(t => !Domain.PublicRuleTypes.Contains(t.Name, StringComparer.Ordinal))
            .Select(t => $"{t.FullName} is public — only {string.Join(" and ", Domain.PublicRuleTypes)} may be");

        ArchRule.Empty(
            offenders,
            "Handlers and Rules are internal, except CombatSimulator and PowerCalculator (30 §11.2).");
    }

    /// <summary>
    /// `30` §11.4 — the internal layering holds: Handlers ▶ Rules ▶ Model ▶ Content ▶
    /// Primitives. `Rules` never references `Handlers`; `Model` never references `Rules`.
    /// `Rng` is pure arithmetic (`14` §8.1) and sits below `Model` with `Content`. `Primitives`,
    /// `Content`, `Rng` and `Events` never reach up into the `SlayIdleRepeat.Core` root.
    /// </summary>
    [Fact]
    public void Core_internal_layering_holds()
    {
        // Each layer, with the layers it must never reference — everything above it.
        var forbidden = new (string Layer, string[] MustNotReference)[]
        {
            (Domain.PrimitivesNamespace, new[] { Domain.ContentNamespace, Domain.RngNamespace, Domain.ModelNamespace, Domain.RulesNamespace, Domain.HandlersNamespace }),
            (Domain.ContentNamespace, new[] { Domain.ModelNamespace, Domain.RulesNamespace, Domain.HandlersNamespace }),
            (Domain.RngNamespace, new[] { Domain.ContentNamespace, Domain.ModelNamespace, Domain.RulesNamespace, Domain.HandlersNamespace }),
            (Domain.ModelNamespace, new[] { Domain.RulesNamespace, Domain.HandlersNamespace }),
            (Domain.RulesNamespace, new[] { Domain.HandlersNamespace }),
        };

        var offenders = new List<string>();

        foreach (var (layer, mustNotReference) in forbidden)
        {
            foreach (var type in Domain.CoreTypesUnder(layer))
            {
                foreach (var referenced in Il.ReferencedTypeNames(type))
                {
                    var violated = mustNotReference.FirstOrDefault(
                        upper => referenced.StartsWith(upper + ".", StringComparison.Ordinal));

                    if (violated is not null)
                    {
                        offenders.Add($"{type.FullName} (in {layer}) references {referenced} (in {violated})");
                    }
                }
            }
        }

        // The `SlayIdleRepeat.Core` ROOT has no row in the table above, and until M1-07 it held no
        // types at all — so the moment `GameRules` (M1-06) and `GameContext` (M1-07) landed there,
        // the root became a region the layering rule matched in neither direction. One of those
        // directions is legitimate: the root sits at the TOP of the layering and reaches down into
        // Handlers, Rules, Model and Content by design. The reverse is not — a bottom layer that
        // names `GameContext` inverts the whole chain with this rule green.
        //
        // ⚠️ Matched EXACTLY, never by prefix. A `StartsWith("SlayIdleRepeat.Core.")` row would
        // match every type in the assembly and make the rule above trivially true — the same trap
        // Domain.IsPermittedCoreNamespace documents for the permitted-namespace list.
        // ⚠️ `Events` is here and in NO row of the table above, and that asymmetry is deliberate.
        // 30 §11.4's chain omits Commands and Events entirely, while 30 §7 writes
        // GearGranted(int, GearInstance, SourceClass, bool) — GearInstance being a Model aggregate.
        // So a row forbidding Events -> Model would contradict 30 §7 and block M4-03, and it is not
        // written on a guess; the ruling is owned by M1-06's brief and due at the M4 kickoff (see
        // SubjectSetFloorTests' Events row). What is NOT in doubt in either reading is the
        // direction below: Apply produces events, so an event naming GameRules or GameContext is a
        // cycle, and this row can fire today — GameContext, Entitlements and FeatureFlags are all
        // in the root already.
        var mustNotReachTheRoot = new[]
        {
            Domain.PrimitivesNamespace,
            Domain.ContentNamespace,
            Domain.RngNamespace,
            Domain.EventsNamespace,
        };

        foreach (var layer in mustNotReachTheRoot)
        {
            foreach (var type in Domain.CoreTypesUnder(layer))
            {
                offenders.AddRange(
                    Il.ReferencedTypeNames(type)
                        .Where(IsCoreRootType)
                        .Select(referenced =>
                            $"{type.FullName} (in {layer}) references {referenced}, which is in the " +
                            $"{Domain.CoreNamespace} root. The root holds GameRules and GameContext, the top of " +
                            "the layering, so a layer beneath it reaching up inverts Handlers -> Rules -> Model " +
                            "-> Content -> Primitives (30 §11.4). For Events specifically: Apply PRODUCES the " +
                            "event list, so an event naming GameRules or GameContext is a cycle, and a timestamp " +
                            "reached through GameContext.NowUtc is the clock 30 §3 keeps out of the domain."));
            }
        }

        ArchRule.Empty(
            offenders,
            "Core's internal layering holds: Handlers -> Rules -> Model -> Content -> Primitives, and " +
            "Primitives, Content, Rng and Events never reach up into the SlayIdleRepeat.Core root (30 §11.4).");
    }

    /// <summary>
    /// True for a type declared directly in the <c>SlayIdleRepeat.Core</c> root — <c>GameContext</c>,
    /// not <c>Content.ContentSnapshot</c>. Nested types are attributed to their outermost declaring
    /// type, which is how Cecil spells them (<c>Namespace.Outer/Nested</c>).
    /// </summary>
    private static bool IsCoreRootType(string typeFullName)
    {
        if (!typeFullName.StartsWith(Domain.CoreNamespace + ".", StringComparison.Ordinal))
        {
            return false;
        }

        var outerName = typeFullName[(Domain.CoreNamespace.Length + 1)..].Split('/')[0];

        return !outerName.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>
    /// `30` §11.3 — 🔒 `InternalsVisibleTo` names exactly one assembly,
    /// `SlayIdleRepeat.Core.Tests`. Aggregates are rehydrated through the public
    /// `ToSnapshot()`/`Rehydrate()` pair, never by opening the assembly to an adapter.
    /// </summary>
    /// <remarks>
    /// EXACTLY one, not "none that are wrong". Written as a filter over the grants, the rule
    /// passed just as happily when `Core` granted internals to nobody — and `Core.Tests`
    /// reaches `Hash64` and `CanonicalStateWriter` through that grant (`14` §16.6), so
    /// losing it would break real tests while this rule, whose name promises to be watching
    /// the grant, said nothing.
    /// </remarks>
    [Fact]
    public void InternalsVisibleTo_names_only_the_Core_test_assembly()
    {
        var granted = InternalsVisibleTo(ProductionAssemblies.CoreModule)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var offenders = granted
            .Where(name => !name.Equals(ProductionAssemblies.CoreTestsName, StringComparison.Ordinal))
            .Select(name => $"SlayIdleRepeat.Core opens its internals to '{name}'")
            .ToList();

        if (!granted.Contains(ProductionAssemblies.CoreTestsName, StringComparer.Ordinal))
        {
            offenders.Add(
                $"SlayIdleRepeat.Core grants InternalsVisibleTo to [{string.Join(", ", granted)}] — " +
                $"'{ProductionAssemblies.CoreTestsName}' is not among them. 30 §11.3 sanctions exactly that one " +
                "grant, and the domain suite reaches Hash64 and CanonicalStateWriter through it.");
        }

        ArchRule.Empty(
            offenders,
            $"InternalsVisibleTo on SlayIdleRepeat.Core names exactly {ProductionAssemblies.CoreTestsName} (30 §11.3).");
    }

    /// <summary>
    /// `30` §11.4 — every type in `Core` lives under one of the namespaces the document
    /// enumerates: `Primitives`, `Content`, `Rng`, `Model`, `Rules`, `Commands`, `Events`,
    /// `Handlers`, `Testing`, or the `SlayIdleRepeat.Core` root that holds `GameRules`.
    /// </summary>
    /// <remarks>
    /// <c>Core_internal_layering_holds</c> works from a fixed five-row table of forbidden
    /// pairs, so a type under a namespace that appears in no row is matched by nothing at
    /// all — not permitted, not forbidden, simply ungoverned, with the layering rule still
    /// green. (This is the hole M0-07 reasoned about when it placed
    /// <c>CanonicalStateWriter</c> under <c>Model/Snapshots/</c>; the judgement was right and
    /// the hole stayed open.) Vacuously true today, which is the point: it costs nothing now
    /// and makes the next <c>Core/Foo/</c> a build failure rather than a silent new region.
    /// </remarks>
    [Fact]
    public void Every_Core_type_lives_under_a_documented_namespace()
    {
        var offenders = Domain.CoreTypes
            .Where(t => !Domain.IsCompilerGenerated(t))
            .Select(t => (Type: t, Namespace: Il.NamespaceOf(t)))
            .Where(x => !Domain.IsPermittedCoreNamespace(x.Namespace))
            .Select(x =>
                $"{x.Type.FullName} is in namespace '{x.Namespace}', which 30 §11.4 does not enumerate. " +
                $"Permitted: {string.Join(", ", Domain.PermittedCoreNamespaces)}. Core_internal_layering_holds " +
                "has no row for it, so nothing governs what it may reference.");

        ArchRule.Empty(
            offenders,
            "Every type in SlayIdleRepeat.Core lives under a namespace 30 §11.4 enumerates (30 §11.4).");
    }

    /// <summary>
    /// `30` §11.6 — 🔒 `Contracts` must never re-declare a command, an event or a domain
    /// type. There is one vocabulary (`14` §2.3); a parallel DTO hierarchy is where
    /// mapping fatigue starts.
    /// </summary>
    [Fact]
    public void Contracts_never_redeclares_a_domain_type()
    {
        var coreNames = Domain.CoreTypes
            .Where(t => !Domain.IsCompilerGenerated(t))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = Il.AllTypes(ProductionAssemblies.Module(ProductionAssemblies.ContractsName))
            .Where(t => !Domain.IsCompilerGenerated(t))
            .Where(t => coreNames.Contains(t.Name) ||
                        Domain.DerivesFrom(t, Domain.GameCommandType) ||
                        Domain.DerivesFrom(t, Domain.DomainEventType))
            .Select(t => $"{t.FullName} re-declares a Core domain type");

        ArchRule.Empty(
            offenders,
            "Contracts re-declares no command, event or domain type — it is wire envelopes only (30 §11.6).");
    }

    /// <summary>Public types under `Core/Model/`, excluding the persistence DTOs of `Model/Snapshots/`.</summary>
    private static IEnumerable<TypeDefinition> AggregateTypes() =>
        Domain.CoreTypesUnder(Domain.ModelNamespace)
              .Where(t => !Il.IsUnder(Il.NamespaceOf(t), Domain.SnapshotsNamespace))
              .Where(t => (t.IsPublic || t.IsNestedPublic) && !Domain.IsCompilerGenerated(t));

    /// <summary>An <c>init</c> accessor is a construction-time setter, not a mutation surface.</summary>
    /// <remarks>
    /// Delegates to <see cref="Il.IsInitOnlySetter"/> rather than repeating the modifier check:
    /// <c>DomainPurityTests</c> asks the same question of the same metadata, and two spellings of
    /// it would eventually disagree about a record (steering S4).
    /// </remarks>
    private static bool IsInitOnly(MethodDefinition setter) => Il.IsInitOnlySetter(setter);

    /// <summary>
    /// A public method that writes an instance or static field of the type it is declared
    /// on. Property accessors, constructors and compiler-generated record plumbing are not
    /// the author's mutation surface and are excluded.
    /// </summary>
    private static bool IsPublicMutator(MethodDefinition method)
    {
        if (!method.IsPublic || method.IsConstructor || method.IsGetter || method.IsSetter ||
            method.IsAddOn || method.IsRemoveOn || Domain.IsCompilerGenerated(method))
        {
            return false;
        }

        var declaring = method.DeclaringType.FullName;
        return Il.Instructions(method)
                 .Where(i => i.OpCode == OpCodes.Stfld || i.OpCode == OpCodes.Stsfld)
                 .Select(i => (i.Operand as FieldReference)?.DeclaringType?.FullName)
                 .Any(owner => owner is not null && owner.Equals(declaring, StringComparison.Ordinal));
    }

    private static IEnumerable<string> InternalsVisibleTo(ModuleDefinition module)
    {
        var assembly = module.Assembly;
        if (assembly is null)
        {
            yield break;
        }

        foreach (var attribute in assembly.CustomAttributes)
        {
            if (attribute.AttributeType.FullName !=
                "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
            {
                continue;
            }

            var argument = attribute.ConstructorArguments.FirstOrDefault().Value as string ?? string.Empty;
            yield return argument.Split(',')[0].Trim();
        }
    }
}
