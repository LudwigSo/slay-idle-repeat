using System.Collections.ObjectModel;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;
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
    /// <para>
    /// ⚠️ <b>`Core/Testing/` is the second documented exemption, and it is outside this rule's
    /// subject set rather than exempted by it</b> — written down here because the rule's name is
    /// wider than its scope. `30` §6's `InMemoryGame` ships in the production assembly with public
    /// `CreatePlayer`, `Send` and `Clock.Advance`, all of which change state; that is exactly what
    /// §6 asks for, since the harness IS the sanctioned public driver. What keeps it honest is not
    /// this rule but `Core_internal_layering_holds`' `Testing` row (it may not name `Rules` or
    /// `Handlers`) plus the harness declaring no door onto an aggregate's `internal` mutators — see
    /// `InMemoryGame.State`'s own remarks for the one route that remains open.
    /// </para>
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

            // 🔒 The fifth surface, added in M1-05's architecture review. The four above read
            // setters, fields, constructors and mutating methods — none of which sees a MUTABLE
            // COLLECTION handed out through a read-only-looking view. `Player.WalletCurrencies`
            // records the hole in its own remarks: a bare array behind an IReadOnlyList<T> casts
            // straight back to T[], so a caller rewrites the aggregate's state through a getter
            // and every rule in this file stays green. Measured on this branch before the check
            // existed: a `public IReadOnlyList<int> P => _array;` added to `Run` passed 54/54.
            offenders.AddRange(ExposedMutableCollections(type));
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
    /// `30` §11.4 — the internal layering holds: Handlers ▶ Rules ▶ Model ▶ Content ▶ Primitives.
    /// `Rules` never references `Handlers`; `Model` never references `Rules`. `Rng` is pure
    /// arithmetic (`14` §8.1) and sits below `Model` with `Content`. `Commands` sits above
    /// `Rng`/`Content`/`Primitives` and below `Handlers`. `Primitives`, `Content`, `Rng`, `Events`,
    /// `Commands` and `Model` never reach up into the `SlayIdleRepeat.Core` root.
    /// <para>
    /// ⚠️ **`Testing` sits above `Handlers`, and that position is an inference rather than a
    /// quotation.** `30` §11.4's chain is written `Handlers ▶ Rules ▶ Model ▶ Content ▶ Primitives`
    /// and omits `Testing` exactly as it omits `Commands` and `Events`. Its place comes from `30` §6
    /// (the harness drives the game) and `30` §11.2 (`GameRules.Apply` is the only public mutation),
    /// which together settle two directions: `Testing` names neither `Rules` nor `Handlers`, and
    /// nothing beneath it — the `SlayIdleRepeat.Core` root included — names `Testing`. The rows
    /// themselves record what that does and does not close.
    /// </para>
    /// </summary>
    [Fact]
    public void Core_internal_layering_holds()
    {
        // Each layer, with the layers it must never reference — everything above it.
        var forbidden = new (string Layer, string[] MustNotReference)[]
        {
            // 🔒 Commands appears in the three rows below as well as owning one of its own, and the
            // symmetry is the point: 30 §11.4's tree puts Commands/ above Rng/, Content/ and
            // Primitives/, so those three may not name a command either. Added in M1-06's
            // architecture review — the Commands row landed one-directional, which left
            // `Primitives.RunId naming Commands.GameCommand` matched by no row in either direction,
            // exactly the ungoverned region the row was written to close. Measured on this branch:
            // a `GameCommand`-typed member added to a Primitives type passed 58/58 before this.
            // 🔒 Testing appears in every row below as well as owning one of its own, added by M1-11
            // on the symmetry M1-06's Commands row established. `30` §11.4 puts Core/Testing/ INSIDE
            // the production assembly, so the harness is a real layer rather than a test project —
            // and it sits at the very top, above Handlers: it drives the domain through
            // GameRules.Apply and every other layer is beneath it. A production type naming the test
            // harness is therefore a cycle under every reading, which is the settled direction; the
            // row of its own below is the other half, and the root's own loop after the table is the
            // third (the root has no Layer row, so it needed a check rather than an entry).
            (Domain.PrimitivesNamespace, new[] { Domain.ContentNamespace, Domain.RngNamespace, Domain.ModelNamespace, Domain.RulesNamespace, Domain.CommandsNamespace, Domain.HandlersNamespace, Domain.TestingNamespace }),
            (Domain.ContentNamespace, new[] { Domain.ModelNamespace, Domain.RulesNamespace, Domain.CommandsNamespace, Domain.HandlersNamespace, Domain.TestingNamespace }),
            (Domain.RngNamespace, new[] { Domain.ContentNamespace, Domain.ModelNamespace, Domain.RulesNamespace, Domain.CommandsNamespace, Domain.HandlersNamespace, Domain.TestingNamespace }),

            // ⚠️ Model and Rules deliberately carry NO Commands entry, and that is the open half
            // rather than an oversight. A handler consumes a command and reads the model, so
            // Handlers -> Commands is required; whether a Rules calculator or an aggregate may name
            // one is not settled by any document, and forbidding it on a guess would block a task
            // rather than protect one. The Commands row below forbids the direction that IS settled.
            (Domain.ModelNamespace, new[] { Domain.RulesNamespace, Domain.HandlersNamespace, Domain.TestingNamespace }),
            (Domain.RulesNamespace, new[] { Domain.HandlersNamespace, Domain.TestingNamespace }),
            (Domain.HandlersNamespace, new[] { Domain.TestingNamespace }),

            // 🔒 M1-06's first cut at carried-forward item 8. 30 §11.4's chain — "Handlers -> Rules
            // -> Model -> Content -> Primitives" — omits Commands and Events entirely, so both were
            // ungoverned regions: a type under either was matched by no row in either direction,
            // with this rule green. M1-03 closed the unambiguous half for Events; this closes it for
            // Commands, and Commands is the easier of the two because nothing in the design set puts
            // an aggregate inside a command.
            //
            // WHAT THIS ROW SAYS: a command may name Primitives, Content and Rng — ids, indices,
            // content references, the vocabulary of 14 §2.3's parameter columns — and may not name
            // Model, Rules or Handlers.
            //
            // WHY IT IS SAFE where the Events equivalent is not. 30 §7 forces Events -> Model:
            // GearGranted(int, GearInstance, SourceClass, bool) carries a Model aggregate, so a row
            // forbidding it would contradict 30 §7 and block M4-03 outright. Nothing forces the
            // command equivalent. 14 §2.3's commands carry ids and indices — a merge names gear
            // INSTANCE IDS, not GearInstances; the server owns the instance — and 30 §11.6's
            // one-vocabulary rule makes a command a wire value, which an aggregate is not. A
            // handler consumes a command and reads the model; a command naming its handler or its
            // rules would be a cycle under every reading.
            //
            // ⚠️ IF M1-02 FINDS A COMMAND THAT MUST CARRY A MODEL TYPE, that is a design finding and
            // belongs at a kickoff, not a row deleted to make a build green. The binding ruling on
            // the Events half is due at the M4 kickoff, before M4-03 authors GearGranted, and its
            // deliverable is a 30 §11.4 amendment rather than a table edit.
            (Domain.CommandsNamespace, new[] { Domain.ModelNamespace, Domain.RulesNamespace, Domain.HandlersNamespace, Domain.TestingNamespace }),

            // 🔒 M1-11. Events gets its FIRST row here, and it is deliberately a row of exactly one
            // entry. The contested half of the Events question is `Events -> Model` — 30 §7 writes
            // GearGranted(int, GearInstance, SourceClass, bool) and GearInstance is a Model
            // aggregate, so a row forbidding it would contradict 30 §7 and block M4-03; that ruling
            // is still owned by the M4 kickoff and is NOT pre-empted here. `Events -> Testing` is not
            // contested under any reading: Apply PRODUCES the event list and the harness CONSUMES
            // Apply, so an event naming InMemoryGame is a cycle. Measured before this row existed: a
            // `typeof(InMemoryGame)` field added to CurrencyChanged passed 63/63.
            (Domain.EventsNamespace, new[] { Domain.TestingNamespace }),

            // 🔒 M1-11, `30` §6 + `30` §11.2. Core/Testing/ was an ungoverned region until this
            // commit — the same shape M1-06 found for Commands and M1-03 for Events, and the same
            // discipline applies: only the direction the documents SETTLE is written.
            //
            // WHAT THIS ROW SAYS: the harness may name the SlayIdleRepeat.Core root (GameRules,
            // WorldSlice, GameContext, CommandResult — it is above them, which is why Testing is
            // deliberately NOT in mustNotReachTheRoot below), Model (it builds a Player through
            // 30 §11.3's Rehydrate), Content, Commands, Events, Rng and Primitives — and may NOT
            // name Rules or Handlers.
            //
            // WHY THAT HALF IS SETTLED. `30` §11.2: "the only public way to change state in this
            // game is GameRules.Apply", and `30` §6 makes InMemoryGame the artefact that
            // DEMONSTRATES it. Core/Testing/ lives inside the production assembly, so it can see
            // every internal in Core — a harness calling BeginSession.Handle or EnergyMath.Grant
            // would drive the domain behind Apply's back, past the clone (P4), past the catch-up,
            // past the RNG fold and past the event stamping, and every claim the harness makes about
            // "the rules decided this" would be a claim about the harness instead. Nothing else
            // catches that: Handlers_and_Rules_are_internal is about ACCESSIBILITY, and internal is
            // exactly what those types are TO this namespace.
            //
            // 🔴 WHAT THIS ROW DOES NOT CLOSE, and an earlier draft of this comment denied it. The
            // row permits `Testing -> Model`, and it MUST: 30 §11.3 makes Player.Rehydrate the one
            // validated construction path and CreatePlayer has to call it. But Player's mutators are
            // `internal`, and Core/Testing/ is inside the assembly — so a harness calling
            // player.AccrueEnergy(...), player.MoveCurrency(...) or player.MarkApplied(...) bypasses
            // Apply just as completely as calling BeginSession.Handle would, and this row does not
            // see it. What this row closes is the half that is NAMESPACE-DECIDABLE; the rest rests on
            // InMemoryGame declaring no such door (it declares none — no Restore, no setter, no
            // internal mutator call) and on review. The one mechanical backstop that does reach it is
            // DomainPurityTests.A_currency_event_is_never_discarded_at_its_call_site, which sees a
            // CurrencyChanged dropped by a caller in Testing/ like any other.
            //
            // ⚠️ AND A KNOWN TENSION WITH 30 §11.2, which is why the `Rules` half is stated as
            // settled-for-now rather than settled. §11.2 makes CombatSimulator public precisely
            // because "the balance harness calls it directly (05 §9)", and 30 §6 makes that balance
            // harness a thin wrapper over InMemoryGame. Today the two are different things — the
            // harness is a tools/ project outside Core, so this row cannot reach it — but a future
            // Core/Testing/ type that wanted CombatSimulator would be doing something 30 §11.2
            // explicitly sanctions and this row forbids wholesale. OWNER: the M6 kickoff, which is
            // where 05 §9's harness is built. The fix if it lands is narrow — exempt
            // Domain.PublicRuleTypes for this layer — and it is written here so it is a decision
            // rather than a surprise.
            (Domain.TestingNamespace, new[] { Domain.RulesNamespace, Domain.HandlersNamespace }),
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

            // 🔒 M1-06. A command naming GameRules is a cycle — Apply CONSUMES commands — and a
            // command naming GameContext or CommandResult would be a second door onto Apply's own
            // arguments and return: a command carrying its own NowUtc or its own CommandSeed is the
            // ambient clock and the invented entropy 30 §3 exists to keep out, one indirection
            // further out and past every guard on GameContext. WorldSlice is in the root too, and a
            // command carrying one would smuggle the aggregates past the clone P4 depends on.
            Domain.CommandsNamespace,

            // 🔒 M1-06's architecture review. Model was ungoverned in this direction, and the cost
            // of that went up on the commit that put GameRules, WorldSlice and CommandResult in the
            // root beside GameContext: every one of those four is a cycle when an aggregate names
            // it. Apply CLONES the slice and CONSUMES the aggregates (30 §2.1's P4), so an aggregate
            // that named WorldSlice, CommandResult or GameRules would close a loop the layering
            // forbids, and one that named GameContext would be handed the clock that 30 §3 exists to
            // keep out of the domain — Player.MarkApplied takes the DateTimeOffset VALUE for exactly
            // that reason. Entitlements is the sharpest case and it is already ruled: 30 §3 and
            // 12 §2.1 put entitlement on the SESSION, never on the aggregate (GapRegister's Player
            // -contents note records the ruling), and IsolationTests only forbids Rules from
            // reaching it.
            //
            // ⚠️ RULES IS NOT HERE, and that is the open half, on the pattern of the Events row
            // above. A rule branching on GameContext.FeatureFlags (26 §8, 30 §3 resolve remote
            // config into that root type) is a shape nothing in the design set forbids, and a row
            // written on a guess would block the first task that needs it. The ruling belongs at the
            // kickoff of the milestone that first wants it, together with 30 §11.4's amendment for
            // Events -> Model.
            Domain.ModelNamespace,
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
                            $"{Domain.CoreNamespace} root. The root holds GameRules, WorldSlice, CommandResult " +
                            "and GameContext — the top of the layering — so a layer beneath it reaching up " +
                            "inverts Handlers -> Rules -> Model -> Content -> Primitives (30 §11.4). For Events: " +
                            "Apply PRODUCES the event list, so an event naming GameRules or GameContext is a " +
                            "cycle. For Model: Apply CLONES and CONSUMES the aggregates (30 §2.1's P4), so an " +
                            "aggregate naming WorldSlice, CommandResult or GameRules closes that loop. And in " +
                            "every layer, a timestamp reached through GameContext.NowUtc is the clock 30 §3 " +
                            "keeps out of the domain — take the value, not the context."));
            }
        }

        // 🔒 M1-11 — AND THE ROOT ITSELF MUST NOT NAME THE HARNESS. This is the one direction the
        // table above structurally cannot express: `Testing` was added to every layer's forbidden
        // list, but the SlayIdleRepeat.Core root has no Layer row (its outbound direction is
        // legitimate — the root is the top of the chain and reaches down by design), so
        // `GameRules -> InMemoryGame` was matched by nothing in either direction. That is the
        // sharpest cycle available in this assembly: InMemoryGame CALLS GameRules.Apply, so a root
        // type naming the harness closes a loop between the transition function and the thing that
        // exists to drive it — and it would additionally put a test artefact in the production call
        // graph of every command. Measured before this check: a `typeof(InMemoryGame)` field added to
        // GameRules passed 63/63.
        //
        // ⚠️ Written as its own loop rather than as a `(CoreNamespace, [TestingNamespace])` row,
        // because Domain.CoreTypesUnder matches by PREFIX and that row would select every type in
        // the assembly — including the harness itself, which would then be forbidden from naming its
        // own namespace. IsCoreRootType is the exact-match predicate the loop above already uses.
        foreach (var type in Domain.CoreTypes.Where(t => IsCoreRootType(t.FullName)))
        {
            offenders.AddRange(
                Il.ReferencedTypeNames(type)
                    .Where(r => Il.IsUnder(NamespaceOfReference(r), Domain.TestingNamespace))
                    .Select(referenced =>
                        $"{type.FullName} (in the {Domain.CoreNamespace} root) references {referenced}, which " +
                        $"is in {Domain.TestingNamespace}. 30 §6's harness DRIVES the root — InMemoryGame " +
                        "calls GameRules.Apply — so a root type naming it closes a cycle between the " +
                        "transition function and the thing that exists to exercise it, and puts a test " +
                        "artefact in the production call graph of every command. Testing sits at the TOP of " +
                        "30 §11.4's chain: it names the root, the root does not name it."));
        }

        ArchRule.Empty(
            offenders,
            "Core's internal layering holds: Testing -> Handlers -> Rules -> Model -> Content -> Primitives, " +
            "Commands names nothing above it, the 30 §6 harness under Testing/ names neither Rules nor " +
            "Handlers (it drives the domain through GameRules.Apply alone) and nothing beneath it — root " +
            "included — names the harness, and Primitives, Content, Rng, Events, Commands and Model never " +
            "reach up into the SlayIdleRepeat.Core root (30 §11.4, 30 §6, 30 §11.2).");
    }

    /// <summary>
    /// The namespace part of a Cecil type name — <c>SlayIdleRepeat.Core.Testing</c> for
    /// <c>SlayIdleRepeat.Core.Testing.InMemoryGame</c> and for its nested
    /// <c>…InMemoryGame/PlayerSession</c>.
    /// </summary>
    /// <remarks>
    /// A nested type is spelled <c>Namespace.Outer/Nested</c>, so the outer name is taken first and
    /// the namespace is what precedes its last dot. Written out rather than done with a
    /// <c>StartsWith(ns + ".")</c> because that would match a future
    /// <c>SlayIdleRepeat.Core.TestingSupport</c> as well, which is the prefix trap
    /// <c>Domain.IsPermittedCoreNamespace</c> documents.
    /// </remarks>
    private static string NamespaceOfReference(string typeFullName)
    {
        var outer = typeFullName.Split('/')[0];
        var lastDot = outer.LastIndexOf('.');

        return lastDot < 0 ? string.Empty : outer[..lastDot];
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
    /// <para>
    /// <c>Core_internal_layering_holds</c> works from a fixed table of forbidden pairs, so a
    /// type under a namespace that appears in no row is matched by nothing at all — not
    /// permitted, not forbidden, simply ungoverned, with the layering rule still green. (This
    /// is the hole M0-07 reasoned about when it placed <c>CanonicalStateWriter</c> under
    /// <c>Model/Snapshots/</c>; the judgement was right and the hole stayed open.) Naming the
    /// permitted set instead makes the next <c>Core/Foo/</c> a build failure rather than a
    /// silent new region.
    /// </para>
    /// <para>
    /// 🔒 <b>Two facts in the sentence above have been corrected rather than left to rot</b>
    /// (steering <b>S4</b>'s known limit, which this milestone keeps hitting). The table is not
    /// <b>five</b> rows — M1-06 added the <c>Commands</c> row and M1-11 the <c>Events</c> and
    /// <c>Testing</c> rows — and the count is deliberately <em>not</em> restated as a number here,
    /// because a number in prose beside the thing it counts is a number that goes stale silently.
    /// Read the table. And this rule is no longer <b>vacuously true</b>:
    /// it was, while <c>Core</c> held no types, and it has quantified over real ones since
    /// M0-06. <c>Core/Testing/</c> is the newest region it governs, and it is the sharpest case
    /// the closed list has had — a directory named <em>Testing</em> inside the <em>production</em>
    /// assembly (`30` §11.4), holding `30` §6's harness, whose every reference ships.
    /// </para>
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

    /// <summary>
    /// 🔒 `30` §11.2 — the teeth of the exposed-collection half of
    /// <see cref="Apply_is_the_only_public_mutation"/>, driven against real IL compiled from
    /// <see cref="ExposedCollectionFixtures"/>. The shape it must catch is a violation, and a
    /// violation is never committed to `Core` to prove a rule works.
    /// </summary>
    /// <remarks>
    /// The negative half carries the same weight as the positive one. Every collection an
    /// aggregate in this repository exposes today goes out behind a
    /// <c>ReadOnlyDictionary&lt;,&gt;</c>, a <c>ReadOnlyCollection&lt;T&gt;</c> or an
    /// <c>IReadOnly*</c>-typed field, and none of those casts back to its mutable store — so a
    /// check that flagged them would be reverted within a commit, and the hole would stay open.
    /// </remarks>
    [Fact]
    public void The_exposed_collection_check_catches_a_mutable_store_behind_a_read_only_view()
    {
        var hole = ExposedMutableCollections(
            SuiteAssembly.Type(nameof(ExposedCollectionFixtures.ArrayBehindAReadOnlyView)));

        hole.ShouldNotBeEmpty(
            "an int[] handed out as IReadOnlyList<int> casts straight back to int[]. If this is empty " +
            "the check is blind to the exact shape it was written for, and 30 §11.2's 'everything the " +
            "outside world can see is a getter' is a claim nothing enforces.");

        hole.ShouldHaveSingleItem().ShouldContain("System.Int32[]", Case.Sensitive);

        ExposedMutableCollections(
                SuiteAssembly.Type(nameof(ExposedCollectionFixtures.MutableFieldBehindAnInterfaceView)))
            .ShouldNotBeEmpty(
                "a Dictionary<,> reached through a getter typed IReadOnlyDictionary<,> casts back just as " +
                "an array does. A check that only knew about arrays would miss the commoner shape.");

        ExposedMutableCollections(
                SuiteAssembly.Type(nameof(ExposedCollectionFixtures.ReadOnlyWrapperOverAMutableStore)))
            .ShouldBeEmpty(
                "a ReadOnlyDictionary<,> view over a private Dictionary<,> is the house idiom — Player's " +
                "counters and Run's ad uses are both built this way. If this fires, the check flags the " +
                "correct construction and would be weakened back out again.");

        ExposedMutableCollections(
                SuiteAssembly.Type(nameof(ExposedCollectionFixtures.ReadOnlyInterfaceOverAnImmutableStore)))
            .ShouldBeEmpty(
                "a field DECLARED as IReadOnlyDictionary<,> is the shape Run.RngStreamPositions and " +
                "Player.Wallet use. It cannot be proven immutable from metadata and it is not the hole " +
                "this check is about — flagging it would make the rule unsatisfiable.");
    }

    /// <summary>
    /// The mutable collection types a read-only-looking getter can be cast straight back to.
    /// </summary>
    /// <remarks>
    /// Arrays are handled separately (they are an <see cref="ArrayType"/>, not a named type). The
    /// interfaces are here as well as the concrete classes because <c>IList&lt;T&gt;</c> and
    /// <c>ICollection&lt;T&gt;</c> carry <c>Add</c>/<c>Clear</c> in their own signature — a field
    /// declared as one is a mutation surface without any cast at all.
    /// </remarks>
    private static readonly string[] MutableCollectionTypes =
    {
        "System.Collections.Generic.List`1",
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.SortedDictionary`2",
        "System.Collections.Generic.SortedList`2",
        "System.Collections.Generic.SortedSet`1",
        "System.Collections.Generic.LinkedList`1",
        "System.Collections.Generic.Queue`1",
        "System.Collections.Generic.Stack`1",
        "System.Collections.ObjectModel.Collection`1",
        "System.Collections.ObjectModel.KeyedCollection`2",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IList`1",
        "System.Collections.Generic.IDictionary`2",
        "System.Collections.Generic.ISet`1",
        "System.Collections.IList",
        "System.Collections.IDictionary",
    };

    /// <summary>True for an array or a mutable collection type, generic instantiation included.</summary>
    private static bool IsMutableCollection(TypeReference? reference) =>
        reference is ArrayType ||
        (reference is not null &&
         MutableCollectionTypes.Contains(reference.GetElementType().FullName, StringComparer.Ordinal));

    /// <summary>
    /// Every mutable collection an aggregate hands out: a public getter (or public field) whose own
    /// type is mutable, and — the shape that matters — one whose read-only-looking type is backed
    /// by a field declared mutable, which a caller casts straight back to.
    /// </summary>
    /// <remarks>
    /// The backing field is read from the getter's IL rather than assumed to be an auto-property's,
    /// because the hole is written by hand: <c>public IReadOnlyList&lt;T&gt; Items =&gt; _items;</c>
    /// compiles to a <c>ldfld</c> on a field the property type says nothing about.
    /// </remarks>
    private static IReadOnlyList<string> ExposedMutableCollections(TypeDefinition type)
    {
        var offenders = new List<string>();

        foreach (var property in type.Properties)
        {
            var getter = property.GetMethod;
            if (getter is null || !getter.IsPublic || Domain.IsCompilerGenerated(getter))
            {
                continue;
            }

            if (IsMutableCollection(property.PropertyType))
            {
                offenders.Add(
                    $"{type.FullName}.{property.Name} is typed {property.PropertyType.FullName}, a mutable " +
                    "collection. A caller adds, clears or overwrites the aggregate's state through a getter, " +
                    "which is a public mutation path around GameRules.Apply (30 §11.2).");
                continue;
            }

            offenders.AddRange(
                ReturnedFields(getter)
                    .Where(field => IsMutableCollection(field.FieldType))
                    .Select(field =>
                        $"{type.FullName}.{property.Name} is typed {property.PropertyType.FullName} but returns " +
                        $"{Il.Describe(field)}, declared {field.FieldType.FullName} — a mutable store a caller " +
                        "casts straight back to. Wrap it (Array.AsReadOnly, ReadOnlyDictionary<,>) or hand out " +
                        "a copy; a read-only-looking type is not a boundary (30 §11.2)."));
        }

        offenders.AddRange(
            type.Fields
                .Where(f => f.IsPublic && !Domain.IsCompilerGenerated(f) && IsMutableCollection(f.FieldType))
                .Select(f =>
                    $"{Il.Describe(f)} is a public {f.FieldType.FullName} — a mutable collection anyone can " +
                    "write to, readonly or not (30 §11.2)."));

        return offenders;
    }

    /// <summary>Every field of the declaring type a getter loads and returns.</summary>
    private static IEnumerable<FieldDefinition> ReturnedFields(MethodDefinition getter) =>
        Il.Instructions(getter)
          .Where(i => i.OpCode == OpCodes.Ldfld || i.OpCode == OpCodes.Ldsfld)
          .Select(i => (i.Operand as FieldReference)?.Resolve())
          .Where(f => f is not null &&
                      f.DeclaringType.FullName.Equals(getter.DeclaringType.FullName, StringComparison.Ordinal))
          .Select(f => f!);

    /// <summary>
    /// The four shapes <see cref="ExposedMutableCollections"/> has to tell apart. They live here
    /// rather than in `Core` because two of them are violations, and a violation is never committed
    /// to the domain to prove a rule works.
    /// </summary>
    private static class ExposedCollectionFixtures
    {
        /// <summary>The hole `Player.WalletCurrencies` documents: an array behind a read-only view.</summary>
        internal sealed class ArrayBehindAReadOnlyView
        {
            private readonly int[] _items = new int[1];

            /// <summary>Casts straight back to <c>int[]</c>.</summary>
            public IReadOnlyList<int> Items => _items;
        }

        /// <summary>The commoner spelling of the same hole: a <c>Dictionary</c> behind an interface.</summary>
        internal sealed class MutableFieldBehindAnInterfaceView
        {
            private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

            /// <summary>Casts straight back to <c>Dictionary&lt;string, long&gt;</c>.</summary>
            public IReadOnlyDictionary<string, long> Counters => _counters;
        }

        /// <summary>The house idiom, and it must not be flagged: `Player`'s counters, `Run`'s ad uses.</summary>
        internal sealed class ReadOnlyWrapperOverAMutableStore
        {
            private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
            private readonly ReadOnlyDictionary<string, long> _view;

            /// <summary>Builds the view once, exactly as the aggregates do.</summary>
            internal ReadOnlyWrapperOverAMutableStore() =>
                _view = new ReadOnlyDictionary<string, long>(_counters);

            /// <summary>A live view that cannot be written through.</summary>
            public IReadOnlyDictionary<string, long> Counters => _view;
        }

        /// <summary>The other legitimate shape: a field DECLARED read-only, replaced wholesale.</summary>
        internal sealed class ReadOnlyInterfaceOverAnImmutableStore
        {
            private readonly IReadOnlyDictionary<string, ulong> _positions =
                new ReadOnlyDictionary<string, ulong>(new Dictionary<string, ulong>(0, StringComparer.Ordinal));

            /// <summary>`Run.RngStreamPositions`' shape.</summary>
            public IReadOnlyDictionary<string, ulong> Positions => _positions;
        }
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
