using Mono.Cecil;

namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// The names the rules are written against. Most of these types do not exist yet —
/// <c>M1</c> creates them (<c>30</c> §11.4). Every rule that needs one looks it up by
/// name: absent, the rule's subject set is empty and the rule holds; present, the
/// rule asserts. That is what makes the suite bite the moment M1 lands, without a
/// single <c>Skip</c>.
/// </summary>
internal static class Domain
{
    internal const string CoreNamespace = "SlayIdleRepeat.Core";
    internal const string PrimitivesNamespace = "SlayIdleRepeat.Core.Primitives";
    internal const string ContentNamespace = "SlayIdleRepeat.Core.Content";
    internal const string RngNamespace = "SlayIdleRepeat.Core.Rng";
    internal const string ModelNamespace = "SlayIdleRepeat.Core.Model";
    internal const string SnapshotsNamespace = "SlayIdleRepeat.Core.Model.Snapshots";
    internal const string GuildModelNamespace = "SlayIdleRepeat.Core.Model.Guild";
    internal const string RulesNamespace = "SlayIdleRepeat.Core.Rules";
    internal const string CombatRulesNamespace = "SlayIdleRepeat.Core.Rules.Combat";
    internal const string StatsRulesNamespace = "SlayIdleRepeat.Core.Rules.Stats";
    internal const string CommandsNamespace = "SlayIdleRepeat.Core.Commands";
    internal const string EventsNamespace = "SlayIdleRepeat.Core.Events";
    internal const string HandlersNamespace = "SlayIdleRepeat.Core.Handlers";
    internal const string TestingNamespace = "SlayIdleRepeat.Core.Testing";

    internal const string PortsNamespace = "SlayIdleRepeat.Application.Ports";

    // Type names the rules key on. Looked up, never assumed to exist.
    internal const string GameRulesType = "GameRules";
    internal const string ApplyMethod = "Apply";
    internal const string GameCommandType = "GameCommand";
    internal const string InMemoryGameType = "InMemoryGame";
    internal const string CurrencyChangedEvent = "CurrencyChanged";
    internal const string CurrencyIdType = "CurrencyId";
    internal const string EntitlementsType = "Entitlements";
    internal const string GameContextType = "GameContext";
    internal const string GuildViewType = "GuildView";
    internal const string GhostSnapshotType = "GhostSnapshot";
    internal const string ClockPortType = "IClockPort";

    /// <summary>
    /// The two <c>Rules</c> types <c>30</c> §11.2 documents as public, each with a named
    /// external consumer: the client's local battle simulation (<c>14</c> §2.4) and the
    /// Hero screen's power readout (<c>29</c> §1).
    /// </summary>
    internal static IReadOnlyList<string> PublicRuleTypes { get; } = new[] { "CombatSimulator", "PowerCalculator" };

    /// <summary>Every type in <c>SlayIdleRepeat.Core</c>.</summary>
    internal static IReadOnlyList<TypeDefinition> CoreTypes { get; } =
        Il.AllTypes(ProductionAssemblies.CoreModule).ToArray();

    /// <summary>Every type in <c>SlayIdleRepeat.Application</c>.</summary>
    internal static IReadOnlyList<TypeDefinition> ApplicationTypes { get; } =
        Il.AllTypes(ProductionAssemblies.ApplicationModule).ToArray();

    /// <summary>The one type in <c>Core</c> with the given simple name, or <c>null</c> when M1 has not created it yet.</summary>
    internal static TypeDefinition? FindInCore(string simpleName) =>
        CoreTypes.FirstOrDefault(t => t.Name.Equals(simpleName, StringComparison.Ordinal));

    /// <summary>Every type in <c>Core</c> under the given namespace or below it.</summary>
    internal static IEnumerable<TypeDefinition> CoreTypesUnder(string namespacePrefix) =>
        CoreTypes.Where(t => Il.IsUnder(Il.NamespaceOf(t), namespacePrefix));

    /// <summary>Every port interface — the interfaces under <c>SlayIdleRepeat.Application/Ports/</c> (<c>23</c> §2.2).</summary>
    internal static IReadOnlyList<TypeDefinition> Ports { get; } =
        ApplicationTypes.Where(t => t.IsInterface && Il.IsUnder(Il.NamespaceOf(t), PortsNamespace))
                        .OrderBy(t => t.FullName, StringComparer.Ordinal)
                        .ToArray();

    /// <summary>True when the type is compiler-generated and therefore not the author's business.</summary>
    internal static bool IsCompilerGenerated(TypeDefinition type) =>
        type.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    /// <summary>True when the member is compiler-generated (record plumbing, backing fields, lambdas).</summary>
    internal static bool IsCompilerGenerated(ICustomAttributeProvider member) =>
        member.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    /// <summary>True when a type derives — at any depth — from a type with the given simple name.</summary>
    internal static bool DerivesFrom(TypeDefinition type, string baseSimpleName)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.Name.Equals(baseSimpleName, StringComparison.Ordinal))
            {
                return true;
            }

            current = current.Resolve()?.BaseType;
        }

        return false;
    }
}
