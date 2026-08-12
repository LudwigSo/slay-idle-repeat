using System.Reflection;
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
        new("GuildView", SubjectKind.CoreType, "M14",
            "IsolationTests.GuildView_is_a_read_only_projection"),
        new("InMemoryGame", SubjectKind.CoreType, "M1-11",
            "DomainPurityTests.The_whole_game_is_playable_from_Core_alone"),
        new("GhostSnapshot", SubjectKind.CoreType, "M12",
            "IsolationTests.Guild_state_is_unreachable_from_the_ghost_snapshot"),

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
        // nowhere: a rename would empty the rule keyed on it permanently with the whole suite green
        // — the exact silence Every_rule_subject_is_present_or_declared_pending exists to break.
        // That rule was vacuous while Core/Rules/ was empty; M1-10 landed Core/Rules/Economy/, so it
        // now quantifies over six real types (see the Rules namespace entry below).
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

        // Moved out of Pending by M1-03, when the rule keyed on this name was still VACUOUS.
        //
        // 🔒 THE RULE IS AWAKE. M1-04 declared Player._wallet — an
        // IReadOnlyDictionary<CurrencyId, long> instance field on the Player aggregate — and
        // CurrencyFields() recognises it by BOTH halves of its predicate (CurrencyId-typed, and
        // named for a wallet). Every_currency_mutation_emits_CurrencyChanged now quantifies over
        // real production fields: PlayerSnapshot's Wallet component and the aggregate's own, and
        // the only method outside a constructor that writes the aggregate's is Player.MoveBalance,
        // which constructs a CurrencyChanged. Removing that construction turns the build red naming
        // Player.MoveBalance and _wallet — demonstrated on this branch, reverted, and quoted in the
        // task report (S1). The `count == 0` sentinel in the rule is now dead code on this
        // repository and stays only as the guard for a future assembly with no wallet at all.
        //
        // ⚠️ Waking it up cost one narrow, principled clause. A positional record compiles each
        // component to a compiler-generated `init` setter, so PlayerSnapshot.set_Wallet writes a
        // currency-carrying field, is not a constructor, and is not named Rehydrate — the rule
        // fired on it immediately, exactly as this comment predicted for CurrencyChanged.set_Id.
        // IsRehydrationOrConstruction now also exempts a method that is BOTH [CompilerGenerated]
        // AND an init-only setter, which is construction by the language's own definition. An
        // author-written method that writes a currency field is still caught; see
        // DomainPurityTests.The_construction_exemption_covers_a_records_init_accessor_and_nothing_else.
        //
        // The name is still tracked here, and that has not stopped mattering: the IL scan looks for
        // the literal simple name CurrencyChanged, so renaming the event would make
        // EmitsCurrencyChanged answer false for every emission and turn the now-live rule into a
        // wall of false failures — or, if the field predicate were renamed in the same commit,
        // permanently green with no other test noticing.
        new("CurrencyChanged", SubjectKind.CoreType, "M1-03",
            "DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged (LIVE since M1-04 declared " +
            "Player._wallet; this pins the event name the IL scan looks for when deciding whether a " +
            "currency write emitted anything)"),

        // Tracked because two rules key on this exact simple name: Domain.IsDomainEvent (the
        // CurrencyFields() exclusion) and Contracts_never_redeclares_a_domain_type's derivation
        // check. Rename the base and both stop matching silently — CurrencyChanged's CurrencyId-
        // typed backing field re-enters the subject set and takes the vacuity sentinel above with
        // it, and Contracts could redeclare the event hierarchy with that rule still green.
        new(Domain.DomainEventType, SubjectKind.CoreType, "M1-03",
            "DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged (the event exclusion that " +
            "keeps its subject set genuinely empty until M1-04), " +
            "AccessibilityBoundaryTests.Contracts_never_redeclares_a_domain_type"),

        // Moved out of Pending by M1-03 rather than deleted: Every_rule_subject_is_present_or_
        // declared_pending requires every namespace 30 §11.4 enumerates to appear in one of these two
        // lists, so dropping the row goes red.
        //
        // ⚠️ DOC CONTRADICTION, CARRIED FORWARD (steering S16). Events appears in no row of
        // Core_internal_layering_holds' FORBIDDEN-PAIR table, and it must not be given one on a
        // guess. 30 §11.4's chain is "Handlers -> Rules -> Model -> Content -> Primitives" and
        // omits Commands and Events entirely, while 30 §7 writes
        // GearGranted(int, GearInstance, SourceClass, bool) — and GearInstance is a Model
        // aggregate. A row forbidding Events -> Model would therefore contradict 30 §7 and block
        // M4-03 outright.
        //   OWNER: M1-06's task brief takes the first cut, because it lands Commands/ and Handlers/
        //   and turns one ungoverned region into two. The binding ruling is due at the M4 KICKOFF,
        //   before M4-03 authors GearGranted — that is the commit where Events -> Model stops being
        //   hypothetical. Whoever rules amends 30 §11.4 rather than only the table.
        // What IS settled and enforced meanwhile: Events is in that rule's mustNotReachTheRoot
        // list (an event naming GameRules or GameContext is a cycle under every reading), and
        // DomainEventTests.Core_Events_holds_the_event_hierarchy_and_nothing_else governs what the
        // namespace DECLARES. Neither says anything about Events -> Model, which is the open half.
        new(Domain.EventsNamespace, SubjectKind.CoreNamespace, "M1-03",
            "AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace, " +
            "AccessibilityBoundaryTests.Core_internal_layering_holds (the mustNotReachTheRoot half only — " +
            "Events has no row in the forbidden-pair table; see the note above)"),

        new(Domain.ContentNamespace, SubjectKind.CoreNamespace, "M0-09",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
        new(Domain.RngNamespace, SubjectKind.CoreNamespace, "M0-06",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
        new(Domain.SnapshotsNamespace, SubjectKind.CoreNamespace, "M0-07",
            "AccessibilityBoundaryTests.Apply_is_the_only_public_mutation (the Snapshots exemption of 30 §11.3)"),

        // Was live only because Model.Snapshots sits beneath it — Domain.CoreTypesUnder matches by
        // namespace PREFIX — which meant the aggregate half of Apply_is_the_only_public_mutation
        // was quantifying over nothing while this row looked satisfied. M1-04 landed the Player
        // aggregate directly in this namespace, so the prefix and the aggregate half now reach the
        // same thing; the Player row in this array is what tracks the aggregate half specifically.
        new(Domain.ModelNamespace, SubjectKind.CoreNamespace, "M0-07",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
        new("CanonicalStateWriter", SubjectKind.CoreType, "M0-07",
            "the 14 §16.6 field-order pin in SlayIdleRepeat.Core.Tests"),

        // Moved out of Pending by M1-10, not deleted — same reason as Primitives above: every
        // namespace 30 §11.4 enumerates has to appear in one of these two arrays.
        //
        // 🔒 The two rules keyed on Core/Rules/ were VACUOUS until this commit. The directory held
        // nothing but .gitkeep, so Handlers_and_Rules_are_internal quantified over an empty set and
        // Entitlements_are_unreachable_from_the_rules_and_the_power_computation had no rule to look
        // inside. Core/Rules/Economy/ now holds the 10 §3 / 28 C energy math and both rules bite:
        // every energy type is internal, and none of them names Entitlements. Proved by mutation in
        // both directions before this landed.
        //
        // Placed HERE, directly after CanonicalStateWriter, and not at the end of the array:
        // IMPLEMENTATION_TRACKER.md's carried-forward item 5 sends M1-03's CurrencyChanged entry
        // "beside the CurrencyId row already there", and CurrencyId is the array's LAST entry. Two
        // agents appending to the same tail is the conflict, not the fix.
        new(Domain.RulesNamespace, SubjectKind.CoreNamespace, "M1-10",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal, IsolationTests.Entitlements_are_unreachable_from_the_rules_and_the_power_computation"),

        // Moved out of Pending by M1-01 rather than deleted, for the reason the type list exists:
        // DomainPurityTests.CurrencyFields() recognises a currency field by the hard-coded simple
        // name Domain.CurrencyIdType, and nothing else in this suite would notice that constant
        // going stale. M1-04 declared the first CurrencyId-typed instance field (Player._wallet),
        // so the rule is LIVE — see the CurrencyChanged entry above for what that cost and what it
        // now catches. This entry tracks the other half: the name the field is recognised BY.
        // Renaming CurrencyId without updating Domain.CurrencyIdType would empty the subject set of
        // a rule that is now doing real work, and the by-name half (*wallet*/*currenc*) would keep
        // Player._wallet in it while silently dropping every future field that is only recognised
        // by its type.
        new("CurrencyId", SubjectKind.CoreType, "M1-01",
            "DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged (LIVE since M1-04; this " +
            "pins the type name a currency-carrying field is recognised by)"),

        // 🔒 M1-04. Two names the newly-live halves of two rules key on, tracked so a rename cannot
        // quietly empty either subject set.
        //
        // Apply_is_the_only_public_mutation's AGGREGATE half — public types under Core/Model/ that
        // are NOT under Model/Snapshots/ — quantified over nothing until this commit; the Model
        // entry above only ever proved the PREFIX reached something, and it reached Snapshots,
        // which that rule excludes. Player is the first real subject.
        new("Player", SubjectKind.CoreType, "M1-04",
            "AccessibilityBoundaryTests.Apply_is_the_only_public_mutation (the aggregate half, live " +
            "from this commit — before it, the rule's subject set was empty while the Model namespace " +
            "was not)"),

        // And the 14 §16.6 field-order pin in SlayIdleRepeat.Core.Tests, whose subject set was empty
        // until PlayerSnapshot. It is tracked HERE as well as by its own floor because the pin lives
        // in a different suite: making PlayerSnapshot internal, nesting it, or moving it out of
        // Core/Model/Snapshots/ would empty the pin's selector, and the architecture suite is where
        // "a rule went quiet" is supposed to be noticed.
        new("PlayerSnapshot", SubjectKind.CoreType, "M1-04",
            "the 14 §16.6 field-order pin in SlayIdleRepeat.Core.Tests (SnapshotFieldOrderPinTests — " +
            "five rules that held vacuously until this record existed)"),

        // 🔒 M1-05. Two more names two live rules key on. Appended after PlayerSnapshot rather than
        // inserted, because M1-05 is alone in its wave and nothing else is in flight against this
        // array — the tail-conflict reasoning on the Rules row above is about concurrent agents, not
        // about ordering as such.
        //
        // Run is the SECOND subject of Apply_is_the_only_public_mutation's aggregate half: Player was
        // its only one from M1-04 until this commit, so a rename of Player would have emptied that
        // half completely. It is now floored by two names rather than one.
        new("Run", SubjectKind.CoreType, "M1-05",
            "AccessibilityBoundaryTests.Apply_is_the_only_public_mutation (the aggregate half — Player " +
            "was its only subject until this commit), DomainPurityTests." +
            "Every_currency_mutation_emits_CurrencyChanged (Run::_wallet is the second currency field " +
            "in the repository and has its own floor row in that rule)"),

        // And the field-order pin's second record. Tracked HERE as well as by its own floor because
        // the pin lives in a different suite: making RunSnapshot internal, nesting it, or moving it
        // out of Core/Model/Snapshots/ would drop it from the pin's selector silently, and the
        // architecture suite is where "a rule went quiet" is supposed to be noticed.
        new("RunSnapshot", SubjectKind.CoreType, "M1-05",
            "the 14 §16.6 field-order pin in SlayIdleRepeat.Core.Tests"),

        // ---------------------------------------------------------------- M1-06, 30 §2
        //
        // 🔒 MOVED out of Pending, not deleted. Both directions of
        // Every_rule_subject_is_present_or_declared_pending need them here: a Pending entry whose
        // subject now exists fails, and a type-name constant appearing in NEITHER array fails too.
        //
        // 🔒 WHAT WOKE UP WITH THEM, measured on this branch rather than assumed:
        //
        //  · Apply_is_the_only_public_mutation had a THIRD arm that had never run — the one that
        //    fails when GameRules exists but declares no method named Apply, and the one that fails
        //    when an Apply overload is not public static. Both were behind `if (gameRules is not
        //    null)`. Renaming Apply to Handle now goes red naming GameRules; before this commit it
        //    was silent.
        //  · Every_command_type_is_handled_by_Apply stops short-circuiting on a missing
        //    GameCommand. It still quantifies over ZERO concrete subtypes — M1-02 authors the 49 —
        //    but the dispatch surface is now real, so the day M1-02 lands a command without a
        //    dispatch row the rule fires instead of returning early. That ordering is why M1-06
        //    lands first.
        //  · Contracts_never_redeclares_a_domain_type gains its derivation half for commands:
        //    DerivesFrom(t, "GameCommand") could not match anything while no such base existed.
        //
        // 🔒 M1-02 — THE SENTENCE ABOVE ABOUT "ZERO CONCRETE SUBTYPES" IS NO LONGER TRUE, and it is
        // corrected here rather than left to go stale (steering S4's known limit, re-read at this
        // task's start as that limit asks). `14` §2.3's 49 commands landed with 49 dispatch rows, so
        // Every_command_type_is_handled_by_Apply is FULLY LOADED for the first time: it quantifies
        // over 49 concrete subtypes against a dispatch surface of one type. Measured on this branch
        // — a fiftieth command declared without a row turns the build red naming it, and the literal
        // output is in the task report (S1). Contracts_never_redeclares_a_domain_type's derivation
        // half also stops being hypothetical: there are now 49 names it can collide with, and
        // Contracts declares exactly one type (WireProtocol), checked before the vocabulary landed.
        new("GameRules", SubjectKind.CoreType, "M1-06",
            "AccessibilityBoundaryTests.Apply_is_the_only_public_mutation (the Apply-exists and " +
            "public-static arms, live from M1-06), DomainPurityTests." +
            "Every_command_type_is_handled_by_Apply (the dispatch surface half — GameRules is the " +
            "ONLY type on that surface until M1-09 puts the first handler under Core/Handlers/, so " +
            "renaming it would drop all 49 commands out of the 'dispatched' set at once)"),

        new("GameCommand", SubjectKind.CoreType, "M1-06",
            "DomainPurityTests.Every_command_type_is_handled_by_Apply (LIVE over 49 concrete " +
            "subtypes since M1-02; the rule's subject set is DerivesFrom(t, \"GameCommand\"), so " +
            "renaming the base empties it and every unregistered command becomes invisible), " +
            "AccessibilityBoundaryTests.Contracts_never_redeclares_a_domain_type (the " +
            "DerivesFrom(GameCommand) half)"),

        // 🔒 The two names M1 kickoff decision 5's rule keys on, and they fail in opposite
        // directions. DeterministicRng is what the IL scan looks for a `newobj` on: rename it and
        // the rule matches nothing while every handler is free to open its own stream. RunRngScope
        // is the rule's IDENTITY FLOOR: it is the one sanctioned construction site, and a count-only
        // floor would be satisfied by whatever construction replaced it.
        new(Domain.DeterministicRngType, SubjectKind.CoreType, "M0-06",
            "DomainPurityTests.DeterministicRng_is_constructed_only_inside_Core_Rng (the name the " +
            "newobj scan matches)"),
        new(Domain.RunRngScopeType, SubjectKind.CoreType, "M1-06",
            "DomainPurityTests.DeterministicRng_is_constructed_only_inside_Core_Rng (the identity " +
            "floor — the one construction site the rule proves it can see)"),

        // The namespace, moved for the reason Primitives and Rules were moved: every namespace
        // 30 §11.4 enumerates has to appear in one of these two arrays or its layering row governs
        // nothing. 🔒 Core_internal_layering_holds gained a Commands ROW on this commit — see the
        // note there for what it forbids and why the Events half is still open.
        // 🔒 M1-02 filled it: 49 commands plus one internal payload helper. The two layering rows
        // M1-06 added were LIVE-BUT-THIN over a single abstract base with no members; they now
        // govern 50 types, and the Commands row's "may name Primitives, Content and Rng, may not
        // name Model, Rules or Handlers" was tested for real by the payload decision — every field
        // in the vocabulary is an int, a string, a bool or Primitives.DifficultyTier, and no command
        // names a Model aggregate. M1-06's brief asked to be told if one had to; none does.
        new(Domain.CommandsNamespace, SubjectKind.CoreNamespace, "M1-06",
            "AccessibilityBoundaryTests.Core_internal_layering_holds (the Commands row and the " +
            "mustNotReachTheRoot row, both added in M1-06 and both governing 50 types since M1-02), " +
            "AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace"),
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
    private const int TypeConstantFloor = 10;        // Domain's *Type / *Event const fields

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

        // 🔒 And the same for the TYPE names, which the comment above has claimed since M0-08
        // while only the namespaces were actually checked. Measured on the M1-03 branch: deleting
        // the CurrencyChanged row from Live outright — rather than moving it — passed. That is the
        // shape steering S1 is about, a comment promising more than the assertion delivers, sitting
        // inside the very mechanism whose job is to stop a subject going untracked.
        //
        // The inventory is read off Domain's own const fields rather than transcribed, so a new
        // constant is covered the moment it is written. IClockPort is the one exclusion and it is
        // inline rather than in a list, so a second one cannot be added quietly: it is the name
        // that must NEVER appear in Core (30 §3), so "pending until some milestone creates it" is
        // the wrong frame for it — AmbientApiTests is what watches that name.
        var typeConstants = TypeNameConstants();

        Floor(offenders, "Domain type-name constants", typeConstants.Length, TypeConstantFloor,
            "The untracked-subject check below is stated over this set. Read off Domain's const fields by " +
            "the 'Type'/'Event' suffix, so a renamed constant drops out of the inventory silently and its " +
            "subject stops having to be tracked at all.");

        offenders.AddRange(
            typeConstants
                .Where(c => !c.Value.Equals(Domain.ClockPortType, StringComparison.Ordinal))
                .Where(c => !declared.Contains(c.Value))
                .Select(c => $"Domain.{c.Constant} looks up the Core type '{c.Value}', which appears in neither " +
                             "Pending nor Live. Every name a rule keys on must be tracked: absent and undeclared, " +
                             "the rule keyed on it is passing over an empty set and nothing here would say so."));

        ArchRule.Empty(
            offenders,
            "Every rule subject is present, or declared pending with the milestone that creates it (23 §6, 30 §11.4).");
    }

    /// <summary>
    /// Every simple type name <c>Domain</c> looks a subject up by, read off its own <c>const</c>
    /// fields by the <c>Type</c>/<c>Event</c> suffix its authors have used since M0-08.
    /// </summary>
    /// <remarks>
    /// Reflection rather than a transcription, so a constant added in a later milestone is covered
    /// on the commit that adds it rather than on the commit someone remembers to. <c>ApplyMethod</c>
    /// is correctly outside the set — it names a method, not a subject a type lookup can find.
    /// </remarks>
    private static (string Constant, string Value)[] TypeNameConstants() =>
        typeof(Domain)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Where(f => f.Name.EndsWith("Type", StringComparison.Ordinal) ||
                        f.Name.EndsWith("Event", StringComparison.Ordinal))
            .Select(f => (Constant: f.Name, Value: (string)f.GetRawConstantValue()!))
            .OrderBy(c => c.Constant, StringComparer.Ordinal)
            .ToArray();

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
