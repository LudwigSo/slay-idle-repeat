using Mono.Cecil;

namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// The names the rules are written against. Every rule that needs one looks it up by name: absent,
/// the rule's subject set is empty and the rule holds; present, the rule asserts. That is what made
/// the suite bite the moment M1 landed, without a single <c>Skip</c>.
/// </summary>
/// <remarks>
/// 🔒 <b>M1-12 corrected "most of these types do not exist yet — M1 creates them".</b> It is now
/// inverted: of the type-name constants below, exactly <b>two</b> name types that do not exist —
/// <see cref="GuildViewType"/> (M14) and <see cref="GhostSnapshotType"/> (M12) — and both are
/// tracked in <c>SubjectSetFloorTests.Pending</c> with the milestone that brings them.
/// <see cref="ClockPortType"/> is a third absence and a permanent one: `30` §3 makes it the name
/// that must NEVER appear in <c>Core</c>, so it is correctly in neither register. Everything else
/// resolves. The count is deliberately not restated as a number in prose beside the list that
/// carries it — the mistake this milestone made three times over
/// <c>Core_internal_layering_holds</c>' row count — but "most do not exist" was wrong in
/// <em>direction</em>, which is worse than being wrong by one.
/// </remarks>
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
    /// out of a fixed table, so a type under a namespace that is in no row of that table is
    /// matched by nothing at all — a new <c>Core/Foo/</c> would be an ungoverned region with
    /// the layering rule still green. Naming the permitted set instead makes the next unlisted
    /// namespace a build failure rather than a silent gap.
    /// <para>
    /// 🔒 <b>It said "five-row" until M1-11 and had been wrong since M1-06</b>, which added the
    /// <c>Commands</c> row; M1-11 added the <c>Events</c> and <c>Testing</c> rows and the count is
    /// now left to the table rather than transcribed a third time (steering <b>S4</b>'s known
    /// limit — a number in prose beside the thing it counts is a number that goes stale silently).
    /// </para>
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
    /// 🔒 The <b>third</b> sanctioned <c>DeterministicRng</c> construction site — `14` §8.1's combat
    /// regime, landed closing the gap the M1/M2 merge surfaced (M2's <c>BattleSimulation</c> and
    /// <c>EncounterFight</c> had been constructing the stream directly, unchecked by this rule until
    /// the two milestones' code met). An IDENTITY floor for the same reason
    /// <see cref="RunRngScopeType"/> and <see cref="MetaDrawScopeType"/> are: a count-only floor stays
    /// satisfied by either of the other two alone while the combat regime stops opening streams
    /// entirely.
    /// </summary>
    internal const string BattleRngScopeType = "BattleRngScope";

    /// <summary>
    /// The <c>Rules</c> types that may be public: three documented entry points — <c>30</c> §11.2's
    /// two, the client's local battle simulation (<c>14</c> §2.4) and the Hero screen's power
    /// readout (<c>29</c> §1), plus M7-05b's board projection for the client's Board screen (S05) —
    /// and the enumerated signature closure of each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>R15 — <c>30</c> §11.2 enumerates public ENTRY POINTS, not the closure of the public
    /// surface.</b> It reads <i>"the only two <c>Rules</c> types that are public"</i>, and <c>05</c>
    /// §7 separately declares <c>SimulationResult</c>, <c>CombatEvent</c> and <c>CombatEventType</c>
    /// public because <c>05</c> §8 has the client replay the log and <c>11</c> §6 has the backend
    /// recompute <c>LogHash</c> over it. C# forces the same conclusion: a public
    /// <c>CombatSimulator.Simulate</c> returning an internal <c>SimulationResult</c> does not compile
    /// (CS0050/CS0051). A public entry point's parameter and return types are public BY CONSEQUENCE
    /// — a language requirement, not a new design exception. Nothing §11.2 protects is weakened: its
    /// actual claim is that <c>GameRules.Apply</c> is the only public way to change state, and none
    /// of the types below mutate anything.
    /// </para>
    /// <para>
    /// 🔒 <b>R16 — the list is ENUMERATED, never a blanket "anything reachable from a public
    /// type".</b> The first six are exactly the signature closure of
    /// <c>Simulate(ulong, ActorStats, int, IReadOnlyList&lt;ActorStats&gt;, int)</c> and its
    /// <c>SimulationResult</c> return. A blanket rule would let a third public entry point appear
    /// without anyone deciding to add one; enumerated, it costs a line in this diff. Everything a
    /// fight can carry beyond a stat block and a level goes through the INTERNAL
    /// <c>Simulate(BattlePlan)</c>, which is why <c>EffectDefinition</c>, <c>StatCaps</c> and the
    /// seam interfaces are not here.
    /// </para>
    /// <para>
    /// ⚠️ <c>ActorStats</c> stays in <c>Core/Rules/Stats/</c>. Relocating it to <c>Content/</c> was
    /// considered and rejected: <c>30</c> §11.4 defines <c>Content/</c> as "ContentSnapshot + every
    /// definition type", and <c>ActorStats</c> is a COMPUTED VALUE, not a definition — the move would
    /// also drag <c>StatRounding</c> across the layering boundary to solve a visibility problem that
    /// visibility solves.
    /// </para>
    /// <para>
    /// 🔒 <b>M7-05b adds a THIRD entry point, <c>BoardView</c>, and its four signature types.</b> Its
    /// named consumer is the client's Board screen (S05) and its Die Panel: `03` §1.1's board is
    /// deliberately never persisted — it regenerates from <c>RunSeed</c> on every command — so before
    /// this widening no assembly outside <c>Core</c> could see a tile track or a fork preview at all,
    /// and the Board screen could draw neither. <c>BoardView.Project</c> replays that layout and
    /// hands back a read-only projection: <c>BoardTrackNode</c> and <c>BoardFork</c> are its return
    /// shapes, and <c>TileKind</c> and <c>ForkLabel</c> are public by the same CONSEQUENCE R15
    /// records — a public member returning an internal enum does not compile.
    /// </para>
    /// <para>
    /// ⚠️ <b>The producer is deliberately not here.</b> <c>BoardGenerator</c>, <c>BoardGraph</c>,
    /// <c>BoardNode</c>, <c>BoardEdge</c>, <c>MovementEngine</c>, <c>BoardResolution</c> and
    /// <c>ForkPreview</c> stay <c>internal</c>: what M7-05b exports is the VIEW, not the machinery
    /// that decides a board, and <c>BoardViewSurfaceRuleTests</c> is what keeps the two apart. The
    /// entry point takes a <c>RunSnapshot</c> and a <c>ContentSnapshot</c> — both already public —
    /// and hands out no draw stream, so nothing outside <c>Core</c> gains a way to generate a board
    /// or to move a run along one.
    /// </para>
    /// <para>
    /// 🔒 <b>M7-06b adds a FOURTH and FIFTH entry point, <c>HeroBuild</c> and <c>RunBattle</c>, and
    /// neither adds a signature type.</b> <c>HeroBuild</c>'s named consumer is the client's Hero and
    /// Inventory screens, whose side-by-side stat delta IS the gear derivation; <c>RunBattle</c>'s is
    /// the Application layer's <c>SimulatePendingBattleUseCase</c>, which is what lets a run leave
    /// <c>BattlePending</c> at all — before it, the hero's stat block could not be built at any
    /// accessibility, so a run entering a battle could never produce the fight it was standing in.
    /// Their public members name only types that were already public: <c>ActorStats</c>,
    /// <c>SimulationResult</c>, <c>EffectDefinition</c>, <c>GearInstance</c>, <c>PlayerSnapshot</c>,
    /// <c>RunSnapshot</c> and <c>ContentSnapshot</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>The machinery stays internal here too.</b> <c>StatAggregation</c>,
    /// <c>AggregatedStats</c>, <c>HeroBaseCurve</c>, <c>GearStatDerivation</c>, <c>GearCatalogue</c>,
    /// <c>LoadoutRules</c>, <c>EncounterFight</c>, <c>BossFight</c>, <c>BattlePlan</c> and
    /// <c>ActorPlan</c> are all still <c>internal</c>, and <c>HeroBattleSurfaceRuleTests</c> is what
    /// keeps them there. <c>HeroBuild</c> is a <c>class</c> rather than a <c>record</c> precisely so
    /// its aggregate can stay internal: a positional record's parameters are public properties, and
    /// exporting <c>AggregatedStats</c> would have exported the battle pipeline's heal ceiling with
    /// it.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> PublicRuleTypes { get; } = new[]
    {
        "CombatSimulator",
        "PowerCalculator",
        "SimulationResult",
        "CombatEvent",
        "CombatEventType",
        "ActorStats",
        "BoardView",
        "BoardTrackNode",
        "BoardFork",
        "TileKind",
        "ForkLabel",
        "HeroBuild",
        "RunBattle",
    };

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
