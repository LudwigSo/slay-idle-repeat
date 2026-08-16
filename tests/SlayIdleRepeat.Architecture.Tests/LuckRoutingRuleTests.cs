using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 <b>`24` §11 — every protected grant is routed through <c>LuckService</c>, and a guarantee can
/// fire in exactly one place.</b> No method in <c>Core</c> outside <c>Rules.Luck</c> produces a
/// grant outcome without going through the façade, and no type outside <c>Rules.Luck</c> may name
/// the guarantee primitives at all.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this rule did not exist and had to.</b> `24` §11's requirement is a <em>routing</em>
/// claim — <em>"exactly one place in the codebase where a guarantee can fire"</em> — and nothing in
/// the suite could express one. <c>Handlers_and_Rules_are_internal</c> governs accessibility,
/// <c>Core_internal_layering_holds</c> and R17 govern which namespace may name which, and all three
/// are satisfied by a chest opener that draws its own rarity out of a weighted table and never
/// mentions pity. That is not a hypothetical shape: it is the shortest path to a working chest, and
/// the counter it skips is invisible until a player complains that a hundred and sixty chests
/// produced nothing.
/// </para>
/// <para>
/// <b>What it buys, concretely.</b> Five grant paths are still unbuilt — chests, eggs, crates, the
/// wheel and the perk draft's five composition rules — and each will be written by a different task
/// in a different milestone. This rule is what makes the fifth of them fail on the commit that adds
/// it rather than on the commit that finally reads `24` again.
/// </para>
/// <para>
/// ⚠️ <b>WHAT THIS RULE CANNOT SEE, listed rather than implied.</b>
/// </para>
/// <list type="bullet">
///   <item>A producer that hides its outcome behind a type this file does not enumerate — an
///   <c>object</c>, a tuple of primitives, a <c>string</c> item id.
///   <see cref="GrantOutcomeTypes"/> is a hand-written closed list on purpose (a blanket "anything
///   under Model" would fire on every aggregate accessor in the assembly), and the cost of that
///   choice is exactly this hole.</item>
///   <item>A method that names <c>LuckService</c> for some other reason and produces its outcome
///   another way. "Routes through" is answered from the call graph one level deep; a laundering
///   indirection defeats it.</item>
///   <item>A <c>const</c> read across the boundary in arm 2 — the compiler folds it, so no type
///   reference survives into metadata. The same hole <c>IntraRulesLayeringRuleTests</c> records,
///   and narrow for the same reason: none of the four guarantee types declares a <c>const</c> a
///   caller outside the namespace could want.</item>
/// </list>
/// <para>
/// 🔒 The namespace constant is <b>read</b> from <c>IntraRulesLayeringRuleTests</c> rather than
/// restated here. That file has to declare it anyway — R17 fails on an ungoverned sub-namespace of
/// <c>Rules</c> — and two spellings of one namespace is one rename away from a rule that governs a
/// namespace nobody has.
/// </para>
/// </remarks>
public sealed class LuckRoutingRuleTests
{
    /// <summary>`24` §11's façade: the one type a protected grant is resolved through.</summary>
    internal const string LuckFacade = "LuckService";

    /// <summary>The façade member `24` §11's routing claim is about — the identity floor's subject.</summary>
    private const string ResolveMember = "Resolve";

    /// <summary>The namespace the façade and its primitives live in, read from R17's own declaration.</summary>
    private const string LuckNamespace = IntraRulesLayeringRuleTests.LuckNamespace;

    /// <summary>
    /// 🔒 The closed list of type simple names that <b>are</b> a grant outcome — what "producing a
    /// grant" means to this rule, stated as an enumeration rather than inferred.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Live today:</b> <c>Rarity</c> (M4-01, the ladder every protected class draws on) and
    /// <c>DraftOption</c> (M3-06, the perk draft's offered option). Both resolve in <c>Core</c> now,
    /// and <see cref="The_routing_rules_subject_set_is_the_one_it_was_written_against"/> asserts that
    /// <c>DraftOption</c> still does — a count-only floor would not notice it going.
    /// </para>
    /// <para>
    /// <b>Pre-registered, and deliberately named before they exist:</b> <c>GearInstance</c> (M4-03),
    /// <c>PetDefinition</c> (M4-07) and <c>MountDefinition</c> (M4-08). All three are already tracked
    /// as pending subjects, so the names are the ones those milestones have committed to rather than
    /// inventions. Registering them now is the whole point of the rule: the day one lands, its
    /// producer either routes through the façade or turns the build red, instead of shipping a
    /// grant path nobody remembered to protect.
    /// </para>
    /// <para>
    /// ⚠️ Matched by <b>simple name</b>, so a differently-named clone is invisible. That is the
    /// price of naming three types that do not exist yet; the alternative is a rule that cannot be
    /// written until M4-08.
    /// </para>
    /// </remarks>
    private static readonly string[] GrantOutcomeTypes =
    {
        "Rarity",
        "DraftOption",
        "GearInstance",
        "PetDefinition",
        "MountDefinition",
    };

    /// <summary>
    /// 🔒 The guarantee primitives: the four types that decide, ramp, bank or reshape a protected
    /// draw. Naming one from outside <c>Rules.Luck</c> is a second place a guarantee can fire.
    /// </summary>
    private static readonly string[] GuaranteePrimitives =
    {
        "HardPity",
        "SoftPity",
        "MercyAccrual",
        "RarityTable",
    };

    /// <summary>
    /// 🔒 The enumerated, closed exemption list for arm 1 — types that carry a grant-outcome name in
    /// a signature without granting anything.
    /// </summary>
    /// <remarks>
    /// Each entry has to justify itself:
    /// <see cref="Every_exempted_producer_still_needs_its_exemption"/> proves the entry is still
    /// load-bearing, so an exemption whose subject stopped tripping the rule is a build failure
    /// rather than a line nobody deletes.
    /// </remarks>
    private static readonly (string Type, string Reason)[] RoutingExemptions =
    {
        // 🔒 The tuning reader and its payload rows. Reading a rarity OUT of luck.json is not
        // granting one — these types carry the authored vocabulary the façade then draws against,
        // and Content sits beneath Rules, so a Content type calling into Rules.Luck would invert the
        // layering outright. There is no design in which this exemption goes away.
        // 🔴 A fourth Content row — `PityLadder` — was written here and DELETED before this landed,
        // because the companion fact reported it as an exemption covering nothing: a ladder carries
        // its rungs and its curve, and neither of those types is a grant outcome, so it never
        // tripped the routing arm in the first place. That is the mechanism working on its own
        // author, and it is worth one line: an exemption added "to be safe" is a rule quietly
        // narrowed, and this file cannot hold one.
        ("LuckTuning", "the pity registry itself — it reads the authored ladders and forms the counter keys the façade draws against; Content sits BENEATH Rules, so routing a Content read through Rules.Luck is not merely unnecessary, it is the layering inverted"),
        ("HardPityStep", "one authored rung of a ladder — a data row carrying its guarantee rarity, read by LuckTuning"),

        // 🔒 M4-01b's, and named with its owner rather than exempted quietly. The perk draft's five
        // 24 §4.7 rules — Legendary pity, the anti-brick Sustain guarantee, the quality floor, the
        // Codex bias and the upgrade famine — are the ONE live grant path that does not route yet,
        // and every one of those five is a guarantee in the sense this rule is about.
        // DraftCompositionRules already declares all eight seams as documented no-ops. When M4-01b
        // wires them through the façade these three entries come out, and the companion fact below
        // fails on the day they stop being needed.
        ("PerkDraftEngine", "M4-01b — the 24 §4.7 draft rules are declared as no-op seams and not yet wired to the façade; this is the one LIVE grant path that still bypasses it, and it is exempted with an owner rather than passed over"),
        ("PickPerk", "M4-01b — the handler that calls the draft engine, and it inherits the engine's exemption for exactly as long as the engine has one"),
    };

    /// <summary>
    /// 🔒 `24` §11 — <b>no method in <c>Core</c> outside <c>Rules.Luck</c> produces a grant outcome
    /// without routing through <see cref="LuckFacade"/>.</b> Every gear, pet, mount, rarity or draft
    /// option a player is handed came out of the one place a guarantee can fire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An IL scan over method signatures rather than a source grep, for <c>Il</c>'s stated reason —
    /// a grep is defeated by a <c>using</c> alias, a fully-qualified call or an extension method,
    /// and it fires inside the comments of the very files that explain at length why they do not
    /// grant anything.
    /// </para>
    /// <para>
    /// The declaring type of a grant-outcome type is excluded from the subject set: a record's own
    /// constructor and equality members mention their own type in every signature, and reporting
    /// them would make the rule noise rather than a rule.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_grant_outcome_is_produced_outside_the_luck_service()
    {
        var exempted = RoutingExemptions.Select(e => e.Type).ToArray();

        var offenders = ScannedMethods()
            .Where(subject => !exempted.Contains(subject.Type.Name, StringComparer.Ordinal))
            .Where(subject => BypassesTheFacade(subject.Method, LuckFacade))
            .Select(subject =>
                $"{Il.Describe(subject.Method)} produces a grant outcome " +
                $"({string.Join(", ", GrantOutcomeNamesIn(subject.Method))}) and never names {LuckFacade}. " +
                "24 §11 puts every protected grant behind one façade: a producer that draws its own " +
                "rarity skips the counter, and a skipped counter is invisible until a player has " +
                "opened a hundred and sixty chests for nothing. Route the draw through " +
                $"{LuckFacade}.{ResolveMember}, or — if this genuinely grants nothing — add it to " +
                "RoutingExemptions with the task that owns wiring it.")
            .ToList();

        ArchRule.Empty(
            offenders,
            "24 §11: every grant outcome in Core is produced through LuckService, the one place a " +
            "guarantee can fire.");
    }

    /// <summary>
    /// 🔒 `24` §11 — <b>a guarantee can fire in exactly one place.</b> No type outside
    /// <c>Rules.Luck</c> names <c>HardPity</c>, <c>SoftPity</c>, <c>MercyAccrual</c> or
    /// <c>RarityTable</c>: the façade is the sole door onto all four.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The companion to the routing arm, and the half that keeps it honest. Arm 1 says a producer
    /// must name the façade; without this one, a producer could name the façade <em>and</em> reach
    /// past it into <c>HardPity.Fires</c> directly, at which point "one place" is two and the second
    /// is wherever the next author found it convenient.
    /// </para>
    /// <para>
    /// <b>Non-vacuous today, in both directions.</b> The subject set is every type in <c>Core</c>
    /// outside one namespace — hundreds — and all four target types resolve, which
    /// <see cref="The_routing_rules_subject_set_is_the_one_it_was_written_against"/> asserts by
    /// identity rather than by count.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_guarantee_can_only_fire_inside_Rules_Luck()
    {
        var guarded = GuaranteePrimitives
            .Select(name => LuckNamespace + "." + name)
            .ToArray();

        var offenders = new List<string>();

        foreach (var type in Il.AllTypes(ProductionAssemblies.CoreModule)
                     .Where(t => !Domain.IsCompilerGenerated(t))
                     .Where(t => !Il.IsUnder(Il.NamespaceOf(t), LuckNamespace)))
        {
            offenders.AddRange(
                Il.ReferencedTypeNames(type)
                    .Where(referenced => guarded.Contains(referenced, StringComparer.Ordinal))
                    .Select(referenced =>
                        $"{type.FullName} names {referenced}. 24 §11 puts the guarantee decision in " +
                        $"one place; {LuckFacade} is the only type permitted to reach it, and a " +
                        "second caller is a second place a guarantee can fire — which is how two " +
                        "chests at the same counter value get different answers."));
        }

        ArchRule.Empty(
            offenders,
            "24 §11: HardPity, SoftPity, MercyAccrual and RarityTable are reachable only from " +
            "Rules.Luck, so LuckService is the sole façade over them.");
    }

    /// <summary>
    /// 🔒 `24` §11 / `23` §6 — <b>the identity floor</b> under both arms above (steering S3). Each is
    /// "no member of set S does X", which passes vacuously when S empties, and both sets can empty
    /// through a rename that nothing else in the suite would notice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>An identity floor, not only a count.</b> A count over <c>Core</c>'s types is satisfied
    /// by the other nine hundred while the façade itself is folded into a handler; a count over
    /// <see cref="GrantOutcomeTypes"/> is satisfied while the one live member of it disappears. So
    /// this asserts the names: the façade exists, it declares <see cref="ResolveMember"/>, the four
    /// guarantee primitives are declared inside the namespace the arm above trusts, and
    /// <c>DraftOption</c> — the grant-outcome name that is live and is <em>not</em> M4-01's own —
    /// still resolves in <c>Core</c>.
    /// </para>
    /// <para>
    /// The count is a floor rather than an equality, set well below the tree it was written against,
    /// so adding or removing a type is not a test edit.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_routing_rules_subject_set_is_the_one_it_was_written_against()
    {
        var offenders = new List<string>();

        var facade = Domain.FindInCore(LuckFacade);

        if (facade is null)
        {
            offenders.Add(
                $"'{LuckFacade}' is not declared in Core. Both rules in this file are stated over it " +
                "— the routing arm asks whether a producer names it, the guarantee arm exempts its " +
                "namespace — so without it the routing arm reports every producer an offender and " +
                "the guarantee arm governs a namespace nobody writes into. If the façade was " +
                "renamed, rename LuckFacade in the same commit.");
        }
        else
        {
            if (!Il.IsUnder(Il.NamespaceOf(facade), LuckNamespace))
            {
                offenders.Add(
                    $"'{LuckFacade}' is declared in {Il.NamespaceOf(facade)}, not under {LuckNamespace}. " +
                    "The guarantee arm exempts a NAMESPACE, so a façade that moved out of it would be " +
                    "reported as an offender by the rule it is the subject of.");
            }

            if (!Il.AllMethods(facade).Any(m => m.Name.Equals(ResolveMember, StringComparison.Ordinal)))
            {
                offenders.Add(
                    $"'{LuckFacade}' declares no member named '{ResolveMember}'. The routing arm's " +
                    "failure message tells an author to route through it, and a rule whose remedy " +
                    "does not exist is a rule nobody can obey.");
            }
        }

        foreach (var primitive in GuaranteePrimitives)
        {
            var type = Domain.FindInCore(primitive);

            if (type is null || !Il.IsUnder(Il.NamespaceOf(type), LuckNamespace))
            {
                offenders.Add(
                    $"the guarantee primitive '{primitive}' is not declared under {LuckNamespace}. " +
                    "A_guarantee_can_only_fire_inside_Rules_Luck forbids a name that nothing " +
                    "declares, which is an empty prohibition — the rule would report success over " +
                    "every type in Core while the guarantee moved somewhere else entirely.");
            }
        }

        // 🔒 The one grant-outcome name that is live and is not this milestone's own. Three of the
        // five are pre-registered for milestones that have not run, so a floor over the LIST would
        // be satisfied by names nothing declares; this is the member that makes the routing arm
        // quantify over a real production shape today.
        if (Domain.FindInCore("DraftOption") is null)
        {
            offenders.Add(
                "'DraftOption' resolves to no type in Core. It is the live member of " +
                "GrantOutcomeTypes that M4-01 did not author, so its absence means the routing arm " +
                "is matching only names that no production type carries — a rule quantifying over " +
                "nothing while reporting success. If the draft's option type was renamed, rename it " +
                "in GrantOutcomeTypes in the same commit.");
        }

        var scanned = ScannedTypes().Count();

        if (scanned < ScannedTypeFloor)
        {
            offenders.Add(
                $"the routing arm scans {scanned} types in Core outside {LuckNamespace}; the floor " +
                $"is {ScannedTypeFloor}. Both arms are stated over this set, so a set this small " +
                "means Core has been split, renamed or moved and the rules are governing whatever " +
                "is left. If the set legitimately shrank — a namespace genuinely moved out of " +
                "SlayIdleRepeat.Core — lower this floor in the same commit and say in the message " +
                "which types went and where, rather than deleting the check.");
        }

        ArchRule.Empty(
            offenders,
            "24 §11's routing rule is stated over the types, the façade and the guarantee primitives " +
            "it was written against (23 §6).");
    }

    /// <summary>
    /// 🔒 `24` §11 / `23` §6 — <b>every exemption is still needed.</b> An exempted type that no
    /// longer trips the routing arm is a stale exemption, and a stale exemption is a rule quietly
    /// narrowed.
    /// </summary>
    /// <remarks>
    /// This is what makes <see cref="RoutingExemptions"/> a list with an expiry rather than a
    /// suppression file. The two draft entries are the ones that matter: when M4-01b routes the
    /// draft through the façade, this rule fails and the entries have to come out in that commit.
    /// </remarks>
    [Fact]
    public void Every_exempted_producer_still_needs_its_exemption()
    {
        var offenders = new List<string>();

        foreach (var (type, reason) in RoutingExemptions)
        {
            var methods = ScannedMethods()
                .Where(subject => subject.Type.Name.Equals(type, StringComparison.Ordinal))
                .ToArray();

            if (methods.Length == 0)
            {
                offenders.Add(
                    $"'{type}' is exempted from the routing rule, but no such type is scanned in Core " +
                    "at all. It was renamed, moved out of Core or deleted, and the exemption now " +
                    $"covers nothing. Reason on the row: {reason}");
                continue;
            }

            if (!methods.Any(subject => BypassesTheFacade(subject.Method, LuckFacade)))
            {
                offenders.Add(
                    $"'{type}' is exempted from the routing rule and no longer trips it — every " +
                    "grant-outcome-producing method on it now routes through " + LuckFacade + ", or " +
                    "it has stopped producing one. Delete the row in the same commit that made it " +
                    $"unnecessary. Reason on the row: {reason}");
            }
        }

        ArchRule.Empty(
            offenders,
            "Every routing exemption still covers a producer that genuinely bypasses LuckService " +
            "(24 §11, 23 §6).");
    }

    /// <summary>
    /// 🔒 `24` §11 / `23` §6 — <b>the teeth</b> (steering S1). The routing predicate is driven
    /// against real IL compiled from <see cref="LuckRoutingFixtures"/>: it must catch a producer that
    /// bypasses the façade, pass one that routes through it, and pass a method that grants nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixtures live in this assembly rather than in <c>Core</c> because the shape that matters
    /// <em>is</em> the violation, and a violation is never committed to the domain to prove a rule
    /// works — the same construction <c>AccessibilityBoundaryTests</c> and <c>DomainPurityTests</c>
    /// both use.
    /// </para>
    /// <para>
    /// The third case carries as much weight as the first two. Without it the predicate could be
    /// keyed on "does not call <c>LuckService</c>" alone, which is true of almost every method in the
    /// assembly, and the rule would be a wall of false failures that gets deleted rather than obeyed.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_routing_predicate_catches_a_bypassing_producer_and_passes_a_routed_one()
    {
        Assert.True(
            BypassesTheFacade(Fixture(nameof(LuckRoutingFixtures.GrantsWithoutRouting)), LuckFacade),
            "a method that answers a grant outcome and never names the façade is the exact shape " +
            "24 §11 forbids. If this is false the routing arm is matching nothing and reports " +
            "success over every producer in Core.");

        Assert.False(
            BypassesTheFacade(Fixture(nameof(LuckRoutingFixtures.GrantsThroughTheFacade)), LuckFacade),
            "resolving through the façade is precisely what the rule asks for. If this is true the " +
            "rule flags the compliant path as well as the bypass, and would be weakened back out " +
            "within a commit rather than obeyed.");

        Assert.False(
            BypassesTheFacade(Fixture(nameof(LuckRoutingFixtures.GrantsNothing)), LuckFacade),
            "a method that produces no grant outcome is not a bypass, however little it mentions the " +
            "façade. If this is true the predicate is keyed on the absent call alone and would " +
            "report every method in the assembly.");
    }

    /// <summary>
    /// Types in <c>Core</c> the routing arm quantifies over: everything outside <c>Rules.Luck</c>,
    /// less the compiler's own and less the grant-outcome types themselves.
    /// </summary>
    private static IEnumerable<TypeDefinition> ScannedTypes() =>
        Il.AllTypes(ProductionAssemblies.CoreModule)
          .Where(t => !Domain.IsCompilerGenerated(t))
          .Where(t => !Il.IsUnder(Il.NamespaceOf(t), LuckNamespace))
          .Where(t => !GrantOutcomeTypes.Contains(t.Name, StringComparer.Ordinal));

    /// <summary>Every author-written method on a scanned type, with the type it belongs to.</summary>
    private static IEnumerable<(TypeDefinition Type, MethodDefinition Method)> ScannedMethods() =>
        ScannedTypes().SelectMany(
            type => Il.AllMethods(type)
                      .Where(m => !Domain.IsCompilerGenerated(m))
                      .Select(m => (Type: type, Method: m)));

    /// <summary>
    /// True for a method that answers or accepts a grant outcome and never names the façade.
    /// </summary>
    /// <remarks>
    /// Takes the façade's name rather than reading <see cref="LuckFacade"/>, so the teeth check drives
    /// the same predicate the rule does instead of a second copy of it.
    /// </remarks>
    private static bool BypassesTheFacade(MethodDefinition method, string facade) =>
        GrantOutcomeNamesIn(method).Length > 0 && !RoutesThrough(method, facade);

    /// <summary>The grant-outcome names a method's signature mentions, flattened through generics.</summary>
    /// <remarks>
    /// Signature types rather than the return type alone: a producer that hands its outcome back
    /// through an <c>out</c> parameter or a builder argument is producing one just as surely as one
    /// that returns it, and both spellings are ordinary C#.
    /// </remarks>
    private static string[] GrantOutcomeNamesIn(MethodDefinition method) =>
        Il.SignatureTypes(method)
          .SelectMany(Il.Flatten)
          .Select(reference => reference.Name)
          .Where(name => GrantOutcomeTypes.Contains(name, StringComparer.Ordinal))
          .Distinct(StringComparer.Ordinal)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();

    /// <summary>True when a method body calls a member of a type with the façade's simple name.</summary>
    private static bool RoutesThrough(MethodDefinition method, string facade) =>
        Il.Instructions(method).Any(instruction =>
            instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj &&
            instruction.Operand is MethodReference callee &&
            callee.DeclaringType.Name.Equals(facade, StringComparison.Ordinal));

    /// <summary>
    /// The floor under the routing arm's subject set — <c>Core</c>'s types outside
    /// <c>Rules.Luck</c>. 466 on the commit this rule landed, measured rather than estimated (the
    /// floor was briefly raised past it and the rule's own message reported the count); set well
    /// below so adding or removing a type is not a test edit.
    /// </summary>
    private const int ScannedTypeFloor = 400;

    /// <summary>One fixture method, by name, out of this assembly's own metadata.</summary>
    private static MethodDefinition Fixture(string name) =>
        SuiteAssembly.Type(nameof(LuckRoutingFixtures))
                     .Methods.Single(m => m.Name.Equals(name, StringComparison.Ordinal));

    /// <summary>
    /// The three IL shapes the routing predicate has to tell apart. They live here rather than in
    /// <c>Core</c> because the first one is the violation.
    /// </summary>
    private static class LuckRoutingFixtures
    {
        /// <summary>🔴 Answers a grant outcome and never names the façade — the shape `24` §11 forbids.</summary>
        /// <returns>A grant, drawn out of nowhere.</returns>
        internal static GearInstance GrantsWithoutRouting() => new();

        /// <summary>Answers the same grant outcome, resolved through the façade. Must not be flagged.</summary>
        /// <returns>A grant the façade decided.</returns>
        internal static GearInstance GrantsThroughTheFacade() => LuckService.Resolve();

        /// <summary>
        /// Produces no grant outcome at all. The case that keeps the predicate from being keyed on
        /// the absent call alone.
        /// </summary>
        /// <returns>An ordinary number.</returns>
        internal static long GrantsNothing() => 1L;

        /// <summary>
        /// Stands in for M4-03's gear instance — a <b>pre-registered</b> member of
        /// <see cref="GrantOutcomeTypes"/>, chosen precisely because <c>Core</c> declares no such
        /// type, so the fixture cannot be confused with a production one.
        /// </summary>
        internal sealed class GearInstance;

        /// <summary>Stands in for the façade, so the routed fixture has something real to call.</summary>
        private static class LuckService
        {
            /// <summary>Resolves a grant.</summary>
            /// <returns>The grant.</returns>
            internal static GearInstance Resolve() => new();
        }
    }
}
