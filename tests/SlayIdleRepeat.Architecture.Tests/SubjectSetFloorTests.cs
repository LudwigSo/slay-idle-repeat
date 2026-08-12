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
    /// Subjects the rules key on that M1 and later create. Each one is absent today, and each is
    /// either the reason some rule is currently vacuous, or — since M2-15 — a name whose
    /// <b>arrival</b> forces a change here that must not be forgotten.
    /// </summary>
    /// <remarks>
    /// The second kind reads oddly against "the reason some rule is vacuous" and is called out so
    /// nobody prunes it as mis-filed. <c>CombatSimulator</c> and <c>PowerCalculator</c> are the two
    /// names <c>Domain.PublicRuleTypes</c> exempts from
    /// <c>Handlers_and_Rules_are_internal</c>. That rule is <b>not</b> vacuous — it quantifies over
    /// every type under <c>Rules/</c> — but its <i>exemption</i> arm is, and a vacuous exemption
    /// makes a rule stricter rather than silent. What these entries buy is different: the arrival
    /// of either type is the moment a decision has to be made, and this array is the only mechanism
    /// in the repo that fires on an arrival.
    /// </remarks>
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

        // 🔒 An UNRESOLVED DOC CONTRADICTION, parked where it expires by itself. It is not a
        // file-contention deferral, and the difference matters to whoever picks it up.
        //
        // `05` §7 declares CombatEvent, CombatEventType and SimulationResult PUBLIC, because
        // `05` §8 has the client replay the log and `11` §6 has the PvP backend recompute LogHash
        // over it. C# forces the same conclusion: a public CombatSimulator.Simulate returning an
        // internal SimulationResult does not compile.
        //
        // But `30` §11.2 is 🔒 and reads "The only two `Rules` types that are public", naming
        // CombatSimulator and PowerCalculator — and Domain.PublicRuleTypes is a faithful
        // transcription of that closed list. So the two documents disagree, and widening the list
        // is a change to a LOCKED section, not a mechanical edit.
        //
        // ⚠️ Whoever lands CombatSimulator must therefore get a CONDUCTOR RULING on the `05` §7 /
        // `30` §11.2 conflict first, and then do both halves in one commit: make the three result
        // types public, and add them to Domain.PublicRuleTypes. M2-15 left them internal — fully
        // tested through the `30` §11.3 InternalsVisibleTo grant, so nothing is unverified; only
        // the visibility is deferred. (M1-12 was also holding Domain.cs at the time, but that is
        // the lesser reason and it will have passed.)
        //
        // This entry is the expiry: Every_rule_subject_is_present_or_declared_pending fails the
        // moment a type named CombatSimulator exists, which is exactly when the ruling is needed.
        new("CombatSimulator", SubjectKind.CoreType, "M2-08",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal — see the note above this entry: " +
            "landing CombatSimulator needs a ruling on the `05` §7 / `30` §11.2 contradiction, then makes " +
            "CombatEvent, CombatEventType and SimulationResult public and adds them to Domain.PublicRuleTypes " +
            "in the same commit"),

        // The other member of Domain.PublicRuleTypes. Tracked for the same reason its sibling is:
        // the list is a transcription of a 🔒 section, and a rule keyed on it must not be able to
        // go quiet through a rename nobody notices. M2-07 is landing Rules/Stats/ and this is the
        // type that directory exists for (`29` §1, `30` §11.2).
        new("PowerCalculator", SubjectKind.CoreType, "M2-07",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal — the exemption arm of the rule; " +
            "Domain.PublicRuleTypes names it and nothing else pins that name"),

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

        // Moved out of Pending by M2-15, which landed the first types under Core/Rules/ —
        // Core/Rules/Combat/, the `05` §7 combat log format. M2-07 landed Core/Rules/Stats/ in the
        // same wave and independently wrote this same entry; one survives, attributed to whichever
        // commit made the namespace non-empty first. Two rules stated over Domain.RulesNamespace
        // stopped quantifying over nothing on that commit.
        // ⚠️ M2-07's version of this entry carried an M1-10 marker: the energy math under
        // Core/Rules/Economy/ makes the same Pending→Live move on milestone/M1, which is NOT on this
        // branch. Whichever milestone merges first wins; the loser's entry is a duplicate to delete,
        // not a second subject. Recorded so the M1+M2 merge does not read it as a conflict of substance.
        new(Domain.RulesNamespace, SubjectKind.CoreNamespace, "M2-15",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal, IsolationTests.Entitlements_are_" +
            "unreachable_from_the_rules_and_the_power_computation"),

        // 🔒 Tracked SEPARATELY from Domain.RulesNamespace, and it has to be.
        //
        // Guild_state_is_unreachable_from_the_combat_path keys on Domain.CombatRulesNamespace — a
        // different constant, and one the inventory check at the foot of
        // Every_rule_subject_is_present_or_declared_pending cannot reach, because that check walks
        // Domain.PermittedCoreNamespaces and the sub-namespaces are not in it.
        //
        // Without this entry: rename Core/Rules/Combat/ and drop "Combat"/"Battle" from the type
        // names, and the rule's subject set is permanently empty while the RulesNamespace entry
        // above stays satisfied by any other Rules/ subfolder (it is a PREFIX match). Nothing goes
        // red, and the game's only contended write is free to reach its hottest path again.
        // Empty since M0-08 wrote the rule; non-empty since M2-15.
        new(Domain.CombatRulesNamespace, SubjectKind.CoreNamespace, "M2-15",
            "IsolationTests.Guild_state_is_unreachable_from_the_combat_path"),

        // The 05 §1-2 stat block and the 18 §8 aggregation. Tracked separately from RulesNamespace
        // because Domain.cs declares it separately: it is the more specific constant, and a rename of
        // Core/Rules/Stats/ that left Core/Rules/ non-empty would leave the entry above satisfied.
        new(Domain.StatsRulesNamespace, SubjectKind.CoreNamespace, "M2-07",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal (the 05 §1-2 stat block and the 18 §8 aggregation)"),

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
