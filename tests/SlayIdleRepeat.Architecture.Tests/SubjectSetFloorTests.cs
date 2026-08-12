using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `23` §6 — the rules that watch the other rules' subject sets.
/// </summary>
/// <remarks>
/// <para>
/// Every rule in this suite is of the shape "no member of set S does X". Each one is
/// written to pass vacuously while S is empty, and that is deliberate: it is what lets a
/// rule be authored in M0 against a type M1 has not created, with no <c>Skip</c> and
/// nothing to remember to switch on. The cost of that design is a specific, silent
/// failure mode — <b>if S becomes empty for the wrong reason, the rule reports success
/// forever and nothing goes red.</b>
/// </para>
/// <para>
/// Two ways that happens. A set built by a naming filter can be emptied by a rename:
/// <c>AdapterNames</c> is <c>StartsWith("SlayIdleRepeat.Adapters.")</c> over 21 projects,
/// and renaming them to the singular <c>SlayIdleRepeat.Adapter.*</c> would empty it and
/// take five rules green with it, at which point <c>Application</c> could reach a vendor
/// driver through a renamed adapter with the whole `23` §5/§6 isolation block passing. A
/// set built by looking a type up by name can be emptied by M1 choosing a different name:
/// call the command base <c>Command</c> instead of <c>GameCommand</c> and
/// <c>Every_command_type_is_handled_by_Apply</c> short-circuits and reports success over N
/// unhandled commands.
/// </para>
/// <para>
/// So the two rules below pin the floors and the names. The mechanism is the one the repo
/// already uses one directory over — <c>build/ci/test-suites.json</c>'s <c>knownEmpty</c>
/// plus its stale-exemption check, and
/// <c>tests/SlayIdleRepeat.Core.Tests/Model/Snapshots/SnapshotFieldOrderPin.cs</c>, the
/// only expiring exception in the repo with a teeth-check against its own permanent
/// vacuity. A declaration that a subject is missing must expire the moment it arrives.
/// </para>
/// </remarks>
public sealed class SubjectSetFloorTests
{
    /// <summary>
    /// Subjects the rules key on that M1 and later create. Each one is absent today, and
    /// each is the reason some rule is currently vacuous.
    /// </summary>
    /// <remarks>
    /// 🔒 Deleting an entry when the subject arrives is not optional — the rule below fails
    /// on a declared-pending subject that exists. That is the whole mechanism: it converts
    /// "M1 renamed the type and nobody noticed the rule went quiet" into a build failure on
    /// the commit that renames it.
    /// </remarks>
    private static readonly PendingSubject[] Pending =
    {
        new("GameRules", SubjectKind.CoreType, "M1-06",
            "AccessibilityBoundaryTests.Apply_is_the_only_public_mutation, DomainPurityTests.Every_command_type_is_handled_by_Apply"),
        new("GameCommand", SubjectKind.CoreType, "M1-06",
            "DomainPurityTests.Every_command_type_is_handled_by_Apply, AccessibilityBoundaryTests.Contracts_never_redeclares_a_domain_type"),
        new("GuildView", SubjectKind.CoreType, "M14",
            "IsolationTests.GuildView_is_a_read_only_projection"),
        new("InMemoryGame", SubjectKind.CoreType, "M1-11",
            "DomainPurityTests.The_whole_game_is_playable_from_Core_alone"),
        new("CurrencyChanged", SubjectKind.CoreType, "M1-03",
            "DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged"),
        new("GhostSnapshot", SubjectKind.CoreType, "M12",
            "IsolationTests.Guild_state_is_unreachable_from_the_ghost_snapshot"),

        // 🔒 Not a rule subject — a DEFERRAL, recorded in the one register the repo has so that it
        // expires by itself (steering S4). `18` §4 types the TIER condition "enum" and no tier enum
        // exists anywhere in the repository; `02` §2's runSeed derivation is the only place tierId is
        // even named. Steering S6 forbids inventing the members, so M2-05's IRunStateView.Tier ships
        // as the tier's ORDINAL, which is what a numeric ConditionTerm can actually compare against.
        // The milestone that declares the enum is not yet assigned; when it does, this entry fails
        // and whoever added the type has to decide whether IRunStateView.Tier should become it.
        new("Tier", SubjectKind.CoreType, "unassigned — difficulty tiers",
            "SlayIdleRepeat.Core.Rules.Effects.IRunStateView.Tier, which ships as an int ordinal " +
            "because 18 §4's 'enum' has no declared type to name"),

        new(Domain.CommandsNamespace, SubjectKind.CoreNamespace, "M1-06",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
        new(Domain.EventsNamespace, SubjectKind.CoreNamespace, "M1-03",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
        new(Domain.HandlersNamespace, SubjectKind.CoreNamespace, "M1-09",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal, DomainPurityTests.Every_command_type_is_handled_by_Apply"),
        new(Domain.TestingNamespace, SubjectKind.CoreNamespace, "M1-11",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
    };

    /// <summary>
    /// Subjects that MUST exist right now, because a rule that keys on them is live and its
    /// silence would be indistinguishable from its success.
    /// </summary>
    private static readonly PendingSubject[] Live =
    {
        // Arrived in M1-07, which is why it is no longer in Pending. It has to be tracked HERE, not
        // nowhere: the rule keyed on it stays vacuous until M1-10 lands Core/Rules/, so a rename in
        // the meantime would empty it permanently with the whole suite green — the exact silence
        // Every_rule_subject_is_present_or_declared_pending exists to break.
        new(Domain.EntitlementsType, SubjectKind.CoreType, "M1-07",
            "IsolationTests.Entitlements_are_unreachable_from_the_rules_and_the_power_computation"),

        // Moved out of Pending by M1-01 rather than deleted: Every_rule_subject_is_present_or_
        // declared_pending requires every namespace 30 §11.4 enumerates to appear in one of these
        // two lists, so a namespace that has arrived is TRACKED here, not dropped. Primitives is
        // the bottom layer of Core_internal_layering_holds' five-row table — the row that forbids
        // it from naming Content, Rng, Model, Rules or Handlers was quantifying over nothing until
        // this commit.
        new(Domain.PrimitivesNamespace, SubjectKind.CoreNamespace, "M1-01",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),

        // Moved out of Pending by M2-05, which landed the first types under Core/Rules/ — the 18 §4
        // condition evaluator and the 18 §5 target resolver. Two rules were quantifying over nothing
        // until this commit: Handlers_and_Rules_are_internal (which is what keeps the DSL
        // interpreter internal, since neither of 30 §11.2's two public exceptions is one of these)
        // and the Rules row of Core_internal_layering_holds.
        new(Domain.RulesNamespace, SubjectKind.CoreNamespace, "M2-05",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal, AccessibilityBoundaryTests." +
            "Core_internal_layering_holds, IsolationTests.Entitlements_are_unreachable_from_the_rules_" +
            "and_the_power_computation"),

        // 🔒 The two evaluators, tracked by NAME as well as by namespace. The count floors below are
        // not enough on their own: ConditionEvaluationTypeFloor is 1, and moving ConditionEvaluator
        // one directory up would leave ConditionArguments — a three-field record struct — satisfying
        // it while both purity rules quantified over nothing but that. These entries are what turn
        // the move into a build failure instead of two permanently green rules.
        new("ConditionEvaluator", SubjectKind.CoreType, "M2-05",
            "ConditionPurityRuleTests.A_condition_never_draws_and_never_reads_a_clock, " +
            "ConditionPurityRuleTests.A_condition_never_mutates_anything"),
        new("TargetResolver", SubjectKind.CoreType, "M2-05",
            "ConditionPurityRuleTests.The_18_5_target_resolver_holds_no_writable_static_state"),

        // Governed by ConditionPurityRuleTests.ReachedByAConditionByName, which is a hard-coded full
        // name: rename this type and the condition rules stop covering the shared roster predicate
        // that ENEMY_COUNT and every 18 §5 enemy token read through, with nothing going red.
        new("BattleRoster", SubjectKind.CoreType, "M2-05",
            "ConditionPurityRuleTests.A_condition_never_draws_and_never_reads_a_clock, " +
            "ConditionPurityRuleTests.A_condition_never_mutates_anything"),

        new(Domain.ContentNamespace, SubjectKind.CoreNamespace, "M0-09",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
        new(Domain.RngNamespace, SubjectKind.CoreNamespace, "M0-06",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
        new(Domain.SnapshotsNamespace, SubjectKind.CoreNamespace, "M0-07",
            "AccessibilityBoundaryTests.Apply_is_the_only_public_mutation (the Snapshots exemption of 30 §11.3)"),

        // Live only because Model.Snapshots sits beneath it — Domain.CoreTypesUnder matches
        // by namespace PREFIX. The aggregates themselves are M1-04's, so the aggregate half
        // of Apply_is_the_only_public_mutation is still quantifying over nothing; what is
        // asserted here is that the prefix reaches something, not that M1 has arrived.
        new(Domain.ModelNamespace, SubjectKind.CoreNamespace, "M0-07",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
        new("CanonicalStateWriter", SubjectKind.CoreType, "M0-07",
            "the 14 §16.6 field-order pin in SlayIdleRepeat.Core.Tests"),

        // Moved out of Pending by M1-01 rather than deleted, for the reason the type list exists:
        // DomainPurityTests.CurrencyFields() recognises a currency field by the hard-coded simple
        // name Domain.CurrencyIdType, and nothing else in this suite would notice that constant
        // going stale. The rule itself is still VACUOUS today — it needs a non-static instance
        // field typed CurrencyId, and M1-04 brings the first — and that vacuity is tracked by the
        // CurrencyChanged (M1-03) entry in Pending. This entry tracks the other half: the name.
        new("CurrencyId", SubjectKind.CoreType, "M1-01",
            "DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged (vacuous until M1-04 " +
            "declares the first currency field; this pins the name it will be recognised by)"),
    };

    // ---------------------------------------------------------------- floors
    //
    // Derived from the tree as it stands on this commit, and stated as FLOORS rather than
    // equalities so that adding an adapter or a Core type is not a test edit. Lowering one
    // is a deliberate decision that belongs in the same commit as the deletion that forces
    // it, with the reason in the message.

    private const int ProductionProjectFloor = 29;   // 26 under src/ + 3 under tools/
    private const int AdapterFloor = 21;
    private const int CoreTypeFloor = 26;            // Il.AllTypes over SlayIdleRepeat.Core
    private const int PortFloor = 1;                 // IContentSourcePort (M0-09)

    // StringOrderingRuleTests (M2-01) is stated over the ordering call sites in Core and
    // Application — LINQ OrderBy/ThenBy/Max/Min, List.Sort, Array.Sort, and the sorted-collection
    // constructors. Move them all out of those two assemblies and the rule reports success over
    // nothing, with 18 §8's device-independent effect-id order unguarded. There were 33 on the
    // commit the rule landed; the floor is set well below that so ordinary refactoring is not a
    // test edit.
    private const int OrderingCallSiteFloor = 10;

    // ConditionPurityRuleTests (M2-05) is stated over the types under
    // SlayIdleRepeat.Core.Rules.Effects.Conditions — a namespace FILTER, which is the shape this
    // whole file exists to watch. Rename the folder, move the evaluator one directory up, or let
    // M2-06 fold it into a neighbouring namespace, and both of that file's rules report success over
    // nothing while 18 §4's "pure functions of current state" goes unguarded. There were 2 types on
    // the commit the rules landed (ConditionEvaluator, ConditionArguments); the floor is 1, because
    // the claim being made is that the namespace still REACHES the evaluator, not that it holds a
    // particular number of helpers.
    private const int ConditionEvaluationTypeFloor = 1;

    /// <summary>
    /// `23` §6 — the subject sets these rules quantify over are the ones they were written
    /// against. Pins a floor under every set whose emptiness would be reported as success:
    /// the production projects, the adapters, the assemblies named by constant, `Core`'s
    /// types, and `Application`'s ports.
    /// </summary>
    /// <remarks>
    /// The numbers are floors, not equalities. A set that has SHRUNK past its floor is the
    /// signal: it means a rename, a move or a deletion has taken a rule's subjects away, and
    /// every rule quantifying over that set has gone quiet rather than gone green.
    /// </remarks>
    [Fact]
    public void The_rules_subject_set_is_the_one_they_were_written_against()
    {
        var offenders = new List<string>();

        Floor(offenders, "production projects (src/ + tools/)", RepoLayout.ProductionProjectFiles.Count, ProductionProjectFloor,
            "ProjectFileTests' six rules are stated over this list; an empty or shrunken one is six green ticks over nothing.");

        Floor(offenders, "adapter assemblies", ProductionAssemblies.AdapterNames.Count, AdapterFloor,
            "AdapterNames is a StartsWith(\"" + ProductionAssemblies.AdapterPrefix + "\") filter. Renaming the projects " +
            "empties it and takes DependencyRuleTests.Adapters_never_reference_each_other plus three ProjectFileTests rules " +
            "permanently green with it.");

        Floor(offenders, "types in SlayIdleRepeat.Core", Domain.CoreTypes.Count, CoreTypeFloor,
            "Every rule in DomainPurityTests and AccessibilityBoundaryTests quantifies over Core's types.");

        Floor(offenders, "ordering call sites in Core and Application", StringOrderingRuleTests.OrderingCallSites, OrderingCallSiteFloor,
            "StringOrderingRuleTests.No_production_code_orders_strings_with_the_default_comparer is stated over them. " +
            "An empty set means nothing is stopping a bare OrderBy(x => x.Id) from putting the ambient collation back " +
            "into 18 §8's effect-id order.");

        Floor(offenders, "types under " + ConditionPurityRuleTests.ConditionsNamespace,
            ConditionPurityRuleTests.SubjectCount, ConditionEvaluationTypeFloor,
            "ConditionPurityRuleTests' two rules — A_condition_never_draws_and_never_reads_a_clock and " +
            "A_condition_never_mutates_anything — are stated over them. An empty set means the 18 §4 " +
            "evaluator has moved out of that namespace and nothing is stopping the next edit from " +
            "memoising a reading or drawing inside a condition.");

        Floor(offenders, "types under " + ConditionPurityRuleTests.TargetingNamespace,
            ConditionPurityRuleTests.TargetSubjectCount, ConditionEvaluationTypeFloor,
            "ConditionPurityRuleTests.The_18_5_target_resolver_holds_no_writable_static_state is stated " +
            "over them. An empty set means the 18 §5 resolver has moved and nothing is stopping the " +
            "next edit from caching a candidate list that is wrong on the next death.");

        Floor(offenders, "ports under " + Domain.PortsNamespace, Domain.Ports.Count, PortFloor,
            "DependencyRuleTests.Every_port_has_at_least_two_implementations and No_port_signature_exposes_a_vendor_type " +
            "are both stated over this set. It has been non-empty since M0-09 landed IContentSourcePort; if it is empty " +
            "again, the ports have moved out of Application/Ports/ and both rules are asserting nothing.");

        // The assemblies the suite names by constant. A rule that loads one of these by name
        // would throw rather than pass if it vanished — but only if some rule actually loads
        // it, which is not true of all of them, so the presence is asserted directly.
        var named = new[]
        {
            ProductionAssemblies.CoreName,
            ProductionAssemblies.ApplicationName,
            ProductionAssemblies.ContractsName,
            ProductionAssemblies.ServerName,
            ProductionAssemblies.ClientName,
        };

        offenders.AddRange(
            named.Where(n => !ProductionAssemblies.AllNames.Contains(n, StringComparer.Ordinal))
                 .Select(n => $"assembly constant '{n}' names no project under src/ or tools/ — the rules keyed on it are silent"));

        offenders.AddRange(
            ProductionAssemblies.CompositionRootNames
                .Where(n => !ProductionAssemblies.AllNames.Contains(n, StringComparer.Ordinal))
                .Select(n => $"composition root '{n}' names no project — Only_composition_roots_reference_adapter_projects " +
                             "would exempt nothing and govern nothing"));

        offenders.AddRange(
            ProductionAssemblies.CoreOnlyToolNames.Concat(ProductionAssemblies.ToolCompositionRootNames)
                .Where(n => !ProductionAssemblies.AllNames.Contains(n, StringComparer.Ordinal))
                .Select(n => $"tool '{n}' names no project under tools/ — the rule pinning its references governs nothing"));

        ArchRule.Empty(
            offenders,
            "Every architecture rule's subject set is the one it was written against — no rule has gone " +
            "vacuous through a rename, a move or a deletion (23 §6).");
    }

    /// <summary>
    /// `23` §6 / `30` §11.4 — every name the rules look up is either present, or declared
    /// pending with the milestone that creates it. Fails both ways: on a subject that is
    /// absent and undeclared, and on a declared-pending subject that has arrived.
    /// </summary>
    /// <remarks>
    /// <c>Infrastructure/Domain.cs</c> looks its subjects up by hard-coded simple NAME, and
    /// an absent name means the rule's subject set is empty and the rule holds. That is good
    /// design — it is what lets the suite bite the day M1 lands without a single
    /// <c>Skip</c> — but it hangs entirely on M1 choosing exactly those identifiers. This
    /// rule is what turns a different choice into a build failure instead of eight rules
    /// quietly reporting success for the rest of the project.
    /// </remarks>
    [Fact]
    public void Every_rule_subject_is_present_or_declared_pending()
    {
        var offenders = new List<string>();

        foreach (var subject in Pending)
        {
            if (!subject.Exists())
            {
                continue;
            }

            offenders.Add(
                $"'{subject.Name}' is declared PENDING ({subject.Milestone}) but now exists. The rule(s) keyed on it " +
                $"are live from this commit: {subject.UsedBy}. Delete its entry from SubjectSetFloorTests.Pending — " +
                "leaving it is what would let the subject disappear again later without anything going red.");
        }

        foreach (var subject in Live)
        {
            if (subject.Exists())
            {
                continue;
            }

            offenders.Add(
                $"'{subject.Name}' is declared LIVE (arrived in {subject.Milestone}) but is gone. Every rule keyed on it " +
                $"is now passing over an empty set: {subject.UsedBy}. Either restore it, or move it to Pending with the " +
                "milestone that brings it back and the reason it left.");
        }

        // The suite must not be able to key on a name that appears in neither list — that is
        // how a subject becomes untracked. Domain.cs's type-name constants are the canonical
        // inventory, so every one of them has to be accounted for here.
        var declared = Pending.Concat(Live).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        offenders.AddRange(
            Domain.PermittedCoreNamespaces
                  .Where(ns => !ns.Equals(Domain.CoreNamespace, StringComparison.Ordinal))
                  .Where(ns => !declared.Contains(ns))
                  .Select(ns => $"Core namespace '{ns}' is enumerated by 30 §11.4 but appears in neither Pending nor Live. " +
                                "Every namespace a layering row names must be tracked, or its row governs nothing."));

        ArchRule.Empty(
            offenders,
            "Every rule subject is present, or declared pending with the milestone that creates it (23 §6, 30 §11.4).");
    }

    private static void Floor(List<string> offenders, string what, int actual, int floor, string consequence)
    {
        if (actual < floor)
        {
            offenders.Add(
                $"{what}: found {actual}, floor is {floor}. {consequence} " +
                "If this shrank on purpose, lower the floor in the same commit and say why in the message.");
        }
    }

    private enum SubjectKind
    {
        CoreType,
        CoreNamespace,
    }

    private sealed record PendingSubject(string Name, SubjectKind Kind, string Milestone, string UsedBy)
    {
        internal bool Exists() => Kind switch
        {
            SubjectKind.CoreType => Domain.FindInCore(Name) is not null,
            SubjectKind.CoreNamespace => Domain.CoreTypesUnder(Name).Any(),
            _ => throw new InvalidOperationException($"Unhandled subject kind {Kind}."),
        };
    }
}
