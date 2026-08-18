using Mono.Cecil;
using NetArchTest.Rules;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `23` §2.1 / §6 — the dependency rule: Adapters ▶ Application ▶ Core ▶ (nothing),
/// plus the port-purity rules `23` §5 A2 and A5.
/// </summary>
public sealed class DependencyRuleTests
{
    /// <summary>
    /// The vendor namespaces `23` §6 names verbatim. Not an exhaustive list of every
    /// vendor in the solution — <see cref="Core_references_only_BCL_assemblies"/> is the
    /// exhaustive rule. This one exists because the doc states it, and it fails with a
    /// far more legible message when someone reaches for a driver from `Core`.
    /// </summary>
    private static readonly string[] BannedVendorNamespaces =
    {
        "Godot", "Microsoft.AspNetCore", "Npgsql", "StackExchange", "Amazon", "Sentry",
    };

    /// <summary>`23` §6 — `Core` depends on nothing but the BCL: no vendor namespace may appear in it.</summary>
    [Fact]
    public void Core_depends_on_nothing_but_the_BCL()
    {
        var result = Types.InAssembly(ProductionAssemblies.Core)
                          .ShouldNot()
                          .HaveDependencyOnAny(BannedVendorNamespaces)
                          .GetResult();

        ArchRule.Assert(
            result,
            $"SlayIdleRepeat.Core must not depend on any of: {string.Join(", ", BannedVendorNamespaces)} (23 §6).");
    }

    /// <summary>`23` §2.1 — the exhaustive form: every assembly `Core` references is a BCL assembly.</summary>
    [Fact]
    public void Core_references_only_BCL_assemblies()
    {
        var offenders = ProductionAssemblies.CoreModule.AssemblyReferences
            .Select(r => r.Name)
            .Where(name => !Il.IsBclAssembly(name))
            .Select(name => $"SlayIdleRepeat.Core references '{name}'");

        ArchRule.Empty(
            offenders,
            "SlayIdleRepeat.Core references nothing but the .NET BCL (23 §2.1).");
    }

    /// <summary>`23` §6 — `Application` never references an adapter.</summary>
    [Fact]
    public void Application_never_references_an_adapter()
    {
        var result = Types.InAssembly(ProductionAssemblies.Application)
                          .ShouldNot()
                          .HaveDependencyOn(ProductionAssemblies.AdapterPrefix.TrimEnd('.'))
                          .GetResult();

        ArchRule.Assert(
            result,
            "SlayIdleRepeat.Application must not depend on any SlayIdleRepeat.Adapters.* type (23 §6).");
    }

    /// <summary>`23` §5 A7 / §6 — adapters never reference each other (pairwise, over IL and assembly references).</summary>
    [Fact]
    public void Adapters_never_reference_each_other()
    {
        var adapters = ProductionAssemblies.AdapterNames;
        var offenders = new List<string>();

        foreach (var adapter in adapters)
        {
            var others = adapters.Where(o => !o.Equals(adapter, StringComparison.Ordinal)).ToArray();
            var module = ProductionAssemblies.Module(adapter);

            // Assembly-level: catches a reference even from an adapter with no types yet.
            offenders.AddRange(
                module.AssemblyReferences
                      .Select(r => r.Name)
                      .Where(name => others.Contains(name, StringComparer.Ordinal))
                      .Select(name => $"{adapter} -> {name} (assembly reference)"));

            // Type-level: names the offending type, which is what a fix needs.
            var result = Types.InAssembly(ProductionAssemblies.Load(adapter))
                              .ShouldNot()
                              .HaveDependencyOnAny(others.ToArray())
                              .GetResult();

            if (!result.IsSuccessful)
            {
                offenders.AddRange(
                    (result.FailingTypeNames ?? Array.Empty<string>())
                    .Select(t => $"{adapter} -> another adapter, from type {t}"));
            }
        }

        ArchRule.Empty(offenders, "Adapters never reference each other; composition happens only at the root (23 §5 A7).");
    }

    /// <summary>
    /// `23` §5 A5 / §6 — every port has at least two implementations: the real adapter
    /// and an in-memory fake. Two implementations is the cheapest proof the abstraction
    /// is real.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LIVE since M0-09, which landed `IContentSourcePort` under `Application/Ports/Shared/`
    /// with both implementations. The comment here used to say "vacuous until M1/M2 declare
    /// the first port"; leaving that in place is the exact form of documentation
    /// `SuiteIntegrityTests` exists to prevent, because the next reader takes it at its word
    /// and assumes the rule is not watching.
    /// </para>
    /// <para>
    /// 🔒 <b>This rule counts implementations; it does not ask WHERE they may live.</b> Its
    /// companion is <c>PortCatalogueTests.No_type_in_the_engine_adapter_implements_a_port</c>
    /// (`23` §7.2a), which forbids the two assemblies that can reach the engine API from holding
    /// one at all — because such an implementation would satisfy the count here while being
    /// untestable at the only tier this repository has. An engine-port author trips that rule and
    /// needs to read this one; the pointer is here so the trip works in both directions.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_port_has_at_least_two_implementations()
    {
        var implementations = ProductionAssemblies.AllNames
            .SelectMany(name => Il.AllTypes(ProductionAssemblies.Module(name)))
            .Where(t => !t.IsInterface && !t.IsAbstract && !Domain.IsCompilerGenerated(t))
            .ToArray();

        var offenders = new List<string>();

        foreach (var port in Domain.Ports)
        {
            var implementors = implementations
                .Where(t => Il.ImplementsInterface(t, port.FullName))
                .Select(t => t.FullName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            if (implementors.Length < 2)
            {
                offenders.Add(
                    $"{port.FullName} has {implementors.Length} implementation(s) " +
                    $"[{string.Join(", ", implementors)}] — needs the real adapter and the in-memory fake");
            }
        }

        ArchRule.Empty(offenders, "Every port has at least two implementations (23 §5 A5).");
    }

    /// <summary>
    /// `23` §5 A2 / §6 — no port signature exposes a vendor type, in a parameter, a return
    /// type, a generic argument or a property. A port may speak only BCL, `Core`,
    /// `Contracts` and `Application` types.
    /// </summary>
    /// <remarks>
    /// LIVE since M0-09 — see the note on <see cref="Every_port_has_at_least_two_implementations"/>.
    /// `IContentSourcePort` is a real subject, and this rule asserts over it today.
    /// </remarks>
    [Fact]
    public void No_port_signature_exposes_a_vendor_type()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            ProductionAssemblies.CoreName,
            ProductionAssemblies.ContractsName,
            ProductionAssemblies.ApplicationName,
        };

        var offenders = new List<string>();

        foreach (var port in Domain.Ports)
        {
            foreach (var (reference, member) in PortSignatureTypes(port))
            {
                if (reference.IsGenericParameter)
                {
                    continue;
                }

                var assembly = Il.AssemblyNameOf(reference);
                if (assembly.Length == 0 || Il.IsBclAssembly(assembly) || allowed.Contains(assembly))
                {
                    continue;
                }

                offenders.Add($"{port.FullName}.{member} exposes {reference.FullName} from '{assembly}'");
            }
        }

        ArchRule.Empty(offenders, "No vendor type crosses a port boundary (23 §5 A2).");
    }

    private static IEnumerable<(TypeReference Reference, string Member)> PortSignatureTypes(TypeDefinition port)
    {
        foreach (var method in port.Methods)
        {
            foreach (var reference in Il.SignatureTypes(method).SelectMany(Il.Flatten))
            {
                yield return (reference, method.Name);
            }
        }

        foreach (var property in port.Properties)
        {
            foreach (var reference in Il.Flatten(property.PropertyType))
            {
                yield return (reference, property.Name);
            }
        }

        foreach (var @event in port.Events)
        {
            foreach (var reference in Il.Flatten(@event.EventType))
            {
                yield return (reference, @event.Name);
            }
        }

        foreach (var reference in port.Interfaces.SelectMany(i => Il.Flatten(i.InterfaceType)))
        {
            yield return (reference, "(base interface)");
        }
    }
}
