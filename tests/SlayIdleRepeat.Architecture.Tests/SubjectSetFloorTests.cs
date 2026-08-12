using System.Reflection;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
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
        // 🔴 M1-12 CORRECTED THIS ROW'S CITATION, and the correction is the first thing
        // Every_tracked_subject_name_is_read_by_a_rule_and_every_cited_rule_exists found. The row
        // said IsolationTests.Guild_state_is_unreachable_from_the_ghost_snapshot, and that rule does
        // NOT key on this constant: it selects `t.Name.Contains("Ghost")`, deliberately, so that it
        // catches a GhostLoadout or a GhostBuild as well as the snapshot. Renaming
        // Domain.GhostSnapshotType would therefore have left that rule working exactly as before,
        // while the row promised it was the thing at risk — steering S4's known limit, in the one
        // file whose job is to stop a subject going untracked.
        //
        // What actually reads the name is GapRegister, in two places, and both now read the CONSTANT
        // rather than a hand-typed copy of it: the M12-01 deferral and 30 §4.1's WorldSlice
        // transcription. Those are real mechanisms — No_deferral_outlives_the_type_that_gives_it_
        // meaning and Every_subject_the_design_docs_enumerate_is_authored_or_declared_deferred are
        // both stated over them — so the constant is load-bearing after all, just not where the row
        // said.
        new("GhostSnapshot", SubjectKind.CoreType, "M12",
            "GapRegister.Deferred (the M12-01 entry) and GapRegister.Surfaces (30 §4.1's " +
            "WorldSlice row) — the only two readers of this name, both keyed on the constant. " +
            "⚠️ NOT IsolationTests.Guild_state_is_unreachable_from_the_ghost_snapshot, which this row " +
            "cited until M1-12: that rule selects by name CONTAINING 'Ghost' and is unaffected by a " +
            "rename of the constant"),
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
        // now quantifies over real types (see the Rules namespace entry below).
        //
        // ⚠️ M1-11 corrected "six" to "real": Handlers_and_Rules_are_internal filters
        // `DeclaringType is null && !IsCompilerGenerated`, so its subject set under Core/Rules/ is
        // the THREE author-written types (EnergyAccrual, EnergyMath, EnergySpend). Six is only
        // reachable by counting compiler-generated closures, which the rule excludes. The number is
        // dropped rather than fixed to 3, on the reasoning the Primitives row above records.
        new(Domain.EntitlementsType, SubjectKind.CoreType, "M1-07",
            "IsolationTests.Entitlements_are_unreachable_from_the_rules_and_the_power_computation"),

        // Moved out of Pending by M1-01 rather than deleted: Every_rule_subject_is_present_or_
        // declared_pending requires every namespace 30 §11.4 enumerates to appear in one of these
        // two lists, so a namespace that has arrived is TRACKED here, not dropped. Primitives is
        // the bottom layer of Core_internal_layering_holds' forbidden-pair table — the row that
        // forbids it from naming Content, Rng, Model, Rules or Handlers was quantifying over
        // nothing until this commit.
        //
        // 🔒 M1-08 corrected two facts in the sentence above rather than leaving them to rot (S4's
        // known limit, which this milestone keeps hitting). ⚠️ AND M1-11 CORRECTED M1-08's
        // CORRECTION, three lines from the file that ALSO carried the same stale number: this
        // comment said "the table is SIX rows, not five" and listed what Primitives may not name.
        // Both went stale again the moment M1-11 added the Events and Testing rows. The row count is
        // therefore gone from this comment rather than restated a third time — read
        // Core_internal_layering_holds' table, which is the thing that decides. What is durable is
        // the CLAIM: Primitives is the bottom layer, so it may name nothing above it, and this row's
        // forbidden list is whatever that table says today.
        //
        // 🔒 M1-08 also made this row's subject set load-bearing in a way it had not been. It landed
        // Primitives/GameCalendar — 30 §2.3's 05:00 UTC day and Monday week — precisely BECAUSE
        // Primitives is the one layer both Model and Rules can see, so Player's boundary invariants
        // and GameRules.AdvanceTime's boundary computation read one definition instead of two
        // transcriptions. That placement is only sound while this row is awake: a Primitives type
        // that reached Content for a tuning value, or reached the Core root for GameContext, is
        // exactly what the row forbids, and the calendar's own remarks cite it as the reason it
        // cannot read a tuning document.
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
        //
        // 🔒 M1-08 put a SECOND rule on this same name and it is recorded here rather than left for
        // the next reader to discover: A_currency_event_is_never_discarded_at_its_call_site matches
        // Domain.CurrencyChangedEvent against a CALL'S RETURN TYPE (and the set of Core methods
        // that return one) where the older rule matches it against a newobj. ⚠️ The failure mode is
        // the OPPOSITE of this file's usual one, which is why it is worth writing down: renaming the
        // event without renaming the constant empties that rule's producer set, and it goes RED —
        // Assert.NotEmpty plus an identity floor naming Player::MoveCurrency, Player::SetEnergy,
        // Player::AccrueEnergy and Run::MoveCurrency — rather than going quiet. Tracked all the same,
        // because the row is what tells whoever does the rename which rules they have just moved.
        new("CurrencyChanged", SubjectKind.CoreType, "M1-03",
            "DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged (LIVE since M1-04 declared " +
            "Player._wallet; this pins the event name the IL scan looks for when deciding whether a " +
            "currency write emitted anything), DomainPurityTests." +
            "A_currency_event_is_never_discarded_at_its_call_site (M1-08 — the same name, matched on a " +
            "call's RETURN type rather than on a newobj)"),

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
            "was not), DomainPurityTests.A_currency_event_is_never_discarded_at_its_call_site (M1-08 " +
            "names Player::MoveCurrency, Player::SetEnergy and Player::AccrueEnergy in its identity " +
            "floor, so a rename of the aggregate turns that rule red rather than quiet)"),

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
            "in the repository and has its own floor row in that rule), DomainPurityTests." +
            "A_currency_event_is_never_discarded_at_its_call_site (M1-08 names Run::MoveCurrency in " +
            "its identity floor)"),

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
            "Every_command_type_is_handled_by_Apply (the dispatch surface half — GameRules was the " +
            "ONLY type on that surface until M1-09 put the first handler under Core/Handlers/, and " +
            "it is still the only one that names 48 of the 49, so renaming it would drop them out of " +
            "the 'dispatched' set at once)"),

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
        // ⚠️ "the ONE construction site" was true until M1-09 and is corrected rather than left to
        // rot (S4's known limit). There are TWO sanctioned sites now — 14 §8.1's two regimes — and
        // the rule asserts both by identity, because a floor naming one is satisfied while the other
        // stops constructing anything at all.
        new(Domain.RunRngScopeType, SubjectKind.CoreType, "M1-06",
            "DomainPurityTests.DeterministicRng_is_constructed_only_inside_Core_Rng (an identity " +
            "floor — the RUN regime's construction site, one of the two the rule proves it can see)"),

        // 🔒 M1-09. 14 §8.1's META regime — Hash64(CommandSeed, s, i) from i = 0, no persisted
        // counter — and the ONLY construction site of it. Tracked for the same reason RunRngScope is
        // and for one more: HandlerInput.MetaDraws is the only door to this type, so a rename that
        // missed the Domain constant would empty half the identity floor while every draw in the
        // meta regime carried on working, and nothing else in the suite would say so.
        new(Domain.MetaDrawScopeType, SubjectKind.CoreType, "M1-09",
            "DomainPurityTests.DeterministicRng_is_constructed_only_inside_Core_Rng (an identity " +
            "floor — the META regime's construction site, live from M1-09)"),

        // The namespace, moved for the reason Primitives and Rules were moved: every namespace
        // 30 §11.4 enumerates has to appear in one of these two arrays or its layering row governs
        // nothing. 🔒 Core_internal_layering_holds gained a Commands ROW on this commit — see the
        // note there for what it forbids and why the Events half is still open.
        // 🔒 M1-02 filled it: 49 commands plus one internal payload helper. The two layering rows
        // M1-06 added were LIVE-BUT-THIN over a single abstract base with no members; they now
        // govern 50 types, and the Commands row — read it in the table rather than here, since
        // M1-11 appended Testing to it — was tested for real by the payload decision: every field
        // in the vocabulary is an int, a string, a bool or Primitives.DifficultyTier, and no command
        // names a Model aggregate. M1-06's brief asked to be told if one had to; none does.
        new(Domain.CommandsNamespace, SubjectKind.CoreNamespace, "M1-06",
            "AccessibilityBoundaryTests.Core_internal_layering_holds (the Commands row and the " +
            "mustNotReachTheRoot row, both added in M1-06 and both governing 50 types since M1-02), " +
            "AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace"),

        // ---------------------------------------------------------------- M1-09, 30 §2.3
        //
        // 🔒 MOVED out of Pending, not deleted — the same reason Primitives, Rules and Commands were
        // moved: Every_rule_subject_is_present_or_declared_pending requires every namespace 30 §11.4
        // enumerates to appear in one of these two arrays, and M1-01 proved that deleting a namespace
        // row goes red rather than quiet.
        //
        // 🔒 WHAT WOKE UP WITH IT, measured on this branch rather than assumed:
        //
        //  · Handlers_and_Rules_are_internal was VACUOUS ON ITS Handlers HALF from M0-08 until this
        //    commit — Core/Handlers/ held nothing but .gitkeep, so the whole rule rested on the Rules
        //    half M1-10 woke up. Core/Handlers/BeginSession is its first subject, and making that
        //    type public turns the build red naming it. Proved by mutation, reverted, and the literal
        //    output is in the task report (S1).
        //  · Every_command_type_is_handled_by_Apply's DISPATCH SURFACE is no longer GameRules alone.
        //    ⚠️ That is a widening, and it is worth stating what it costs: the surface is now "types
        //    under Core/Handlers/ plus GameRules", so a command named by a handler and by no dispatch
        //    row would count as dispatched. It cannot be reached that way today — a handler is only
        //    ever named FROM a dispatch row — but the rule's guarantee is now "some type on the
        //    surface names it" rather than "the table names it", and the thing that keeps the two the
        //    same is CommandVocabularyTests pinning the registry's 49 wire names in both directions.
        //
        // ⚠️ WHAT THIS ROW DOES AND DOES NOT CATCH, stated exactly — an earlier draft of this
        // paragraph was wrong in both directions, in the one file whose job is to stop a comment
        // promising more than its assertion delivers (see the untracked-subject note below, which
        // makes the same complaint about M0-08).
        //
        //   · A MOVE or a DELETION of everything under Core/Handlers/ IS caught, and by this row:
        //     PendingSubject.Exists() for a CoreNamespace is Domain.CoreTypesUnder(name).Any(), so
        //     an empty namespace fails here rather than reporting present.
        //   · A RENAME of the handler type is NOT caught by either rule and does not need to be:
        //     both quantify by NAMESPACE, so a renamed type is still a subject. The draft claimed a
        //     rename would empty them; it would not.
        //   · What genuinely has no floor is the handler's IDENTITY, and the BeginSession row below
        //     is it — added because Every_command_type_is_handled_by_Apply's dispatch surface is
        //     "Core/Handlers/ ∪ GameRules", and a Core/Handlers/ that held some OTHER type would
        //     satisfy this namespace row while the handler it was written for had gone.
        new("BeginSession", SubjectKind.CoreType, "M1-09",
            "DomainPurityTests.Every_command_type_is_handled_by_Apply (the dispatch surface's second " +
            "member — the namespace row above cannot tell 'the handler is there' from 'something is " +
            "there'), AccessibilityBoundaryTests.Handlers_and_Rules_are_internal (its only subject " +
            "on the Handlers half)"),

        new(Domain.HandlersNamespace, SubjectKind.CoreNamespace, "M1-09",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal (the Handlers half, LIVE from " +
            "this commit — before it, Core/Handlers/ was empty and that half quantified over nothing), " +
            "DomainPurityTests.Every_command_type_is_handled_by_Apply (the dispatch-surface half — " +
            "GameRules was the only type on that surface until this commit)"),

        // ---------------------------------------------------------------- M1-11, 30 §6
        //
        // 🔒 MOVED out of Pending, not deleted — the reason M1-01 proved by experiment and every
        // milestone since has repeated: Every_rule_subject_is_present_or_declared_pending fails BOTH
        // ways, so a namespace 30 §11.4 enumerates that appears in neither array goes red, and a
        // type-name constant of Domain's that appears in neither goes red with it.
        //
        // 🔒 WHAT WOKE UP WITH THEM, measured on this branch rather than assumed:
        //
        //  · The_whole_game_is_playable_from_Core_alone — `30` §9's own "load-bearing" rule.
        //    ⚠️ AND THE FIRST DRAFT OF THIS NOTE SAID IT HAD BEEN "VACUOUS SINCE M0-08", WHICH IS
        //    FALSE, and getting it wrong here would have been the S1 defect this file exists to
        //    stop, sitting inside the mechanism. The rule has THREE arms and only one was dead:
        //      – the CLOSURE arm walks Core's real AssemblyReferences and has asserted since M0. A
        //        ProjectReference added to SlayIdleRepeat.Core.csproj turns it red whoever names the
        //        reference, so a mutation of that shape demonstrates the arm that was ALREADY live.
        //      – the PUBLIC-HARNESS arm sat behind `harness is not null` and had never run, because
        //        there was no InMemoryGame. That is the one this commit wakes, and the mutation that
        //        distinguishes it is making the harness internal: red with
        //        'SlayIdleRepeat.Core.Testing.InMemoryGame is not public'. Both literal outputs are
        //        in the task report (S1).
        //      – the PRESENCE arm is M1-11's addition, on Apply_is_the_only_public_mutation's
        //        precedent: without it the rule reported success over the ABSENCE of the very type
        //        its name is about, which is the state it was in from M0-08 until this commit.
        //  · Core_internal_layering_holds gained a Testing ROW. ⚠️ It did not have one before, and
        //    that is the pre-existing inaccuracy M1-09 flagged by name and this entry closes: the
        //    Pending row for this namespace cited Core_internal_layering_holds, and that rule did not
        //    key on Testing in EITHER direction — no row of the forbidden-pair table named it as a
        //    layer, and it was not in mustNotReachTheRoot (correctly: the harness is ABOVE the root
        //    and reaches GameRules, WorldSlice, GameContext and CommandResult by design). The rule
        //    that has always keyed on this namespace is
        //    Every_Core_type_lives_under_a_documented_namespace, through
        //    Domain.PermittedCoreNamespaces. Both are now cited, and the citation is true of both.
        //
        // 🔒 The Testing row that was added is the SETTLED direction and no more (the reasoning the
        // Events and Rules rows record for their open halves): Testing may not name Rules or
        // Handlers, because 30 §11.2 makes GameRules.Apply the only public mutation and the harness
        // is the artefact that demonstrates it — a harness calling BeginSession.Handle or
        // EnergyMath.Grant directly would drive the domain behind Apply's back, which is the one
        // thing it exists not to do. Every layer beneath gained Testing in its own forbidden list at
        // the same time — plus an Events row and a root-side loop, because neither of those two had
        // a Layer row to append to — in the direction M1-06's Commands row had to be widened for: a
        // production type naming the test harness is a cycle under every reading.
        //
        // ⚠️ WHAT THAT ROW DOES NOT REACH, so this note does not promise more than the rule: it
        // permits Testing -> Model, and must (30 §11.3's Rehydrate), so a harness calling an
        // aggregate's INTERNAL mutator bypasses Apply exactly as calling a handler would and no
        // namespace rule sees it. The row itself carries that limit in full.
        new(Domain.InMemoryGameType, SubjectKind.CoreType, "M1-11",
            "DomainPurityTests.The_whole_game_is_playable_from_Core_alone (the rule's NAME is stated " +
            "over this type: its public-harness arm had never run before this commit, and M1-11 " +
            "added the presence arm that makes deleting the harness red rather than green)"),

        new(Domain.TestingNamespace, SubjectKind.CoreNamespace, "M1-11",
            "AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace (the rule " +
            "that has named this namespace since M0-08, through Domain.PermittedCoreNamespaces — " +
            "though it governed an EMPTY region until this commit, and the Pending row this replaces " +
            "cited a different rule that did not key on it at all, which M1-09 flagged), " +
            "AccessibilityBoundaryTests.Core_internal_layering_holds (LIVE from this commit, when the " +
            "Testing row was added: the harness may not name Rules or Handlers, and nothing beneath " +
            "it — root included — may name the harness)"),
    };

    // ---------------------------------------------------------------- floors
    //
    // Derived from the tree as it stands on this commit, and stated as FLOORS rather than
    // equalities so that adding an adapter or a Core type is not a test edit. Lowering one
    // is a deliberate decision that belongs in the same commit as the deletion that forces
    // it, with the reason in the message.

    private const int ProductionProjectFloor = 29;   // 26 under src/ + 3 under tools/
    private const int AdapterFloor = 21;
    // 🔒 M1-02 raised this from 26 to 110 (measured: 120 today). It is the one floor in this file
    // that had gone quiet by standing still: the file's own preamble says these numbers are
    // "derived from the tree as it stands on this commit", and 26 was M0-08's tree. At 26 the ENTIRE
    // Core/Commands/ namespace could vanish — 51 types, `14` §2.3's whole wire vocabulary — with
    // every rule in DomainPurityTests and AccessibilityBoundaryTests quantifying over a smaller set
    // and this floor still clearing. Raised rather than left for M1-12 because M1-12's task is to
    // prove the ten rules WOKE UP, and a floor that cannot notice them going back to sleep is the
    // wrong thing to hand that task. 110 rather than 120: a floor, with room for the handful of
    // compiler-generated types a refactor moves either way, and low enough that lowering it is still
    // the deliberate act the comment above describes.
    private const int CoreTypeFloor = 110;           // Il.AllTypes over SlayIdleRepeat.Core
    private const int PortFloor = 1;                 // IContentSourcePort (M0-09)
    private const int TypeConstantFloor = 10;        // Domain's *Type / *Event const fields

    // 🔒 M1-12. The constants whose register row carries a citation THIS assembly can resolve, and
    // therefore the rows whose citation the truth arm actually checks. 12 constants today, one of
    // which (IClockPort) is correctly outside the registers: 11 checked, floored at 9 so a row
    // rewritten into unresolvable prose is a build failure rather than a quiet exemption (S3).
    private const int CitedConstantRowFloor = 9;

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
    /// 🔒 `23` §6 / carried-forward item (b) — every name <c>Domain</c> declares is <b>read</b> by
    /// something, and every rule a register row cites <b>exists</b>. The other half of
    /// <see cref="Every_rule_subject_is_present_or_declared_pending"/>: that one asks whether a
    /// constant is tracked, this one asks whether tracking it buys anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The finding this closes.</b> <c>Domain.GameContextType</c> was declared, referenced by
    /// no rule, and in neither register — and it was caught only because a human read the file. The
    /// register half of that was closed when the untracked-subject check below started reading
    /// <see cref="TypeNameConstants"/>; the <em>referenced</em> half is this rule. A constant nothing
    /// reads is not a subject the suite keys on — it is a name that looks like enforcement, and the
    /// register row beside it is a claim about a rule that is not there.
    /// </para>
    /// <para>
    /// 🔒 <b>Read by <c>ldstr</c>, which is not an implementation detail but the only thing there is
    /// to read.</b> These are <c>const string</c>s, so the compiler inlines each one at its use site
    /// and <em>no field reference to <c>Domain</c> survives into IL</em> — the same inlining that
    /// made <c>Core_internal_layering_holds</c> blind to a cross-layer constant for three
    /// milestones. A rule that looked for a <c>ldsfld</c> on <c>Domain.GhostSnapshotType</c> would
    /// therefore find nothing and report success over every constant in the file.
    /// </para>
    /// <para>
    /// ⚠️ <b><see cref="SubjectSetFloorTests"/> itself is excluded from the reader set, and that is
    /// the whole point.</b> Every constant is named in <see cref="Live"/> or <see cref="Pending"/>
    /// by construction — the check below enforces it — so counting this file as a reader would make
    /// the rule trivially true. What has to exist is a reader somewhere <em>else</em>.
    /// </para>
    /// <para>
    /// 🔒 <b>The second arm is the citation check, and it is the one that found something.</b> A
    /// register row's <c>UsedBy</c> is prose, and prose goes stale silently — steering <b>S4</b>'s
    /// documented known limit, which this milestone hit in M1-08 (three <c>Live</c> rows not
    /// recording a second rule keyed on them), in M1-11 (four more) and here. Any
    /// <c>SomethingTests.Some_rule</c> spelled in a row is now resolved against this assembly's
    /// real <c>[Fact]</c> methods: a rename, a typo or a rule that never existed fails the build.
    /// Cross-suite citations — <c>SnapshotFieldOrderPinTests</c> lives in
    /// <c>SlayIdleRepeat.Core.Tests</c> — are skipped rather than guessed at, because this assembly
    /// cannot see them and inventing a resolution would be worse than the gap.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_tracked_subject_name_is_read_by_a_rule_and_every_cited_rule_exists()
    {
        var offenders = new List<string>();
        var constants = TypeNameConstants();

        Floor(offenders, "Domain type-name constants", constants.Length, TypeConstantFloor,
            "This rule is stated over that set. Read off Domain's const fields by the 'Type'/'Event' " +
            "suffix, so a renamed constant drops out of the inventory and stops having to be read at all.");

        foreach (var (constant, value) in constants)
        {
            var readers = ReadersOf(value);

            if (readers.Length == 0)
            {
                offenders.Add(
                    $"Domain.{constant} = '{value}' is read by nothing outside {nameof(SubjectSetFloorTests)}. " +
                    "It is tracked in a register and keyed on by no rule, which is the shape " +
                    "Domain.GameContextType had when a human — not this suite — found it. Either point a " +
                    "rule at it or delete the constant and its register row together.");
            }
        }

        var facts = SuiteFactNames();

        foreach (var subject in Pending.Concat(Live))
        {
            foreach (var citation in CitedRules(subject.UsedBy).Where(IsRuleCitation))
            {
                if (!facts.Contains(citation, StringComparer.Ordinal))
                {
                    offenders.Add(
                        $"'{subject.Name}' cites '{citation}', which is not a [Fact] in this suite. A register " +
                        "row is the only place that records WHICH rules a subject's absence would silence, " +
                        "and a citation naming a rule that does not exist records nothing (S4).");
                }
            }
        }

        // 🔒 THE ARM THAT DECIDES WHETHER A CITATION IS TRUE, not merely well-spelled. Scoped to the
        // constants, because those are the subjects looked up BY NAME — a row for `Player` is keyed
        // on a namespace selection instead and has no literal for this to find, which is why the
        // rows are not all treated alike.
        var byName = Pending.Concat(Live).ToDictionary(s => s.Name, StringComparer.Ordinal);
        var checkedRows = 0;

        foreach (var (constant, value) in constants)
        {
            if (value.Equals(Domain.ClockPortType, StringComparison.Ordinal) ||
                !byName.TryGetValue(value, out var row))
            {
                continue;
            }

            var citedTypes = CitedRules(row.UsedBy)
                .Select(c => c.Split('.')[0])
                .ToArray();

            if (citedTypes.Length == 0)
            {
                continue;
            }

            checkedRows++;

            var readerTypes = ReaderTypesOf(value);

            if (!citedTypes.Any(t => readerTypes.Contains(t, StringComparer.Ordinal)))
            {
                offenders.Add(
                    $"Domain.{constant} = '{value}' is cited by [{string.Join(", ", citedTypes.Distinct(StringComparer.Ordinal))}] " +
                    $"but is READ by [{string.Join(", ", readerTypes.OrderBy(t => t, StringComparer.Ordinal))}] — no " +
                    "overlap. The row names rules that do not key on this constant, so renaming the constant " +
                    "would leave every rule the row names working exactly as before, and would silence " +
                    "whatever actually reads it without the row saying so. Cite the mechanism that reads it.");
            }
        }

        Floor(offenders, "register rows with an in-suite citation", checkedRows, CitedConstantRowFloor,
            "The citation-truth arm is stated over that set. If it shrinks, rows are being written with " +
            "prose citations this assembly cannot resolve, and the arm is passing over them rather than " +
            "checking them.");

        ArchRule.Empty(
            offenders,
            "Every name Domain declares is read by a rule, and every register row cites a rule that both " +
            "exists and reads it (23 §6).");
    }

    /// <summary>
    /// 🔒 `23` §6 — the teeth of the three arms above (steering <b>S1</b>). Each is a set-membership
    /// check over a set this file builds, and any of them could be built empty — at which point the
    /// rule reports success over every constant and every citation in the file.
    /// </summary>
    [Fact]
    public void The_reader_and_citation_lookups_recognise_a_real_name_and_refuse_an_invented_one()
    {
        Assert.NotEmpty(ReadersOf(Domain.InMemoryGameType));

        Assert.Empty(
            ReadersOf("M1_12_ANameNoRuleCouldPossiblyRead"));

        var facts = SuiteFactNames();

        Assert.Contains(
            nameof(AccessibilityBoundaryTests) + "." + nameof(AccessibilityBoundaryTests.Core_internal_layering_holds),
            facts);

        Assert.DoesNotContain("AccessibilityBoundaryTests.M1_12_No_Such_Rule", facts);

        // The citation PARSER, separately: a row naming no rule must yield nothing, or the arm above
        // quantifies over an empty set on every row and its failure mode is silence.
        Assert.Empty(CitedRules("the 14 §16.6 field-order pin in SlayIdleRepeat.Core.Tests"));

        Assert.Equal(
            new[] { "DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged" },
            CitedRules("DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged (LIVE since M1-04)"));
    }

    /// <summary>
    /// Every method in this suite that loads <paramref name="value"/> as a literal, outside this
    /// file. A <c>const string</c> is inlined at its use site, so the literal is the only trace.
    /// </summary>
    private static string[] ReadersOf(string value) =>
        ReadingMethods(value)
            .Select(m => $"{m.DeclaringType.Name}::{m.Name}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The <b>declaring test classes</b> of everything that reads <paramref name="value"/>, walked
    /// out to the outermost type.
    /// </summary>
    /// <remarks>
    /// 🔒 The walk is what makes this usable. A rule's <c>Where(t =&gt; t.Name.Equals(Domain.X))</c>
    /// lambda compiles into a nested <c>&lt;&gt;c</c> closure class, so the literal is read by
    /// <c>IsolationTests/&lt;&gt;c</c> and not by <c>IsolationTests</c> — and a comparison against
    /// the citation would never match for any rule that reads its constant inside a lambda, which is
    /// most of them. Compared at TYPE level rather than method level for the same kind of reason:
    /// <c>DeterministicRng_is_constructed_only_inside_Core_Rng</c> reads its constant through the
    /// private <c>ConstructsADeterministicRng</c> predicate, which is a true citation of the rule.
    /// </remarks>
    private static string[] ReaderTypesOf(string value) =>
        ReadingMethods(value)
            .Select(m => OutermostName(m.DeclaringType))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<MethodDefinition> ReadingMethods(string value) =>
        Il.AllTypes(SuiteAssembly.Module)
          .Where(t => !OutermostName(t).Equals(nameof(SubjectSetFloorTests), StringComparison.Ordinal))
          .SelectMany(t => t.Methods)
          .Where(m => Il.Instructions(m).Any(i =>
              i.OpCode == OpCodes.Ldstr && (i.Operand as string)?.Equals(value, StringComparison.Ordinal) == true));

    /// <summary>Every <c>Type.Method</c> in this suite carrying <c>[Fact]</c>.</summary>
    private static HashSet<string> SuiteFactNames() =>
        typeof(SubjectSetFloorTests).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The <c>SomeTests.Some_rule</c> citations in a register row's prose, restricted to types this
    /// assembly declares — a citation into <c>SlayIdleRepeat.Core.Tests</c> is not this suite's to
    /// resolve.
    /// </summary>
    private static IEnumerable<string> CitedRules(string usedBy)
    {
        var suiteTypes = typeof(SubjectSetFloorTests).Assembly
            .GetTypes()
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(usedBy, @"\b([A-Z]\w*)\.([A-Za-z_]\w*)\b"))
        {
            if (suiteTypes.Contains(match.Groups[1].Value))
            {
                yield return $"{match.Groups[1].Value}.{match.Groups[2].Value}";
            }
        }
    }

    /// <summary>
    /// True for a citation this assembly can resolve to a <c>[Fact]</c> — a rule class, not a
    /// register. <c>GapRegister</c> is a legitimate thing for a row to cite (it is the mechanism
    /// that carries some names) and it declares no <c>[Fact]</c>, so its members are outside the
    /// existence arm and inside the truth arm. Stated rather than silently skipped, because a
    /// citation nobody checks in either direction is the gap this rule exists to close.
    /// </summary>
    private static bool IsRuleCitation(string citation) =>
        citation.Split('.')[0].EndsWith("Tests", StringComparison.Ordinal);

    /// <summary>The outermost declaring type's name, so a nested fixture is attributed to its host.</summary>
    private static string OutermostName(TypeDefinition type)
    {
        var outer = type;
        while (outer.DeclaringType is not null)
        {
            outer = outer.DeclaringType;
        }

        return outer.Name;
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
