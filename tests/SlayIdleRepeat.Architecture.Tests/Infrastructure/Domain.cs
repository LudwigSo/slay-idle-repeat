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

    /// <summary>
    /// Every namespace <c>30</c> §11.4 enumerates for <c>SlayIdleRepeat.Core</c>, plus the
    /// <c>SlayIdleRepeat.Core</c> root itself, which is where <c>GameRules</c> lives.
    /// </summary>
    /// <remarks>
    /// This is the closed list. <c>Core_internal_layering_holds</c> forbids specific pairs
    /// out of a fixed five-row table, so a type under a namespace that is in no row of that
    /// table is matched by nothing at all — a new <c>Core/Foo/</c> would be an ungoverned
    /// region with the layering rule still green. Naming the permitted set instead makes the
    /// next unlisted namespace a build failure rather than a silent gap.
    /// </remarks>
    internal static IReadOnlyList<string> PermittedCoreNamespaces { get; } = new[]
    {
        CoreNamespace,
        PrimitivesNamespace,
        ContentNamespace,
        RngNamespace,
        ModelNamespace,
        RulesNamespace,
        CommandsNamespace,
        EventsNamespace,
        HandlersNamespace,
        TestingNamespace,
    };

    // Type names the rules key on. Looked up, never assumed to exist.
    internal const string GameRulesType = "GameRules";
    internal const string ApplyMethod = "Apply";
    internal const string GameCommandType = "GameCommand";
    internal const string InMemoryGameType = "InMemoryGame";
    internal const string DomainEventType = "DomainEvent";
    internal const string CurrencyChangedEvent = "CurrencyChanged";
    internal const string CurrencyIdType = "CurrencyId";
    internal const string EntitlementsType = "Entitlements";
    internal const string GuildViewType = "GuildView";
    internal const string GhostSnapshotType = "GhostSnapshot";
    internal const string ClockPortType = "IClockPort";

    /// <summary>
    /// 🔒 `14` §8.1's counter-based draw stream. The name
    /// <c>DomainPurityTests.DeterministicRng_is_constructed_only_inside_Core_Rng</c> looks for a
    /// <c>newobj</c> on — rename the type without renaming this and the rule matches nothing, with
    /// every handler free to open its own stream and never write the counter back.
    /// </summary>
    internal const string DeterministicRngType = "DeterministicRng";

    /// <summary>
    /// 🔒 The one sanctioned <see cref="DeterministicRngType"/> construction site, and the identity
    /// floor under the rule above (steering S3): a count-only floor is satisfied by a construction
    /// anywhere, including the one that replaced the scope.
    /// </summary>
    internal const string RunRngScopeType = "RunRngScope";

    /// <summary>
    /// 🔒 The <b>second</b> sanctioned <c>DeterministicRng</c> construction site — `14` §8.1's meta
    /// regime, landed by M1-09. Named for the same reason <see cref="RunRngScopeType"/> is: it is an
    /// IDENTITY floor under <c>DeterministicRng_is_constructed_only_inside_Core_Rng</c>, and a
    /// count-only floor would stay satisfied by <c>RunRngScope</c> alone while the meta regime
    /// stopped opening streams entirely.
    /// </summary>
    internal const string MetaDrawScopeType = "MetaDrawScope";

    /// <summary>
    /// The two <c>Rules</c> types <c>30</c> §11.2 documents as public, each with a named
    /// external consumer: the client's local battle simulation (<c>14</c> §2.4) and the
    /// Hero screen's power readout (<c>29</c> §1).
    /// </summary>
    internal static IReadOnlyList<string> PublicRuleTypes { get; } = new[] { "CombatSimulator", "PowerCalculator" };

    /// <summary>
    /// True when a namespace is one <c>30</c> §11.4 enumerates, or a namespace beneath one.
    /// </summary>
    /// <remarks>
    /// ⚠️ The <c>SlayIdleRepeat.Core</c> root is matched EXACTLY, everything else by prefix.
    /// Prefix-matching the root would make every namespace in the assembly permitted and the
    /// rule that uses this trivially true — the precise failure it was written to close.
    /// </remarks>
    internal static bool IsPermittedCoreNamespace(string ns) =>
        ns.Equals(CoreNamespace, StringComparison.Ordinal) ||
        PermittedCoreNamespaces
            .Where(p => !p.Equals(CoreNamespace, StringComparison.Ordinal))
            .Any(p => Il.IsUnder(ns, p));

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
    /// <remarks>
    /// 🔒 <b>Walks out to the outermost declaring type</b>, exactly as <see cref="Il.NamespaceOf"/>
    /// does, and that is not tidiness. The compiler marks <c>&lt;PrivateImplementationDetails&gt;</c>
    /// with <c>[CompilerGenerated]</c> but does <b>not</b> mark the
    /// <c>__StaticArrayInitTypeSize=N</c> types it nests inside it — and those nest at namespace
    /// <c>""</c>. So the first <c>Core</c> type to compile a static array initialiser
    /// (<c>Player.WalletCurrencies</c>, M1-04) made
    /// <c>Every_Core_type_lives_under_a_documented_namespace</c> fail over a type no author wrote
    /// and no author can move. Measured, red-then-green: without this walk the rule reports
    /// <c>&lt;PrivateImplementationDetails&gt;/__StaticArrayInitTypeSize=24 is in namespace ''</c>.
    /// </remarks>
    internal static bool IsCompilerGenerated(TypeDefinition type)
    {
        var outer = type;
        while (outer is not null)
        {
            if (IsCompilerGenerated((ICustomAttributeProvider)outer))
            {
                return true;
            }

            outer = outer.DeclaringType;
        }

        return false;
    }

    /// <summary>True when the member is compiler-generated (record plumbing, backing fields, lambdas).</summary>
    internal static bool IsCompilerGenerated(ICustomAttributeProvider member) =>
        member.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    /// <summary>
    /// True for the <c>30</c> §7 event hierarchy: a type under <c>Core/Events/</c> that either is
    /// <see cref="DomainEventType"/> or derives from it.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Both halves are load-bearing, and neither is sufficient alone.</b> Matching only the
    /// namespace would exempt a payload record someone dropped into <c>Core/Events/</c>. Matching
    /// only the base type would exempt a <c>Core/Model/</c> aggregate that derived from
    /// <c>DomainEvent</c> — nothing in this suite forbids that, and it would let a real wallet buy
    /// its way out of <c>DomainPurityTests.CurrencyFields()</c> by inheriting from an event.
    /// </remarks>
    internal static bool IsDomainEvent(TypeDefinition type) =>
        Il.IsUnder(Il.NamespaceOf(type), EventsNamespace) &&
        (type.Name.Equals(DomainEventType, StringComparison.Ordinal) ||
         DerivesFrom(type, DomainEventType));

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
