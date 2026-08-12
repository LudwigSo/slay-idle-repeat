using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `30` §9 — the rules that keep the domain playable in memory.
/// Every rule here is written against its final subject; the subjects M1 creates
/// simply yield an empty set today, so each rule turns into a real assertion the
/// moment the type it names appears.
/// </summary>
public sealed class DomainPurityTests
{
    /// <summary>Types that make a signature asynchronous. `30` §9 bans all of them from `Core`.</summary>
    private static readonly string[] AsynchronyTypes =
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask",
        "System.Threading.Tasks.ValueTask`1",
        "System.Threading.Tasks.TaskCompletionSource",
        "System.Threading.Tasks.TaskCompletionSource`1",
        "System.Threading.CancellationToken",
        "System.Threading.CancellationTokenSource",
        "System.Collections.Generic.IAsyncEnumerable`1",
        "System.Collections.Generic.IAsyncEnumerator`1",
        "System.IAsyncDisposable",
    };

    /// <summary>
    /// `30` §9 — the domain is synchronous: no `Task`, `ValueTask`, `async`,
    /// `CancellationToken` or `IAsyncEnumerable` in any public or private signature in
    /// `SlayIdleRepeat.Core`, nor anywhere in a method body. IL/metadata scan over member
    /// signatures, locals and IL operands, plus the `AsyncStateMachineAttribute` the compiler
    /// stamps on every `async` method.
    /// </summary>
    /// <remarks>
    /// The body scan is the half that was missing. A signature scan sees a method that
    /// RETURNS a `Task`; it does not see one that starts work and drops it —
    /// `_ = Task.Run(Recalculate);` has a `void` signature, no async state machine, and
    /// launches a thread inside a domain `30` §9 requires to be deterministic and
    /// replayable. `Il.ReferencedTypeNames` already walks locals and operands.
    /// </remarks>
    [Fact]
    public void Domain_is_synchronous()
    {
        var banned = new HashSet<string>(AsynchronyTypes, StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var type in Domain.CoreTypes)
        {
            offenders.AddRange(
                Il.ReferencedTypeNames(type)
                  .Where(banned.Contains)
                  .Select(n => $"{type.FullName} names {n} somewhere in its members or their bodies"));

            foreach (var field in type.Fields)
            {
                offenders.AddRange(
                    Il.Flatten(field.FieldType)
                      .Where(r => banned.Contains(r.FullName))
                      .Select(r => $"{Il.Describe(field)} : {r.FullName}"));
            }

            foreach (var property in type.Properties)
            {
                offenders.AddRange(
                    Il.Flatten(property.PropertyType)
                      .Where(r => banned.Contains(r.FullName))
                      .Select(r => $"{type.FullName}.{property.Name} : {r.FullName}"));
            }

            foreach (var method in type.Methods)
            {
                offenders.AddRange(
                    Il.SignatureTypes(method).SelectMany(Il.Flatten)
                      .Where(r => banned.Contains(r.FullName))
                      .Select(r => $"{Il.Describe(method)} : {r.FullName}"));

                if (method.CustomAttributes.Any(a =>
                        a.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"))
                {
                    offenders.Add($"{Il.Describe(method)} is an async method");
                }
            }
        }

        ArchRule.Empty(offenders, "The domain is synchronous — no Task/ValueTask/async/CancellationToken in Core (30 §9).");
    }

    /// <summary>
    /// `30` §9 — the `Core`-scoped form of the ambient-API ban (`14` §8.1), extended to
    /// ban `IClockPort` itself appearing in `Core`. Time enters the domain only as
    /// `GameContext.NowUtc` (`30` §3); a rule that calls a clock is not pure.
    /// </summary>
    [Fact]
    public void Domain_has_no_ambient_time_or_randomness()
    {
        var offenders = BannedApi.Violations(ProductionAssemblies.CoreModule).ToList();

        offenders.AddRange(
            Domain.CoreTypes
                  .Where(t => t.Name.Equals(Domain.ClockPortType, StringComparison.Ordinal))
                  .Select(t => $"{t.FullName} — IClockPort must not exist in Core (30 §9)"));

        offenders.AddRange(
            Domain.CoreTypes
                  .SelectMany(t => Il.ReferencedTypeNames(t).Select(n => (Type: t, Name: n)))
                  .Where(x => x.Name.EndsWith("." + Domain.ClockPortType, StringComparison.Ordinal) ||
                              x.Name.Equals(Domain.ClockPortType, StringComparison.Ordinal))
                  .Select(x => $"{x.Type.FullName} names {x.Name} — Core must not know about a clock port (30 §9)"));

        ArchRule.Empty(offenders, "The domain has no ambient time or randomness, and no clock port (30 §9, 14 §8.1).");
    }

    /// <summary>
    /// `30` §9 — the domain references no port interface: `Core` must not name any type
    /// from `SlayIdleRepeat.Application/Ports/`, nor reference the `Application` assembly.
    /// </summary>
    [Fact]
    public void Domain_references_no_port_interface()
    {
        var offenders = new List<string>();

        offenders.AddRange(
            ProductionAssemblies.CoreModule.AssemblyReferences
                .Where(r => r.Name.StartsWith("SlayIdleRepeat.", StringComparison.Ordinal))
                .Select(r => $"SlayIdleRepeat.Core references assembly '{r.Name}'"));

        foreach (var type in Domain.CoreTypes)
        {
            offenders.AddRange(
                Il.ReferencedTypeNames(type)
                  .Where(n => n.StartsWith(Domain.PortsNamespace + ".", StringComparison.Ordinal) ||
                              n.StartsWith(ProductionAssemblies.ApplicationName + ".", StringComparison.Ordinal))
                  .Select(n => $"{type.FullName} names {n}"));

            // A port copied into Core rather than referenced is the same violation.
            offenders.AddRange(
                Il.ReferencedTypeNames(type)
                  .Where(IsPortShaped)
                  .Select(n => $"{type.FullName} names the port-shaped type {n}"));
        }

        ArchRule.Empty(offenders, "Core names nothing from Application/Ports/ (30 §9).");
    }

    /// <summary>
    /// `30` §9 — every `GameCommand` subtype is handled: no silently unhandled command.
    /// The dispatch surface is `GameRules` plus everything under `Core/Handlers/`
    /// (`30` §11.4, one handler per command); a concrete command no type on that surface
    /// mentions has no way of being applied. Vacuous until M1 declares `GameCommand`.
    /// </summary>
    [Fact]
    public void Every_command_type_is_handled_by_Apply()
    {
        var gameCommand = Domain.FindInCore(Domain.GameCommandType);
        if (gameCommand is null)
        {
            // No command hierarchy yet: the set of unhandled commands is empty, and the
            // rule holds. It becomes an assertion over 48 commands the day M1 lands.
            ArchRule.Empty(Array.Empty<string>(), UnhandledCommandRule);
            return;
        }

        var commands = Domain.CoreTypes
            .Where(t => !t.IsAbstract && !t.IsInterface && !Domain.IsCompilerGenerated(t))
            .Where(t => Domain.DerivesFrom(t, Domain.GameCommandType))
            .ToArray();

        var dispatchSurface = Domain.CoreTypes
            .Where(t => Il.IsUnder(Il.NamespaceOf(t), Domain.HandlersNamespace) ||
                        t.Name.Equals(Domain.GameRulesType, StringComparison.Ordinal))
            .ToArray();

        var dispatched = new HashSet<string>(
            dispatchSurface.SelectMany(Il.ReferencedTypeNames),
            StringComparer.Ordinal);

        var offenders = commands
            .Where(c => !dispatched.Contains(c.FullName))
            .Select(c => $"{c.FullName} is never named by GameRules or by any type under {Domain.HandlersNamespace}");

        ArchRule.Empty(offenders, UnhandledCommandRule);
    }

    /// <summary>
    /// `30` §9 / §7 — every currency mutation emits `CurrencyChanged`. IL scan: a write
    /// to a currency-carrying field may only happen inside a method that also emits the
    /// event. Construction and rehydration are exempt — they rebuild state rather than
    /// move currency (`30` §11.3). Vacuous until M1-04 adds the first field a currency is
    /// <b>held</b> in — see <see cref="CurrencyFields"/> for why an event does not count.
    /// </summary>
    [Fact]
    public void Every_currency_mutation_emits_CurrencyChanged()
    {
        var currencyFields = CurrencyFields().ToHashSet(StringComparer.Ordinal);
        if (currencyFields.Count == 0)
        {
            ArchRule.Empty(Array.Empty<string>(), CurrencyRule);
            return;
        }

        var offenders = new List<string>();

        foreach (var method in Il.MethodsWithBodies(ProductionAssemblies.CoreModule))
        {
            var written = Il.Instructions(method)
                .Where(i => i.OpCode == OpCodes.Stfld || i.OpCode == OpCodes.Stsfld)
                .Select(i => (i.Operand as FieldReference)?.FullName)
                .Where(name => name is not null && currencyFields.Contains(name))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (written.Length == 0 || IsRehydrationOrConstruction(method) || EmitsCurrencyChanged(method))
            {
                continue;
            }

            offenders.AddRange(
                written.Select(field =>
                    $"{Il.Describe(method)} writes {field} without emitting {Domain.CurrencyChangedEvent}"));
        }

        ArchRule.Empty(offenders, CurrencyRule);
    }

    /// <summary>
    /// 🔒 `30` §7 / `30` §9 — the teeth of the event exclusion the rule above rests on. It must
    /// recognise the event hierarchy and refuse everything else, or the currency subject set is
    /// being emptied by something other than what the remark claims (steering S3).
    /// </summary>
    /// <remarks>
    /// The exclusion is the only reason <c>CurrencyFields()</c> is empty today. An
    /// <c>IsDomainEvent</c> that answered <c>true</c> for everything would empty the set
    /// permanently — including on the day M1-04 lands the first wallet — and
    /// <c>Every_currency_mutation_emits_CurrencyChanged</c> would short-circuit forever with its
    /// `count == 0` sentinel looking exactly as it does now.
    /// </remarks>
    [Fact]
    public void The_event_exclusion_recognises_the_hierarchy_and_nothing_else()
    {
        Assert.True(
            Domain.IsDomainEvent(Require(Domain.DomainEventType)),
            "DomainEvent is the base of the 30 §7 hierarchy. If this is false the exclusion matches nothing " +
            "and CurrencyChanged's CurrencyId-typed backing field is back in the subject set.");

        Assert.True(
            Domain.IsDomainEvent(Require(Domain.CurrencyChangedEvent)),
            "CurrencyChanged is a DomainEvent under Core/Events/ — the one type this exclusion exists for.");

        Assert.False(
            Domain.IsDomainEvent(Require(Domain.CurrencyIdType)),
            "CurrencyId is a Primitives enum, not an event. An exclusion that swallowed it would swallow " +
            "every currency-carrying type M1-04 declares.");

        Assert.False(
            Domain.IsDomainEvent(Require(Domain.EntitlementsType)),
            "Entitlements sits in the Core root and derives from nothing. This pins that the predicate is " +
            "namespace-scoped and base-typed rather than answering true for whatever it is handed.");

        Assert.False(
            Domain.IsDomainEvent(TypeFixture(nameof(CurrencyEmissionFixtures.DerivesButIsMisplaced))),
            "a type that derives from DomainEvent but does NOT live under Core/Events/ must not be excluded. " +
            "Drop the namespace half and a Core/Model/ aggregate could exempt its own wallet from " +
            "Every_currency_mutation_emits_CurrencyChanged by inheriting from an event — nothing else in this " +
            "suite forbids that inheritance.");
    }

    /// <summary>
    /// 🔒 `30` §7 / `30` §9 — the teeth of the emission half. Driven against real IL compiled
    /// from <see cref="CurrencyEmissionFixtures"/> and read back with Cecil, because the shape
    /// that matters — a method that *reads* an event while mutating a balance — does not exist in
    /// `Core` and must never have to.
    /// </summary>
    /// <remarks>
    /// The negative case is the whole point. Until this branch narrowed it,
    /// <see cref="EmitsCurrencyChanged"/> answered <c>true</c> for
    /// <see cref="CurrencyEmissionFixtures.OnlyReads"/> — <c>Il.OperandTypes</c> yields the
    /// *declaring* type of every member reference, so touching an event counted as emitting one.
    /// </remarks>
    [Fact]
    public void The_emission_check_recognises_a_constructed_event_and_refuses_one_that_is_only_read()
    {
        Assert.True(
            EmitsCurrencyChanged(Fixture(nameof(CurrencyEmissionFixtures.Emits))),
            "a method that constructs a CurrencyChanged emits one. If this is false the rule flags every " +
            "legitimate grant M1-04 onwards and gets weakened back out again.");

        Assert.False(
            EmitsCurrencyChanged(Fixture(nameof(CurrencyEmissionFixtures.OnlyReads))),
            "reading Delta off an event the method was handed is not emitting one. If this is true, a handler " +
            "that debits a balance and inspects any other event satisfies 30 §7 without producing a row for " +
            "21 §8.3's income_attribution.csv.");
    }

    /// <summary>
    /// 🔒 `30` §9 — the load-bearing test. `InMemoryGame`'s assembly closure is exactly
    /// { `SlayIdleRepeat.Core`, `System.*` }: the whole game is playable from `Core` alone,
    /// with no Application, no ports, no fakes, no adapters (`30` §6).
    /// </summary>
    [Fact]
    public void The_whole_game_is_playable_from_Core_alone()
    {
        var offenders = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(ProductionAssemblies.CoreName);

        while (queue.Count > 0)
        {
            var assemblyName = queue.Dequeue();
            if (!visited.Add(assemblyName))
            {
                continue;
            }

            foreach (var reference in ProductionAssemblies.Module(assemblyName).AssemblyReferences)
            {
                if (Il.IsBclAssembly(reference.Name))
                {
                    continue;
                }

                offenders.Add($"{assemblyName} -> {reference.Name}");

                if (ProductionAssemblies.AllNames.Contains(reference.Name, StringComparer.Ordinal))
                {
                    queue.Enqueue(reference.Name);
                }
            }
        }

        // When InMemoryGame exists it must live in Core and be constructible from outside it —
        // it is the only object tests and the economy simulator ever build (30 §6).
        var harness = Domain.FindInCore(Domain.InMemoryGameType);
        if (harness is not null && !harness.IsPublic)
        {
            offenders.Add($"{harness.FullName} is not public — the harness must be usable from outside Core (30 §6)");
        }

        ArchRule.Empty(
            offenders,
            "The whole game is playable from Core alone: Core's assembly closure is exactly " +
            "{ SlayIdleRepeat.Core, System.* } (30 §9, the load-bearing rule).");
    }

    private const string UnhandledCommandRule =
        "Every GameCommand subtype is handled by Apply — no silently unhandled command (30 §9).";

    private const string CurrencyRule =
        "Every currency mutation emits CurrencyChanged (30 §9, 30 §7).";

    private static bool IsPortShaped(string typeFullName)
    {
        var simpleName = typeFullName.Split('.', '/').Last();
        return typeFullName.Contains(".Ports.", StringComparison.Ordinal) &&
               simpleName.StartsWith("I", StringComparison.Ordinal) &&
               simpleName.EndsWith("Port", StringComparison.Ordinal);
    }

    /// <summary>
    /// Currency-carrying fields: anything typed by (or generic over) the `CurrencyId`
    /// primitive `30` §7 pins, plus anything named for a wallet or a currency. Both
    /// halves are name-based on purpose — the rule must recognise its subject the day
    /// M1 writes it, without M1 having to opt in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The `30` §7 event hierarchy is excluded, and that exclusion is load-bearing.</b>
    /// A `DomainEvent` is the *emission* of a currency movement, never the place one is
    /// held. Without this skip, M1-03's `CurrencyChanged(int, CurrencyId Id, long, string)`
    /// alone made this set non-empty — which does not wake the rule up, it just takes away
    /// the `count == 0` sentinel that is the only visible signal the rule is still asleep.
    /// That is steering S3's failure mode arriving through the front door: the set stays
    /// meaningfully empty until M1-04 puts a currency on the `Player` aggregate, and it
    /// must keep *saying* so.
    /// </para>
    /// <para>
    /// ⚠️ <b>Not "because constructors are exempt".</b> Measured, not assumed: with the skip
    /// removed, the three methods writing `CurrencyChanged::&lt;Id&gt;k__BackingField` are
    /// its two constructors *and* `set_Id`, the compiler-generated `init` accessor —
    /// `IsRehydrationOrConstruction` does not exempt that one. It passed only because
    /// `EmitsCurrencyChanged` used to count *touching* the type as emitting it. That
    /// predicate has since been narrowed to production (see
    /// <see cref="EmitsCurrencyChanged"/>), which is what makes this exclusion the thing
    /// actually keeping the set empty rather than a second opinion about it.
    /// </para>
    /// <para>
    /// The predicate is <see cref="Domain.IsDomainEvent"/> — namespace-scoped *and* base-typed,
    /// so a `Core/Model/` aggregate cannot exempt its own wallet by deriving from `DomainEvent`.
    /// Its teeth are shown in
    /// <see cref="The_event_exclusion_recognises_the_hierarchy_and_nothing_else"/>.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> CurrencyFields()
    {
        foreach (var type in Domain.CoreTypes)
        {
            if (Domain.IsDomainEvent(type))
            {
                continue;
            }

            foreach (var field in type.Fields)
            {
                if (field.IsLiteral || field.IsStatic)
                {
                    continue;
                }

                var byType = Il.Flatten(field.FieldType)
                               .Any(r => r.Name.Equals(Domain.CurrencyIdType, StringComparison.Ordinal) ||
                                         r.Name.Contains("Wallet", StringComparison.Ordinal));

                var name = field.Name.Trim('<', '>').Replace("k__BackingField", string.Empty, StringComparison.Ordinal);
                var byName = name.Contains("currenc", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("wallet", StringComparison.OrdinalIgnoreCase);

                if (byType || byName)
                {
                    yield return field.FullName;
                }
            }
        }
    }

    private static bool IsRehydrationOrConstruction(MethodDefinition method) =>
        method.IsConstructor ||
        method.Name.Equals("Rehydrate", StringComparison.Ordinal) ||
        method.Name.Equals("FromSnapshot", StringComparison.Ordinal);

    /// <summary>
    /// 🔒 Whether a method <b>produces</b> a `CurrencyChanged`: it constructs one, or it calls
    /// something that returns one. Not merely mentions one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ This used to be "any operand type named `CurrencyChanged`", which
    /// <c>Il.OperandTypes</c> yields for the <i>declaring</i> type of every field and method
    /// reference — so reading <c>evt.Delta</c> off an event the method was handed counted as
    /// emitting one. Verified against this very branch: `CurrencyChanged.set_Id` writes a
    /// currency-typed field, is not a constructor, and was exempted by that reading alone.
    /// </para>
    /// <para>
    /// The consequence once M1-04 lands the first wallet is the whole rule: a handler that
    /// debits a balance and happens to inspect some *other* event on the way would satisfy
    /// `30` §7's "every currency movement emits `CurrencyChanged`" without emitting anything.
    /// That is the Critical this rule exists to catch, passing green.
    /// </para>
    /// <para>
    /// A <c>call</c> is counted alongside <c>newobj</c> so a factory or a <c>with</c> expression
    /// still reads as production, and <c>Il.Flatten</c> unwraps a returned collection of events.
    /// </para>
    /// <para>
    /// ⚠️ <b>The residual limit, stated so nobody assumes otherwise.</b> A `Core` method that
    /// *returns* an event without building one — a lookup, a passthrough — still reads as
    /// production. Narrowing further was not done because it could not be demonstrated to bite
    /// against any shape that compiles today (steering S1), and a clause nobody can show working
    /// is the defect this whole file is about. Indexing a `List&lt;CurrencyChanged&gt;` is already
    /// excluded: Cecil hands back the open element method, so the return type reads as the
    /// generic parameter rather than the event.
    /// </para>
    /// </remarks>
    private static bool EmitsCurrencyChanged(MethodDefinition method) =>
        Il.Instructions(method).Any(Produces);

    /// <summary>True when a single instruction constructs a `CurrencyChanged` or returns one.</summary>
    private static bool Produces(Instruction instruction)
    {
        if (instruction.Operand is not MethodReference reference)
        {
            return false;
        }

        if (instruction.OpCode == OpCodes.Newobj)
        {
            return NamesTheEvent(reference.DeclaringType);
        }

        return (instruction.OpCode == OpCodes.Call ||
                instruction.OpCode == OpCodes.Callvirt) &&
               NamesTheEvent(reference.ReturnType);
    }

    /// <summary>True when a type reference is, or wraps, the `CurrencyChanged` event.</summary>
    private static bool NamesTheEvent(TypeReference? reference) =>
        Il.Flatten(reference).Any(r => r.Name.Equals(Domain.CurrencyChangedEvent, StringComparison.Ordinal));

    /// <summary>
    /// The `Core` type with this simple name, or a failure that says which rule went silent —
    /// never a silent <c>null</c> that would make a teeth-check pass over nothing.
    /// </summary>
    private static TypeDefinition Require(string simpleName) =>
        Domain.FindInCore(simpleName)
        ?? throw new InvalidOperationException(
            $"SlayIdleRepeat.Core declares no type named '{simpleName}', so the predicate below is being " +
            "driven against nothing. SubjectSetFloorTests tracks this name for exactly that reason.");

    /// <summary>
    /// This assembly, read back through Cecil so <see cref="EmitsCurrencyChanged"/> can be driven
    /// against real IL rather than a hand-built <c>MethodDefinition</c> that could be wrong in the
    /// same direction as the predicate.
    /// </summary>
    private static readonly Lazy<ModuleDefinition> OwnModule = new(() =>
        ModuleDefinition.ReadModule(typeof(DomainPurityTests).Assembly.Location));

    /// <summary>One fixture method, by name, out of this assembly's own metadata.</summary>
    private static MethodDefinition Fixture(string name) =>
        FixtureHost().Methods.Single(m => m.Name.Equals(name, StringComparison.Ordinal));

    /// <summary>One fixture type, by name, out of this assembly's own metadata.</summary>
    private static TypeDefinition TypeFixture(string name) =>
        FixtureHost().NestedTypes.Single(t => t.Name.Equals(name, StringComparison.Ordinal));

    private static TypeDefinition FixtureHost() =>
        Il.AllTypes(OwnModule.Value)
          .Single(t => t.Name.Equals(nameof(CurrencyEmissionFixtures), StringComparison.Ordinal));

    /// <summary>
    /// The two IL shapes <see cref="EmitsCurrencyChanged"/> has to tell apart. They live here
    /// rather than in `Core` because the one that matters is a violation, and a violation is
    /// never committed to the domain to prove a rule works.
    /// </summary>
    private static class CurrencyEmissionFixtures
    {
        /// <summary>Produces an event — a <c>newobj</c> on `CurrencyChanged`.</summary>
        internal static object Emits() =>
            new CurrencyChanged(0, CurrencyId.CROWNS, 1, "architecture_rule_teeth_check");

        /// <summary>
        /// Only reads one. The IL names `CurrencyChanged` as the declaring type of the property
        /// getter, which is precisely the mention the old predicate accepted as an emission.
        /// </summary>
        internal static long OnlyReads(CurrencyChanged handed) => handed.Delta;

        /// <summary>
        /// Derives from `DomainEvent` while living outside `Core/Events/` — the shape the
        /// namespace half of <see cref="Domain.IsDomainEvent"/> exists to refuse. Nested and
        /// non-public, so it is outside every real subject set in the repository.
        /// </summary>
        internal sealed record DerivesButIsMisplaced(int Sequence) : DomainEvent(Sequence);
    }
}
