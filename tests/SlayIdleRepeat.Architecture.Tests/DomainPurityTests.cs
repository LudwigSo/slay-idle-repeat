using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
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
    /// `SlayIdleRepeat.Core`. IL/metadata scan over member signatures plus the
    /// `AsyncStateMachineAttribute` the compiler stamps on every `async` method.
    /// </summary>
    [Fact]
    public void Domain_is_synchronous()
    {
        var banned = new HashSet<string>(AsynchronyTypes, StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var type in Domain.CoreTypes)
        {
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
    /// move currency (`30` §11.3). Vacuous until M1 adds the first currency field.
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
    private static IEnumerable<string> CurrencyFields()
    {
        foreach (var type in Domain.CoreTypes)
        {
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

    private static bool EmitsCurrencyChanged(MethodDefinition method) =>
        Il.Instructions(method)
          .SelectMany(Il.OperandTypes)
          .SelectMany(Il.Flatten)
          .Any(r => r.Name.Equals(Domain.CurrencyChangedEvent, StringComparison.Ordinal));
}
