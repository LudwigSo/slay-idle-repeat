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
/// <c>AdapterNames</c> is <c>StartsWith("SlayIdleRepeat.Adapters.")</c> over every adapter
/// project, and renaming them to the singular <c>SlayIdleRepeat.Adapter.*</c> would empty it and
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
    /// Subjects the rules key on that a later milestone creates. Each one is absent today, and each
    /// is either the reason some rule is currently vacuous — <b>by design, and the milestone that
    /// ends it is on the row</b> — or, since M2-15, a name whose <b>arrival</b> forces a change here
    /// that must not be forgotten.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>M1-12 corrected "that M1 and later create".</b> M1 is finished and created none of
    /// these: what is left is <c>GuildView</c> (M14) and <c>GhostSnapshot</c> (M12). The distinction
    /// this array now carries is the one M1-12's brief turns on — a rule whose subject arrives in
    /// M2–M16 is <em>correctly</em> documented as vacuous, and deleting that note would be as wrong
    /// as leaving a false one.
    /// </remarks>
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
        // 🔒 M1 REVIEW. Both Domain.CombatRulesNamespace and Domain.GuildModelNamespace were outside
        // every register until the constants inventory was widened to cover namespace constants —
        // and both are looked up by a live IsolationTests rule that consequently quantified over
        // ZERO types with nothing saying so.
        //
        // 🔒 M2-15 DISCHARGED the CombatRulesNamespace half: it landed Core/Rules/Combat/, so
        // Guild_state_is_unreachable_from_the_combat_path now has real subjects and the row is moved
        // to Live rather than kept here — see Domain.CombatRulesNamespace below. What is left pending
        // is GuildModelNamespace, whose milestone (M14) has not landed either side of this merge.
        new(Domain.GuildModelNamespace, SubjectKind.CoreNamespace, "M14",
            "IsolationTests.Guild_state_is_unreachable_from_the_combat_path, IsolationTests.Guild_state_is_unreachable_from_the_ghost_snapshot"),

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

        // 🔒 BossCatalogue's entry lived here and was DELETED by M2-16a — the second discharge this
        // register has recorded, and the first one M2-13 wrote against ITSELF.
        //
        // The entry said: M2-13 authored content/bosses/bosses.json and, to prove the shipped scripts
        // satisfy BossEncounterBuilder's eight authoring rules, gave SlayIdleRepeat.Core.Tests a
        // System.Text.Json reader (AuthoredBossScripts) plus an EmbeddedResource reaching into
        // game-data/ — which INVERTS the convention EnemyFixtures and StatFixtures both state. The
        // proper home was a Core reader on EnemyCatalogue's precedent, and M2-16a needed it anyway:
        // its balance harness has to build a boss encounter from that file, and tools/BalanceHarness
        // is pinned to Core with no package references, so it could reach neither the test reader nor
        // the Application pipeline.
        //
        // 🔒 All of it is in that commit. SlayIdleRepeat.Core.Rules.Combat.Bosses.BossCatalogue reads
        // the document through ContentSnapshot, and it is INTERNAL on EnemyCatalogue's precedent —
        // Domain.PublicRuleTypes is an enumerated six-name list and a seventh public Rules type would
        // fail Handlers_and_Rules_are_internal. AuthoredBossScripts and its EmbeddedResource were
        // DELETED in the SAME commit, not kept alongside as a second mapping of one file into one set
        // of types, which is exactly what this entry existed to force; the two suites that read them
        // now read BossCatalogue.Read(GameDataLoader.Load()), off disk, with no fixture in between.
        // Leaving the entry would fail Every_rule_subject_is_present_or_declared_pending.
        //
        // The name it left behind is tracked in Live below rather than dropped, on EnemyCatalogue's
        // and StatusCatalogue's precedent: it is the ONE type that reads content/bosses/bosses.json,
        // and the floor BossEngineRuleTests states over the boss namespace would stay satisfied by
        // the phase controller and its neighbours if it went away.

        // 🔒 CombatSimulator's entry lived here and was DELETED by M2-08, which is the mechanism
        // working exactly as this file's remarks describe. The entry recorded an unresolved
        // contradiction — `05` §7 declares CombatEvent, CombatEventType and SimulationResult public
        // while `30` §11.2 is 🔒 that CombatSimulator and PowerCalculator are "the only two `Rules`
        // types that are public" — and said that whoever landed the type had to get a ruling first
        // and then do both halves in one commit.
        //
        // Both halves are in that commit. R15 ruled that §11.2 enumerates public ENTRY POINTS rather
        // than the closure of the public surface, because C# requires a public Simulate's return and
        // parameter types to be public too (CS0050/CS0051), and R16 widened Domain.PublicRuleTypes to
        // the ENUMERATED signature closure — see its remarks, which carry both rulings. The type now
        // exists, so leaving the entry would fail Every_rule_subject_is_present_or_declared_pending.
        //
        // The names it left behind are tracked in Live below rather than dropped — this file's own
        // doctrine, stated three times over ("Moved out of Pending by M1-01 RATHER THAN DELETED",
        // "It has to be tracked HERE, not nowhere").

        // 🔒 A DEFERRAL, in the RunController entry's shape and for its reason (steering S4).
        //
        // `05` §3.1's tick order has a slot 5 — "pet ability cooldowns advance; ready abilities fire,
        // pets in slot order" — and M2-08 built it as a seam, Rules.Combat.IPetAbilities, whose
        // default NoPetAbilities does nothing. That default is CORRECT today and not a silent hole:
        // `18` §7.7's pet actives are a WRAPPER holding an effect list plus a cooldown, no such
        // wrapper type exists anywhere in the repository, and `05` §3 makes pets optional — so no pet
        // can currently carry an authored active for the slot to fire.
        //
        // ⚠️ But no M2 task owns it. The milestone's remaining tasks are M2-09 (damage), M2-10
        // (statuses), M2-12/M2-13 (bosses) and M2-14 (duels); `07` §2.1's pet actives belong to the
        // hero/pet milestone, which is not yet assigned. Without an entry here, nothing fires on the
        // day the wrapper type lands and NoPetAbilities must stop being the default — slot 5 would
        // keep running and keep doing nothing, which is a balance bug the harness would attribute to
        // pets being weak.
        //
        // ⚠️ THE NAME IS AN INFERENCE, recorded as one, exactly as the RunController entry records
        // its own. `05` §7 already spells the log event PetAbility (CombatEventType.PetAbility), so
        // PascalCasing the same noun for the wrapper is the precedent the documents set rather than a
        // name picked out of the air. If the hero/pet milestone picks another, RENAME this entry
        // rather than delete it: the subject being tracked is "something makes a pet ability fire",
        // not the string. NoPetAbilities' own remarks carry the inbound pointer, on DurationScopes'
        // precedent, because a note addressed to a future milestone is worthless in a test file it
        // will never open.
        new("PetAbility", SubjectKind.CoreType, "unassigned — the hero/pet milestone",
            "SlayIdleRepeat.Core.Rules.Combat.IPetAbilities and its NoPetAbilities default, which is " +
            "05 §3.1's slot 5 running and firing nothing"),

        // 🔒 PowerCalculator's entry lived here and was DELETED by M2-16a — the third discharge, and
        // the only one so far taken by a task that did not own the subject.
        //
        // The entry read: "the other member of Domain.PublicRuleTypes … M2-07 is landing Rules/Stats/
        // and this is the type that directory exists for (`29` §1, `30` §11.2)." M2-07 landed
        // Rules/Stats/ and did NOT land this type, so the entry survived its own milestone with a
        // stale owner — steering S4's ⚠️ known limit, an exemption whose REASON went stale while it
        // was still formally valid, which nothing detects mechanically.
        //
        // 🔒 M2-16a landed it because `05` §9 cannot be measured without it, not for tidiness. Two of
        // the six balance guardrails are defined over PlayerPower and over nothing else: guardrail 1
        // is "a player at exactly ParPower(c,t)", which `29` §2.5.3 reaches by BISECTING this
        // function, and guardrail 6 is "top-3 by MARGINAL power", which is its partial derivative. A
        // copy of `29` §2.3 inside tools/BalanceHarness would have made guardrail 6 grade the
        // harness's own arithmetic instead of the game's — R30, one directory over, exists to stop
        // exactly that drift between `05` §4's mitigation dials and `29` §2.3's.
        //
        // Nothing in Domain.PublicRuleTypes changed: the name has been in that list since M2-08, and
        // this is the type finally arriving under it. Its Compute overload THROWS on the shipped data
        // (tuning/power_model.json#/kPower is authored null and steering S6 forbids defaulting a
        // hole); PowerIndex is the K-free member every ratio uses, and `29` §2.1 defines the constant
        // by PlayerPower(referenceParBuild) := 1000, so a caller derives it rather than inventing it.
        //
        // The name it left behind is tracked in Live below rather than dropped, for the reason the
        // entry itself gave: Domain.PublicRuleTypes is a transcription of a 🔒 section, and the
        // exemption arm of Handlers_and_Rules_are_internal must not be able to go quiet through a
        // rename nobody notices.

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

        // 🔒 M2-04 wrote a `Run` DEFERRAL entry here — "whoever lands `Run` inherits an obligation:
        // a run must hold ONE RunTriggerCounters for its whole length and hand the same instance to
        // every battle's TriggerRegistry, and its snapshot must carry the pairs" — against the OTHER,
        // then-unfinished milestone's M1-05. M1-05 has since landed Run (see the Live entry below,
        // moved out of Pending by M1-05 itself before M2-04 was written against it), so the deferral
        // is DISCHARGED rather than still open, and the entry is deleted rather than kept: `Run` is a
        // Live subject now, not a Pending one, and keeping both would be exactly the "arrived twice"
        // shape Every_tracked_subject_name_is_read_by_a_rule_and_every_cited_rule_exists rejects. The
        // IRunTriggerCounters obligation itself is not lost — it is folded into the Live `Run` row
        // below, which is where a reader landing on the aggregate will actually look.
        // `18` §3: "ON_KILL counters PERSIST ACROSS BATTLES FOR THE RUN (PK_MIDAS's 'every 6th enemy
        // killed')." M2-04 owns the counter and its read/write seam
        // (Rules.Effects.Triggers.IRunTriggerCounters, with RunTriggerCounters as the in-Core
        // implementation and RunTriggerCountersContract as the shared suite, per steering S7); the
        // Run aggregate is what has to carry that state across battle boundaries, and rebuilding it
        // per battle resets PK_MIDAS every fight — a defect that produces a legal-looking log and is
        // wrong in the only number that matters. The controller that carries it between battles is
        // still M3's; M1-05 only had to make the obligation satisfiable, not build the controller.

        // 🔒 Not a rule subject — a DEFERRAL, in the Tier entry's shape and for the same reason
        // (steering S4). `18` §6 declares six duration scopes and M2-06 implements all six, but
        // STAGE, RUN and PERMANENT outlive a battle, so the battle-scoped evaluator answers
        // "not ended" for them and NOTHING ELSE IN THE REPOSITORY CONSUMES THEM. That is the
        // correct end state today (kickoff A4: "the run layer is declared, not wired"; `18` §2.5
        // gives the resolver to the run controller) and a placeholder controller would be a second
        // mechanism to find and delete later — but it is still a hole, and a hole nobody is
        // pointed at is a hole nobody closes.
        //
        // DurationScopes.OutlivesTheBattle is the one place that has to be read when the run layer
        // arrives: it is the ruling, named, with the three scopes on one side of it.
        //
        // ⚠️ THE NAME IS AN INFERENCE, recorded as one — but not a coin flip. `18` §2.5 writes "the
        // run controller" as its own noun, in the same sentence as "the combat simulator", and that
        // second noun is already a C# type spelled exactly that way (Domain.PublicRuleTypes,
        // `30` §11.2). PascalCasing §2.5's other noun is the precedent the document itself set, not
        // a name picked out of the air. It is still only an inference, so:
        //
        // 🔒 THE INBOUND PATH IS IN THE PRODUCTION CODE, not here. DurationScopes' own remarks name
        // this entry, because a note addressed to whoever lands M3 is worthless in a test file M3
        // will never open. If M3 picks another name, that remark sends them here to RENAME this
        // entry rather than leave it — the subject being tracked is "something ends a RUN-scoped
        // effect", not the string. If it picks this name, the rule below fires on its own and
        // deleting the entry is then correct, because the deferral has actually been discharged.
        new("RunController", SubjectKind.CoreType, "M3 — the run layer",
            "SlayIdleRepeat.Core.Rules.Effects.Duration.DurationScopes.OutlivesTheBattle, which sorts " +
            "18 §6's six scopes into the three a battle ends and the three it does not. The three it " +
            "does not have had no consumer since M2-06 declared them"),

        // ── M2-11 ───────────────────────────────────────────────────────────────────────────
        //
        // 🔒 Not a rule subject — a DEFERRAL, recorded in the one register the repo has so that it
        // expires by itself (steering S4), on the precedent of the `Tier` and `RunController`
        // entries above.
        //
        // `05` §6.2 is 🔒: "No Elite may draw the same modifier as the immediately preceding Elite
        // in the same run — redraw on collision", and it points at `24` §4.10's B2 protection.
        // That is RUN-SCOPED state, and the M2 kickoff's A4 ruling is that the run layer is declared
        // and not wired. M2-11 therefore ships the seam — Rules.Combat.Enemies.IEliteModifierHistory,
        // with EliteModifierHistory as the in-Core implementation and EliteModifierHistoryContract as
        // the shared suite every implementation is run through (steering S7) — and deliberately
        // builds no LuckService and no pity mechanism.
        //
        // ⚠️ Whoever lands `LuckService` inherits an obligation, and it is the one that makes the
        // difference between the rule working and the rule being decoratively present: a run must
        // hold ONE IEliteModifierHistory for its whole length and hand the same instance to every
        // Elite encounter in it. A fresh instance per battle leaves PreviousEliteModifier
        // permanently null, the redraw never fires, and every test of the draw still passes.
        // IEliteModifierHistory's own remarks state the full five-point contract, because a note
        // addressed to M4-01 is worthless in a test file M4-01 will never open.
        //
        // This entry is the expiry: Every_rule_subject_is_present_or_declared_pending fails the
        // moment a type named LuckService exists, which is exactly when the obligation lands.
        //
        // 🔴 M4-01 LANDED THE TYPE AND DID **NOT** DISCHARGE THE OBLIGATION. The entry has therefore
        // MOVED to Live below — the mechanism fired exactly as designed, and leaving it here would
        // fail the rule — but moving it is bookkeeping, not a discharge, and the two must not be
        // confused. What follows is the state of the obligation itself, written here because this is
        // where the next reader of the LuckService row will be standing.
        //
        // ⚠️ STILL OPEN, AND STILL BROKEN. EncounterFight.Run — the only production caller — builds
        // its history as a LOCAL, one per battle: `EliteModifierHistory.Restore(null)`. So
        // PreviousEliteModifier is permanently null, 05 §6.2's redraw has never once excluded
        // anything, and every test of the draw passes over a rule that is present and inert. That is
        // worse than a missing rule, because nothing looks wrong.
        //
        // 🔒 WHY M4-01 COULD NOT TAKE IT, and why it is not re-keyed to a later type here. M4-01
        // wires no run at all: it authors the pity façade, the guarantee primitives, the tuning
        // reader and the counter map, and touches nothing that spans two battles. And the fix is NOT
        // TYPE-SHAPED — it is a FIELD on the existing Run aggregate plus threading one instance
        // through every Elite encounter of that run — so a type-keyed Pending row is the wrong home
        // for it, by this file's own filter. That is precisely the argument the IStatOpBehaviour note
        // in Live below makes for itself: Live and Pending exist for subjects whose SILENT
        // disappearance would leave a rule vacuous, and a row that can never fire is dilution. The
        // finding is carried in PRODUCTION CODE instead, where the next reader of the seam will
        // actually look — IEliteModifierHistory's own remarks and EncounterFight's, both strengthened
        // in M4-01's commit to name the defect, the shape of the fix and the owner.
        //
        // 🔒 OWNER: M4-02, which still owns DROP_RUN's D1-D3 counters — the per-run luck state this
        // history was expected to ride in beside.
        //
        // 🔴 THE PREMISE THAT PICKED THAT OWNER HAS SINCE GONE FALSE, recorded rather than quietly
        // left: the row was written on the argument that M4-02 would be "the task that gives the Run
        // aggregate its FIRST persisted luck state", so a per-run instance would have to be threaded
        // for the first time in that commit anyway. M4-01b got there first — the three run-scoped
        // DRAFT guarantee counters are persisted Run fields as of that task — and it did NOT carry
        // the Elite history, because the counters need no per-battle instance threaded through the
        // encounter path and this does. The owner is unchanged and the obligation is undischarged;
        // what is gone is the "it comes along for free" half of the reason. Naming a later milestone
        // would be this file inventing a plan; naming none is what let PowerCalculator's entry go
        // stale inside its own milestone.
        //
        // ⚠️ AND ONE THING M4-01 DID SETTLE, so M4-02 does not have to re-derive it: the
        // implementation must NOT live on the façade. R17 now carries three Luck edges
        // (IntraRulesLayeringRuleTests.ForbiddenEdges), and Rules.Luck naming Rules.Combat is
        // forbidden outright rather than merely undecided — which is what the original entry
        // predicted and what the edges make true.

        // 🔒 A DEFERRAL with teeth, recorded because the architecture review found the hole it
        // closes. 05 §6.2's eight modifiers are authored as named PARAMETER numbers rather than as
        // embedded 18 §1 effect JSON — the content pipeline cannot validate embedded effect JSON
        // (JsonSchemaValidator resolves same-document pointers only, and
        // EffectSchemaTests.No_other_schema_restates_the_effect_vocabulary rejects a second copy of
        // the op set), so authoring it would ship it unvalidated.
        //
        // ⚠️ The consequence is that each row has its OWN key vocabulary — atkMult+belowHpFraction,
        // defMult+aspdMult, lifesteal, deathExplosionHeroMaxHpPct, startingWardMaxHpPct, aspdMult,
        // killWithinSeconds, thorns — so a consumer must know which keys belong to which modifier,
        // and the path of least resistance is `switch (modifier)` in the tick engine. That is per-
        // enemy code, which 18's headnote forbids, and EnemyDerivationRuleTests.
        // No_elite_identity_is_named_in_code cannot see it: EliteModifier is an ENUM, so a switch
        // over it emits no string literal.
        //
        // M2-11 deliberately wrote NO consumer, because completing the eight effect shapes needs
        // targets, triggers and a curse id 05 §6.2 does not state (steering S6) — CURSED's curse id
        // is null in the data for exactly that reason. Whoever writes the first consumer owes ONE
        // table from EliteModifier to its 18 §1 effects, in Rules/Combat/Enemies/, and a rule that
        // fails a switch over EliteModifier anywhere else.
        //
        // This entry is the expiry: it fires the moment a type named EliteModifierEffects exists,
        // which is when the obligation has been discharged and the note should be deleted. If the
        // consumer picks another name, RENAME this entry rather than dropping it — the subject being
        // tracked is "one table maps the modifiers to effects", not the string.
        //
        // 🔴 THE OWNER WENT STALE INSIDE ITS OWN MILESTONE, and the architecture review corrected it.
        // M2-11 wrote this entry against "M2-08 / M2-13 — the first consumer". Both tasks then landed
        // and NEITHER wrote one: M2-08's tick engine takes a roster of ActorPlans and never reads a
        // modifier row, and M2-13 authored bosses, which have no elite modifiers at all. The entry
        // was still formally valid — the type does not exist, so the rule it guards is still vacuous
        // for the reason stated — while its stated owner had passed, which is exactly S4's known
        // limit ("an exemption whose REASON went stale while still formally valid is not mechanically
        // detectable") and exactly the shape M2-16a found in PowerCalculator's entry.
        //
        // It is deliberately NOT reassigned to a guessed task. `05` §6.2's modifiers are consumed
        // when an elite is spawned into a real encounter, which is the run layer's (M3) at the
        // earliest, and the draw itself is M4-01's per the LuckService entry above; naming either
        // here would be this file inventing a plan. The convention the PetAbility, Tier and
        // StatusDecayCurve entries already use is the honest one.
        //
        // 🔒 The HALF of this note that was an unguarded obligation is now guarded. It said the
        // elite-id rule "is blind to an enum switch" and left it there.
        // EnemyDerivationRuleTests.No_elite_modifier_is_named_in_code_outside_the_enemy_namespace
        // closes that: it scans the enum's TYPE reference rather than a string literal, and it
        // permits Rules/Combat/Enemies/ — the directory this entry already names as the table's home.
        // The entry survives because the rule cannot see the OTHER half: that no table exists at all.
        //
        // 🔴 M2-R4 UPDATE — the owner named above ("unassigned — the first consumer") has arrived and
        // the table is STILL missing, which is the reopening this entry's remarks predicted.
        // EncounterFight (SlayIdleRepeat.Core.Rules.Combat.Enemies.EncounterFight) is the first
        // production caller of EliteModifierDraw.Draw — it wires 05 §6.2's Elite treatment into a
        // real, publicly reachable fight (CombatSimulator.SimulateEncounter) — and it deliberately
        // draws the modifier and discards it rather than resolving it into an 18 §1 effect, for the
        // same reason M2-11 never wrote a consumer: CURSED's curse id is null (content/curses/ is
        // empty) and VOLATILE needs a hero-naming target token no ruling has settled, so a six-of-
        // eight table would retire this deferral while two modifiers silently did nothing — worse
        // than the gap staying open and named. The owner column below is corrected to name the actual
        // blocker rather than "the first consumer", which no longer describes what is missing.
        new("EliteModifierEffects", SubjectKind.CoreType,
            "unassigned — needs 05 §6.2's CURSED curse id and R3a's VOLATILE target-token ruling",
            "05 §6.2's modifier parameters, which have a real caller (EncounterFight) but no effect " +
            "translation; 18's headnote forbids the per-modifier branching that is otherwise the path " +
            "of least resistance. The branching itself is caught by EnemyDerivationRuleTests." +
            "No_elite_modifier_is_named_in_code_outside_the_enemy_namespace; this entry tracks the " +
            "missing table, which no rule can see"),

        // ══════════════════════════════════════════════════════════════════════════════════════
        // ── M2-02 · `18` §8 step 1's NINE ABSENT SOURCES ──────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════════════════════
        //
        // 🔒 Nine DEFERRALS, not rule subjects — recorded here because this is the repo's one
        // register and each must expire by itself (steering S4), on the precedent of the `Tier` and
        // `RunController` entries above.
        //
        // `18` §8 step 1 is "collect all active effects from: gear → affixes → set bonuses →
        // talents → pet auras → mount → run buffs → shrine buffs → curses → perks (in draft
        // order)". M2-02 owns that step and built the collector — Rules.Effects.EffectSourceSet,
        // over Rules.Effects.EffectSourceCatalogue's ten declared rows. NINE of the ten have no data
        // model anywhere in the repository, so nine of the ten slots can never be filled today and
        // the collector walks past them. Steering S6 forbids stubbing them with plausible shapes,
        // and nine invented models would be nine things nine later milestones each had to find and
        // delete. So each is DECLARED and each is tracked here.
        //
        // ⚠️ Each is keyed on the name whose ARRIVAL fires — the opposite direction from M2-03's two
        // entries below, and the right one here: the deferral is discharged when the owning
        // milestone lands a data model, which is exactly when somebody has to come back to
        // EffectSourceCatalogue and wire that source.
        //
        // ⚠️ THE NAMES ARE INFERENCES, on the RunController entry's precedent — each is the type the
        // tracker row for that milestone describes. If the owning milestone picks another name, the
        // correct action is to RENAME the entry, not delete it: the subject being tracked is "this
        // 18 §8 step 1 source now has something to collect from", not the string. The inbound path
        // is in the PRODUCTION code — EffectSourceCatalogue's remarks name this file — because a
        // note addressed to M4-03 is worthless in a test file M4-03 will never open. And
        // EffectSourceCatalogueTests.The_pending_expiry_subjects_are_distinct keeps two sources from
        // sharing one entry, which would untrack the second when the first arrived.

        // 🔒 M4-03 RAN, AND THESE THREE STAY PENDING — with their reasons rewritten, because the
        // reason going stale while the predicate still holds is the one case CI cannot catch and
        // this milestone is the reader that has to falsify it. What M4-03 landed is the DATA MODEL:
        // Model.Gear.GearInstance, the fourteen-affix pool behind Content.DropsTuning, and
        // Rules.Gear.SetBonusResolver, which answers which of the authored breakpoints an equipped
        // loadout has reached. What step 1 still cannot collect from is TWO things, neither of them
        // M4-03's: an EQUIPPED LOADOUT on the aggregate (M4-05 has since landed the INVENTORY —
        // Player.Inventory holds what a player owns — but owning an item is not wearing one, and
        // which items are equipped is M4-10's), and the EFFECT CONTENT itself — no affix and no set bonus has an authored
        // EffectDefinition, op or magnitude anywhere in the design set, and inventing one would be
        // the plausible-looking hole S6 forbids sitting under the whole stat pipeline.
        //
        // ⚠️ The subject names are still the catalogue's inferences and M4-03 deliberately did not
        // author a type by any of them — its types are GearInstance, GearAffixRoll (the rolled
        // affix), GearAffixDefinition (the pool row) and ActiveSet. If the milestone that wires
        // these picks other names, rename the entries rather than deleting them: what is tracked is
        // "this source now has something to collect from", not the string.
        // 🔒 M4-16 DISCHARGED THE GearItem, GearAffix AND SetBonus ENTRIES THAT USED TO SIT HERE, and
        // it is a discharge rather than an expiry: the three sources are WIRED, not merely unblocked.
        // Rules.Effects.GearEffectSource reads the equipped items' own two stats at the enhancement
        // level they stand at; Rules.Effects.GearAffixEffectSource reads the rolled affixes through
        // the stat/op mapping now authored on every row of tuning/drops.json#/affixPool/affixes; and
        // Rules.Effects.SetBonusEffectSource joins Rules.Gear.SetBonusResolver's answer to the effects
        // authored in content/sets/sets.json. Rules.Stats.HeroBuild composes all three through
        // EffectSourceSet and StatAggregation.
        //
        // 🔒 THE REMOVAL IS FORCED, which is what makes it a discharge rather than a tidy-up. The
        // three EffectSourceCatalogue rows carry a null PendingSubject as of the same commit, so
        // EffectSourceDeferralRuleTests' floor drops from ten to seven there — and an entry left
        // standing here would key an expiry on a name nothing will ever author.
        //
        // ⚠️ WHAT IS NOT DISCHARGED, and it is a different shape rather than a smaller version of the
        // same one: one of the fourteen affixes and seven of the twelve set-bonus effects are still
        // unauthored. Those are not source deferrals — the sources collect them the moment they are
        // authored — so they are not entries here. They are holes in the DATA, carried as nulls the
        // content-hole pin counts, with their owners in GearAuthoringGapRegister, which checks the
        // owning task is still OPEN rather than merely present in the tracker.
        new("TalentNode", SubjectKind.CoreType, "M4-06",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 4 of 10, " +
            "'talents'. 09's 60-node tree is M4-06's"),
        new("PetDefinition", SubjectKind.CoreType, "M4-07",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 5 of 10, " +
            "'pet auras'. ⚠️ 18 §7.7's `aura` block ONLY — the sibling `active` block is an ability " +
            "on the pet's own cooldown and step 1 does not collect it, which is what scopes " +
            "EffectDefaults' absent-trigger ruling"),
        new("MountDefinition", SubjectKind.CoreType, "M4-08",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 6 of 10, " +
            "'mount'. 07 §3's 12 mounts are M4-08's"),
        new("RunBuff", SubjectKind.CoreType, "M3-08",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 7 of 10, " +
            "'run buffs'. 03 §7's shop consumables and run-scoped grants are M3-08's"),
        new("ShrineBuff", SubjectKind.CoreType, "M3-11",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 8 of 10, " +
            "'shrine buffs'. 03 §7a's shrine is M3-11's"),
        new("Curse", SubjectKind.CoreType, "M3-11",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 9 of 10, " +
            "'curses'. 19 E's 12-curse catalogue is M3-11's"),

        // ── M2-14 ───────────────────────────────────────────────────────────────────────────
        //
        // 🔒 Not a rule subject — a DEFERRAL, recorded here so it expires by itself (steering S4), on
        // the precedent of the `RunController` and `LuckService` entries above.
        //
        // `11` §4.3 is 🔒: "on an exact tie, the LOWER-RATED player wins." M2-14 implements it, and
        // the simulator takes it as one bit — CombatRules.Duel's `lowerRatedSide` — because rating is
        // `11` §5's and `30` §11.1 keeps Elo out of the tick loop. What M2-14 deliberately does NOT
        // build is the thing that decides that bit: `11` §4's duel flow, its candidate selection and
        // its server-issued seed are M12's, and no ghost, ladder or rating type exists yet
        // (GhostSnapshot is still Pending above).
        //
        // ⚠️ Whoever lands the duel flow inherits an obligation, and it is not obvious from the call
        // site: `lowerRatedSide` is a LogHash INPUT. The outcome reaches ON_BATTLE_END's HeroWon, and
        // those firings append to the log before it is hashed (`18` §9.2's win-only PET_DICEBEAST
        // grant is the authored example) — so `11` §6 compares a hash that this bit moved. It
        // therefore needs `11` §6's duelSeed discipline exactly: SERVER-ISSUED, fixed once at duel
        // start, travelling with the seed. A rating re-read at re-run time, or an attacker rating
        // that moved between the client's fetch and the server's, discards an HONEST duel and
        // increments the player's cheat flag — the anti-cheat firing on the anti-cheat.
        // CombatRules.Duel's own remarks carry the full contract, because a note addressed to M12 is
        // worthless in a test file M12 will never open.
        //
        // ⚠️ THE NAME IS AN INFERENCE, recorded as one on the RunController entry's precedent, and it
        // is `11`'s own title noun ("Ghost Duel") PascalCased. If M12 picks another, RENAME this entry
        // rather than delete it: the subject being tracked is "something decides which duellist is the
        // underdog and issues it with the seed", not the string.
        new("GhostDuel", SubjectKind.CoreType, "M12 — 11 §4's duel flow",
            "SlayIdleRepeat.Core.Rules.Combat.CombatRules.Duel's lowerRatedSide — 11 §4.3's exact-tie " +
            "underdog bias, which M2-14 implements and nothing yet decides. See the note above this " +
            "entry: the bit is inside LogHash, so it needs 11 §6's server-issued duelSeed discipline " +
            "or an honest duel is discarded as tampering"),

        // ── M2-10 ───────────────────────────────────────────────────────────────────────────
        //
        // 🔒 Two DEFERRALS, not rule subjects — recorded in the one register the repo has so that each
        // expires by itself (steering S4), on the precedent of the `Tier` and `RunController` entries
        // above.
        //
        // 1. `05` §5 says RAGE is "+X% ATK, DECAYS OVER D s" and states no curve — not linear, not
        //    stepped, not exponential — and no boss script, perk row or on-hit row in the content set
        //    authors one either. Steering S6 forbids inventing it, so RAGE ships holding its full
        //    potency for its duration (the only shape `18` §6 can express) and the missing decay is
        //    an authored null at content/statuses.json#/statuses/8/decayCurve, counted by
        //    RealDataNegativeCaseTests' hole guard and refused by name by
        //    StatusDefinition.RequireDecayCurve.
        //
        // ⚠️ THE NAME IS AN INFERENCE, recorded as one. `05` §5 writes no noun for the curve, so the
        // entry is keyed on the type a milestone would have to add to express one. If the milestone
        // that rules on it picks another name, RENAME this entry rather than delete it: the subject
        // being tracked is "something gives RAGE its decay", not the string. The inbound path is in
        // PRODUCTION code — StatusDefinition.RequireDecayCurve's remarks name this entry — because a
        // note addressed to a future milestone is worthless in a test file it will never open.
        new("StatusDecayCurve", SubjectKind.CoreType, "unassigned — whoever rules on 05 §5's RAGE",
            "SlayIdleRepeat.Core.Rules.Combat.Status.StatusDefinition.DecayCurve, which is null " +
            "because 05 §5 states that RAGE decays and states no curve for the decay"),

        // 🔒 Domain.CommandsNamespace (M1-06), Domain.EventsNamespace (M1-03), Domain.HandlersNamespace
        // (M1-09) and Domain.TestingNamespace (M1-11) do NOT belong here: M1 already moved all four to
        // Live below (see the M1-06/M1-09/M1-11 rows in Live), and review/M2 branched before that
        // landed on main, carrying stale Pending rows for names M1's side had already discharged.
        // Per this merge's M1-vs-M2 discharge ordering, M1's Live entries win and these duplicate
        // Pending rows are dropped rather than merged — keeping them here would fail
        // Every_rule_subject_is_present_or_declared_pending the moment this file compiles, since all
        // four namespaces already exist.
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
        // 🔒 THE DOC CONTRADICTION THIS ROW CARRIED SINCE M1-03 IS DISCHARGED (steering S16). It read:
        // 30 §11.4's chain omits Commands and Events entirely, while 30 §7 writes
        // GearGranted(int, GearInstance, SourceClass, bool) — so a row forbidding Events -> Model
        // would contradict 30 §7 and block M4-03, and a row permitting it would put an aggregate in
        // a list that leaves the domain. M1-03, M1-06 and M1-11 each closed the halves that were
        // unambiguous and left this one open BY NAME, with the M4 kickoff as its owner.
        //
        // RULED at the M4 kickoff (2026-08-16) and landed by M4-03: Events -> Model is PERMITTED,
        // NARROWLY — an event may name a Model/ type only when that type is an immutable, fully
        // serialisable value record with no mutators, never an aggregate ROOT and never a Model/
        // type carrying an internal mutator. 30 §11.4 carries the amendment (the chain is now
        // Testing -> Handlers -> Rules -> Model -> Content -> Primitives with Commands and Events as
        // peer leaves), DomainEvent's own remarks no longer say "not an aggregate", and the
        // enforcement is AccessibilityBoundaryTests
        // .An_event_names_a_Model_type_only_when_it_is_an_immutable_value_record — a rule of its own
        // rather than a forbidden PAIR, because the permitted reference and the forbidden one go to
        // the same namespace and differ only in the shape of the type reached.
        //
        // Events still has no row in the forbidden-pair table for Model, and now that is a decision
        // with a reason rather than a gap: it has one for Rules, Handlers and Testing, and it is in
        // mustNotReachTheRoot.
        new(Domain.EventsNamespace, SubjectKind.CoreNamespace, "M1-03",
            "AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace, " +
            "AccessibilityBoundaryTests.Core_internal_layering_holds (the mustNotReachTheRoot half and " +
            "the Rules/Handlers/Testing row), " +
            "AccessibilityBoundaryTests.An_event_names_a_Model_type_only_when_it_is_an_immutable_value_record"),

        // 🔒 M1-vs-M2 MERGE NOTE: Domain.RulesNamespace was moved out of Pending independently by BOTH
        // milestones — M1-10 (Core/Rules/Economy/, the 10 §3 / 28 C energy math) and M2-15
        // (Core/Rules/Combat/, the `05` §7 combat log format, with M2-07's Core/Rules/Stats/ wave
        // writing the same entry a third time). This is exactly the duplicate the M2-15 entry's own
        // remarks predicted and pre-authorised deleting: "the loser's entry is a duplicate to delete,
        // not a second subject." M1-10 landed on main first, so its row survives below, directly after
        // CanonicalStateWriter per its own placement note; M2-15's extra citation
        // (AccessibilityBoundaryTests.Core_internal_layering_holds) is folded into that surviving row
        // rather than dropped, since M2-15's wave is what actually populated Core/Rules/ with
        // non-Economy types and both rules the merged citation names now have real subjects either way.

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
            "IntraRulesLayeringRuleTests.The_namespaces_R17_governs_are_the_ones_under_Rules (pins its " +
            "own restated StatsNamespace constant against this one, so a rename here that missed R17's " +
            "file would make it govern a namespace that no longer exists), DamageResolutionRuleTests " +
            "(MitigationConstants' declaring namespace is this constant's value, so a rename that left " +
            "the type where it is would still be caught by prefix)"),


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

        // 🔒 M2-04's two vacuity-prone subjects, and only those two. Most of M2-04's types are named
        // in C# by the tests that cover them, so a rename is a compile error rather than a silent
        // vacuity and an entry here would buy nothing but dilution. These two are different:
        //
        //   TriggerCatalogue     — the 23-kind floor is stated over its ROWS, which is why the
        //                          catalogue builds its own inventory rather than deriving it from
        //                          Enum.GetValues. Losing a row is precisely the silent shrink S3
        //                          watches for, and TriggerCatalogueTests' theories are MemberData
        //                          over it, so they would quietly quantify over less.
        //   IRunTriggerCounters  — the seam RunTriggerCountersContract runs every implementation
        //                          through (steering S7), and the counterpart of the Run entry in
        //                          Pending, which is where its persistence lands. It is the seam's
        //                          name that carries the cross-milestone obligation.
        new("TriggerCatalogue", SubjectKind.CoreType, "M2-04",
            "TriggerCatalogueTests — 18 §11's 23-kind floor and 18 §3.1's narrowing/constitutive " +
            "partition, both stated over the catalogue's own rows; TriggerFiringTests' MemberData"),
        new("IRunTriggerCounters", SubjectKind.CoreType, "M2-04",
            "RunTriggerCountersContract — the shared contract suite every implementation of the " +
            "run-scoped ON_KILL counter is run through (steering S7); paired with the Run entry in " +
            "Pending, which is where its persistence lands"),

        // 🔒 The four evaluators M2-06 added, tracked by NAME for the reason the two above are: the
        // count floor below is one number over THREE namespaces, so moving any one evaluator out
        // would leave the floor satisfied by the record structs that travel with the other two while
        // EffectEvaluationPurityRuleTests quietly stopped covering the type it was written for.
        new("ValueScaleEvaluator", SubjectKind.CoreType, "M2-06",
            "EffectEvaluationPurityRuleTests.An_effect_evaluator_never_draws_and_never_reads_a_clock, " +
            "EffectEvaluationPurityRuleTests.An_effect_evaluator_holds_no_mutable_static_state"),
        new("ValueModeEvaluator", SubjectKind.CoreType, "M2-06",
            "EffectEvaluationPurityRuleTests.An_effect_evaluator_never_draws_and_never_reads_a_clock, " +
            "EffectEvaluationPurityRuleTests.An_effect_evaluator_holds_no_mutable_static_state"),
        new("DurationEvaluator", SubjectKind.CoreType, "M2-06",
            "EffectEvaluationPurityRuleTests' two rules — and it is the one type in the three " +
            "namespaces that holds a static field at all"),
        new("EffectStackSet", SubjectKind.CoreType, "M2-06",
            "EffectEvaluationPurityRuleTests' two rules — 05 §3.1 re-reads its Count at the moment " +
            "each 20 Hz tick lands, which is where a cache would be added"),

        // ── M2-03, as M2-02 left them ───────────────────────────────────────────────────────
        //
        // M2-03 recorded TWO deferrals here, both caused by R17 making `Rules.Effects` the bottom of
        // the intra-`Rules` layering, and both keyed to M2-02's relocation of the `18` seams out of
        // `Rules/Stats/`. M2-02 has now run. ONE of them is discharged and its entry is DELETED; the
        // other turned out to be impossible and its entry is REWRITTEN rather than removed.
        //
        // 🔴 DELETED — `StatRounding` / `OpRounding`. The entry read: "OpRounding is a SECOND
        // statement of `05` §1.1's 4-dp rule … the real fix is a shared primitive under
        // Core.Primitives; when StatRounding moves or is replaced, this entry fails and whoever did
        // it has to delete OpRounding." That primitive is now
        // SlayIdleRepeat.Core.Primitives.DeterminismRounding, and it works where StatRounding could
        // not because `30` §11.4 puts Primitives beneath EVERY layer that rounds — under
        // Rules.Effects and Rules.Stats alike, and under Content, which rounds too and may name
        // neither. SIX statements of the rule collapsed onto it, not two: StatRounding, OpRounding,
        // ValueScale, ConditionEvaluator, TriggerInstance and TriggerRouting. StatRounding and
        // OpRounding survive as their own FAILURE MESSAGES — which were never the duplicated part,
        // and which steering S2 wants kept distinct — so the entry is satisfied and a satisfied
        // exemption that stays is a stale one (steering S4).
        //
        // ⚠️ What replaces it is a real rule rather than nothing:
        // DeterminismRoundingRuleTests.The_4_dp_rule_of_05_1_1_is_stated_in_exactly_one_place scans
        // the IL of Core and Application for a Math.Round with a literal precision of 4 outside the
        // primitive, and carries its own S3 floor. A tracked NAME could only ever have caught the
        // duplication being removed; the rule catches a seventh being added.

        // 🔴 ALSO DELETED — `IStatOpBehaviour`. Not because the deferral was discharged (it was not),
        // but because this register is the wrong home for it, by this file's own stated filter.
        //
        // The entry read: "M2-02 owns the relocation that closes the split." M2-02 attempted it and
        // found R17 forbids it: Convert takes an ActorStats and OverrideCaps takes and returns a
        // StatCaps, so moving the interface DOWN to Rules.Effects would make the bottom layer name
        // the one above it. R17 is both what was cited to require the move and what blocks it.
        //
        // ⚠️ THAT FINDING IS NOT LOST — it is in PRODUCTION CODE, in StatAggregationSeams.cs's
        // remarks on the interface itself, with the two designs that WOULD close it (a read-only
        // view seam on IResolvedStatReader's precedent; or moving `05` §1's vocabulary) and why
        // M2-02 chose neither. That is where the next reader of the interface will actually look,
        // and it is the same argument this file makes for EffectSourceCatalogue's inbound path.
        //
        // 🔒 Why not keep the entry as well: `Live` exists for subjects whose SILENT disappearance
        // would leave a rule vacuous, and this file says so itself thirty lines up — "most of M2-04's
        // types are named in C# by the tests that cover them, so a rename is a compile error rather
        // than a silent vacuity and an entry here would buy nothing but DILUTION". IStatOpBehaviour
        // is named in C# by production code (StatAggregationSeams' record), so renaming or deleting
        // it is a compile error across the solution, and no namespace-filtered rule keys on it, so a
        // MOVE costs nothing either. The entry could never fire for the reason Live entries exist.

        // ── M2-11 ───────────────────────────────────────────────────────────────────────────
        //
        // 🔒 The two names EnemyDerivationRuleTests keys on that a rename could empty in silence.
        // Its three rules are stated over a namespace FILTER (Rules.Combat.Enemies), which the floor
        // in that file covers — but two of them additionally key on a NAME:
        //
        //   IEliteModifierHistory — the seam EliteModifierHistoryContract runs every implementation
        //                           through (steering S7), and the counterpart of the LuckService
        //                           entry in Pending, which is where its run-scoped persistence
        //                           lands. It is the seam's NAME that carries the obligation.
        //   EnemyCatalogue        — the one type that reads content/enemies/enemies.json. Rename or
        //                           delete it and nothing reads 05 §6's tables at all, while the
        //                           namespace floor stays satisfied by the value types beside it and
        //                           the whole enemy-derivation suite goes on passing over fixtures.
        // 🔴 M4-01 CORRECTED THIS ROW'S CROSS-REFERENCE. It said the seam was "paired with the
        // LuckService entry in Pending, which is where its persistence lands", and both halves of
        // that went stale in the same commit: LuckService has arrived and is a Live row three
        // entries down, so there is no Pending entry to pair with — and it was never where the
        // persistence lands either. The persistence is a field on Run, owned by M4-02; the discharge
        // note in Pending above carries the full argument and the reason a type-keyed row is the
        // wrong home for what is left.
        new("IEliteModifierHistory", SubjectKind.CoreType, "M2-11",
            "EliteModifierHistoryContract — the shared contract suite every implementation of 05 §6.2's " +
            "run-scoped no-repeat memory is run through (steering S7). ⚠️ Its run-scoped persistence " +
            "does NOT land on the luck façade — see the discharge note in Pending above: it is a field " +
            "on the Run aggregate, owned by M4-02, and R17's Luck edges now forbid the alternative " +
            "outright"),
        new("EnemyCatalogue", SubjectKind.CoreType, "M2-11",
            "the single reader of content/enemies/enemies.json — 05 §6's derivation constants, level " +
            "table, archetype rows, on-hit tables, elite modifiers and identities and chapter pools " +
            "all enter Core through it, and EnemiesDataTests asserts the document it names"),

        // ── M4-01 ───────────────────────────────────────────────────────────────────────────
        //
        // 🔒 MOVED out of Pending by M4-01 rather than deleted, on this file's own doctrine and for
        // CombatSimulator's stated reason: a name that has arrived is TRACKED here, not dropped. The
        // Pending entry it replaces carried a cross-milestone obligation that is NOT discharged —
        // the full note is in Pending above, and the short version is that 05 §6.2's run-scoped
        // Elite history is a field on Run owned by M4-02, not anything M4-01 could build.
        //
        // ⚠️ WHY THE NAME NEEDS TRACKING NOW THAT IT EXISTS, which is a different reason from the
        // one it was tracked for while absent. LuckRoutingRuleTests keys on the literal simple name
        // in FIVE places at once: the routing arm asks whether a producer calls it, the guarantee
        // arm exempts the namespace it lives in, the identity floor asserts that it declares a
        // Resolve member at all, the grant-class coverage arm asks which of its members each class
        // is reached through, and the closed-surface arm asks whether every member production calls
        // is one this file classifies. Rename the façade without renaming
        // LuckRoutingRuleTests.LuckFacade and the failure is LOUD rather than silent — the identity
        // floor fires — which is worth writing down because it is the opposite of this file's usual
        // failure mode, and because the row is what tells whoever does the rename which arms they
        // have just moved.
        new("LuckService", SubjectKind.CoreType, "M4-01",
            "LuckRoutingRuleTests.No_grant_outcome_is_produced_outside_the_luck_service (the routing " +
            "arm — 24 §11's 'every protected grant goes through one façade', matched by this exact " +
            "simple name), LuckRoutingRuleTests.A_guarantee_can_only_fire_inside_Rules_Luck (the " +
            "sole-façade arm, which is what stops a producer naming the façade and reaching past it " +
            "into HardPity anyway), LuckRoutingRuleTests." +
            "The_routing_rules_subject_set_is_the_one_it_was_written_against (the identity floor — it " +
            "asserts the type exists, sits under Rules.Luck and declares Resolve, so a rename turns " +
            "this red instead of quiet), LuckRoutingRuleTests." +
            "Every_live_grant_source_class_routes_through_the_facade_and_the_rest_have_no_caller " +
            "(the coverage arm — per 24 §3 grant class, which façade member reaches it and who " +
            "calls that member), LuckRoutingRuleTests." +
            "Every_facade_member_production_calls_is_a_grant_class_this_file_classifies (the closed " +
            "surface — every member production reaches for is one a row classifies, which is what " +
            "covers a class no entry point serves yet)"),

        // 🔒 The counter map, tracked by NAME because two mechanisms key on it and neither is a
        // compile-time reference. GapRegister's 30 §4 Player-contents transcription still lists
        // "PityCounters", and Undeclared() now finds it authored under Domain.ModelNamespace with no
        // Deferred entry carrying it — rename or move the type out of Core/Model/ and that
        // transcription reports an undeclared gap for a type that exists.
        //
        // 🔴 THE HALF THIS ROW USED TO CARRY IS DISCHARGED, and the note is corrected rather than
        // left standing. It said the type was "the ARGUMENT the stateless luck façade takes, not a
        // field on Player", that 30 §4's "all pity counters (24)" line had no home on the aggregate,
        // and that M4-02 owned the field, the snapshot column and the SchemaVersion bump. M4-01b
        // landed all three: the chest pick's gold-tier guarantee is PLAYER-scoped and lifetime, so a
        // run-scoped home would reset it every run and put a four-miss guarantee out of reach.
        // M4-02 still owns the chest ladders and the in-run drop mercy that write the map further;
        // it does not own adding the field a second time. Nothing mechanical fires on either version
        // of that note — GapRegister's predicate is a type simple name and the type is authored —
        // which is exactly why it had to be re-read rather than trusted (steering S4's known limit).
        new("PityCounters", SubjectKind.CoreType, "M4-01",
            "GapRegister.Surfaces — 30 §4's Player-contents transcription enumerates it, and " +
            "GapRegisterTests.Every_subject_the_design_docs_enumerate_is_authored_or_declared_deferred " +
            "is what decides whether an enumerated name is authored under Domain.ModelNamespace or " +
            "carried by a Deferred entry. M4-01 authored it, so the entry that used to carry it is " +
            "gone and the transcription now rests on the type being where it is"),

        // ── M2-16a ──────────────────────────────────────────────────────────────────────────
        //
        // 🔒 Moved out of Pending by M2-16a rather than deleted, on EnemyCatalogue's precedent
        // directly above and for its reason. It is the ONE type that reads
        // content/bosses/bosses.json: rename or delete it and nothing reads 17 §1.2's nine rows at
        // all, while BossEngineRuleTests' own floor over Rules.Combat.Bosses stays satisfied by the
        // phase controller, the telegraphs and the built-ins beside it.
        //
        // See the discharge note in Pending above for what landed with it — the test-side
        // AuthoredBossScripts reader and its game-data EmbeddedResource both went in the same commit.
        new("BossCatalogue", SubjectKind.CoreType, "M2-16a",
            "the single reader of content/bosses/bosses.json — 17 §1.2's nine coefficient rows, the " +
            "baseline secondaries, every script's own effect set and every 17 §1 wind-up enter Core " +
            "through it; AuthoredBossScriptTests, AuthoredBossFightTests and BossCatalogueTests all " +
            "assert the shipped document through it, and tools/BalanceHarness builds its boss " +
            "encounters from it"),

        // 🔒 The second member of Domain.PublicRuleTypes to arrive, moved out of Pending by M2-16a
        // rather than deleted — CombatSimulator's entry above made the same move for the same reason.
        // `30` §11.2 is 🔒 that these two are "the only two `Rules` types that are public", so the
        // list is a transcription of a locked section: a rename that emptied it would make the
        // EXEMPTION arm of Handlers_and_Rules_are_internal vacuous, and a vacuous exemption is
        // silent rather than loud. See the discharge note in Pending above.
        new("PowerCalculator", SubjectKind.CoreType, "M2-16a",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal — the exemption arm of the " +
            "rule; Domain.PublicRuleTypes names it and nothing else pins that name. It is also the " +
            "single statement of `29` §2.3's PlayerPower, which `05` §9's guardrails 1 and 6 are " +
            "both defined over"),

        // ── M2-10 ───────────────────────────────────────────────────────────────────────────
        //
        // 🔒 Tracked SEPARATELY from Domain.CombatRulesNamespace, for that entry's own stated reason:
        // StatusCatalogueRuleTests keys on "SlayIdleRepeat.Core.Rules.Combat.Status", which Domain
        // declares no constant for, so the inventory sweep at the foot of
        // Every_rule_subject_is_present_or_declared_pending cannot reach it — that sweep walks
        // Domain.PermittedCoreNamespaces, and the sub-namespaces are not in it.
        //
        // Without this entry: rename Core/Rules/Combat/Status/ and all three rules in that file
        // report success over an empty set, while the CombatRulesNamespace entry above stays
        // satisfied by BattleSimulation and friends (it is a PREFIX match). `05` §5 would then be
        // free to become a switch statement again with nothing going red. The namespace constant is
        // restated in the rule file rather than added to Domain.cs because M1-12 holds that file
        // (steering S12), and that file's own pin keeps the restatement honest.
        new(StatusCatalogueRuleTests.StatusNamespace, SubjectKind.CoreNamespace, "M2-10",
            "StatusCatalogueRuleTests.No_status_id_is_named_in_code_outside_the_catalogue, " +
            "Each_exempted_type_names_only_the_one_status_its_document_rules_on, " +
            "The_rules_subject_set_is_the_one_they_were_written_against"),

        // 🔒 The catalogue, tracked by NAME as well as by namespace — the precedent is EnemyCatalogue
        // and TriggerCatalogue, and the reason is theirs. The count floor in the rule file is stated
        // over the namespace, so moving StatusCatalogue one directory up would leave it satisfied by
        // the cadence, the stun window and the instance types while nothing read
        // content/statuses.json at all, and every behaviour test went on passing over a fixture. It
        // is also the type whose own rows carry `05` §5's twelve-status floor.
        new("StatusCatalogue", SubjectKind.CoreType, "M2-10",
            "the single reader of content/statuses.json — 05 §5's twelve statuses, their types, " +
            "their units, the five stacking rules the section states, FREEZE's literal potency, " +
            "STUN's cap and immunity window and BLEED's 📐 missing-HP term all enter Core through " +
            "it, and StatusCatalogueTests states the twelve-row floor over its own rows"),

        // ── M2-09 ───────────────────────────────────────────────────────────────────────────
        //
        // 🔒 The three names DamageResolutionRuleTests keys on by hard-coded FULL name, and every one
        // of them can go silent through a rename with the whole suite green. Its rules are stated as
        // "no method outside X does Y", so a rename that empties the *subject* side reports success
        // over nothing — which is exactly this file's subject.
        //
        //   MitigationConstants — the rule looks for `get_Flat`/`get_PerLevel` on this type. Rename
        //                         it, or replace the record with two loose doubles on BattlePlan, and
        //                         The_05_4_mitigation_quotient_is_computed_only_in_the_attack_pipeline
        //                         quantifies over nothing while `05` §4's two most important balance
        //                         dials go unwatched. The floor case beside it fires on that, which is
        //                         why the pair exists — but the floor cannot say WHICH name went away.
        //   AttackPipeline      — the exemption arm of both the mitigation rule and the ward rule.
        //                         A vacuous exemption makes a rule stricter rather than silent, so it
        //                         would report the real formula as an offender; that is loud, and the
        //                         entry is here so the diff that renames it also reads why.
        //   WardPool            — Only_the_attack_pipeline_absorbs_damage_with_a_ward looks for calls
        //                         to `WardPool.Absorb`. Rename or inline the pool and the rule is
        //                         green over an empty set with `05` §4.1's four rules — the ceiling,
        //                         the absorption order, the per-source cap and the
        //                         WardBroken-versus-expiry distinction `18` §6's `until: WARD_BROKEN`
        //                         is built on — restated wherever the absorption went.
        new("MitigationConstants", SubjectKind.CoreType, "M2-07",
            "DamageResolutionRuleTests.The_05_4_mitigation_quotient_is_computed_only_in_the_attack_pipeline " +
            "and its floor — 05 §4's two 📐 dials, mirrored in data against tuning/power_model.json"),
        new("AttackPipeline", SubjectKind.CoreType, "M2-09",
            "DamageResolutionRuleTests — the exemption arm of both the mitigation rule and the ward " +
            "absorption rule; 05 §4's ten-step pipeline"),
        new("WardPool", SubjectKind.CoreType, "M2-09",
            "DamageResolutionRuleTests.Only_the_attack_pipeline_absorbs_damage_with_a_ward — 05 §4.1's " +
            "one absorb pool per actor"),

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
        // 🔒 M2-15 (Core/Rules/Combat/) and M2-07 (Core/Rules/Stats/) independently wrote this same
        // entry against the same constant; both are duplicates of this row and their citation
        // (AccessibilityBoundaryTests.Core_internal_layering_holds, in addition to the two below) is
        // merged in here rather than kept as a second row — see the merge note directly above the
        // Live array's Domain.CombatRulesNamespace / Domain.StatsRulesNamespace entries.
        //
        // Placed HERE, directly after CanonicalStateWriter, and not at the end of the array:
        // IMPLEMENTATION_TRACKER.md's carried-forward item 5 sends M1-03's CurrencyChanged entry
        // "beside the CurrencyId row already there", and CurrencyId is the array's LAST entry. Two
        // agents appending to the same tail is the conflict, not the fix.
        new(Domain.RulesNamespace, SubjectKind.CoreNamespace, "M1-10",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal, AccessibilityBoundaryTests." +
            "Core_internal_layering_holds, IsolationTests.Entitlements_are_" +
            "unreachable_from_the_rules_and_the_power_computation"),

        // 🔒 M2-08's five. Moved out of Pending rather than deleted, on this file's own doctrine —
        // and the count floor in PublicRuleTypeFloorTests is NOT a substitute for them.
        //
        // That floor is `resolved >= 5` over a six-name list. Today it has zero headroom and a rename
        // does fail it. The moment M2-07's PowerCalculator lands, resolved becomes 6 — and renaming
        // CombatSimulator (folding the entry point into GameRules, say) would leave resolved at 5,
        // every rule green, Domain.PublicRuleTypes carrying a dead name, and `30` §11.2's public entry
        // point silently gone. A count cannot pin a name; only a name can.
        //
        // All five are the enumerated signature closure R16 authorised, so each is load-bearing for
        // the exemption arm of Handlers_and_Rules_are_internal.
        new("CombatSimulator", SubjectKind.CoreType, "M2-08",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal — the exemption arm; " +
            "Domain.PublicRuleTypes names it and it is 30 §11.2's public entry point"),
        new("SimulationResult", SubjectKind.CoreType, "M2-08",
            "Domain.PublicRuleTypes — Simulate's return type, public by consequence (R15)"),
        new("CombatEvent", SubjectKind.CoreType, "M2-08",
            "Domain.PublicRuleTypes — reached through SimulationResult.Log (R15)"),
        new("CombatEventType", SubjectKind.CoreType, "M2-08",
            "Domain.PublicRuleTypes — reached through CombatEvent.Type (R15)"),
        new("ActorStats", SubjectKind.CoreType, "M2-08",
            "Domain.PublicRuleTypes — Simulate's parameter type (R15), and the one type in the " +
            "closure with a public factory, which PublicRuleTypeFloorTests." +
            "Every_public_entry_points_parameters_can_be_built_from_outside_Core requires"),

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
            "its identity floor). Also SlayIdleRepeat.Core.Rules.Effects.Triggers.IRunTriggerCounters — " +
            "M2-04's ON_KILL counter is run-scoped state (18 §3's PK_MIDAS 'every 6th enemy killed'), " +
            "and this aggregate is what has to carry ONE instance of it across battle boundaries; " +
            "rebuilding it per battle resets the counter every fight with no rule catching it"),

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

        // 🔒 Landed closing the gap the M1/M2 merge surfaced. 14 §8.1's COMBAT regime — a
        // server-issued battleSeed, no persisted counter of its own (the seed IS the persisted
        // state, and BattlePlan.RngPosition threads a resumed stream's end position across the
        // pre-battle/in-fight boundary). BattleSimulation and EncounterFight had been constructing
        // DeterministicRng directly since M2, unchecked by this rule until this merge — the rule was
        // never wrong, the two branches simply never ran against each other before now.
        new(Domain.BattleRngScopeType, SubjectKind.CoreType, "M2 merge",
            "DomainPurityTests.DeterministicRng_is_constructed_only_inside_Core_Rng (an identity " +
            "floor — the COMBAT regime's construction site, live from the M1/M2 merge)"),

        // The namespace, moved for the reason Primitives and Rules were moved: every namespace
        // 30 §11.4 enumerates has to appear in one of these two arrays or its layering row governs
        // nothing. 🔒 Core_internal_layering_holds gained a Commands ROW on this commit — see the
        // note there for what it forbids and why the Events half is still open.
        // 🔒 M1-02 filled it: 49 commands plus one internal payload helper, and the M4 retro ruling of
        // 2026-08-17 added three more commands. The two layering rows
        // M1-06 added were LIVE-BUT-THIN over a single abstract base with no members; they now
        // govern 53 types, and the Commands row — read it in the table rather than here, since
        // M1-11 appended Testing to it — was tested for real by the payload decision: every field
        // in the vocabulary is an int, a string, a bool or Primitives.DifficultyTier, and no command
        // names a Model aggregate. M1-06's brief asked to be told if one had to; none does.
        new(Domain.CommandsNamespace, SubjectKind.CoreNamespace, "M1-06",
            "AccessibilityBoundaryTests.Core_internal_layering_holds (the Commands row and the " +
            "mustNotReachTheRoot row, both added in M1-06 and both governing 53 types: M1-02's 49 " +
            "commands, the internal payload helper, and the three the 2026-08-17 ruling added), " +
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
        //    same is CommandVocabularyTests pinning the registry's 52 wire names in both directions.
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
        // 🔒 WHAT THAT ROW DOES NOT REACH — CLOSED IN M1-12. It permits Testing -> Model, and must
        // (30 §11.3's Rehydrate), so a harness calling an aggregate's INTERNAL mutator bypasses Apply
        // exactly as calling a handler would, and no namespace rule can see it: both references go to
        // the same namespace and differ only in the visibility of the member reached. That is now
        // AccessibilityBoundaryTests.The_harness_drives_the_aggregates_through_their_public_seam_only,
        // which is an accessibility rule rather than a layering row for exactly that reason — and
        // this namespace is its subject set, so the row below carries it too.
        new(Domain.InMemoryGameType, SubjectKind.CoreType, "M1-11",
            "DomainPurityTests.The_whole_game_is_playable_from_Core_alone (the rule's NAME is stated " +
            "over this type: its public-harness arm had never run before this commit, and M1-11 " +
            "added the presence arm that makes deleting the harness red rather than green)"),

        new(Domain.TestingNamespace, SubjectKind.CoreNamespace, "M1-11",
            "AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace (the rule " +
            "that has named this namespace since M0-08, through Domain.PermittedCoreNamespaces — " +
            "though it governed an EMPTY region until this commit, and the Pending row this replaces " +
            "cited a different rule that did not key on it at all, which M1-09 flagged), " +
            "AccessibilityBoundaryTests.Core_internal_layering_holds (LIVE from M1-11, when the " +
            "Testing row was added: the harness may not name Rules or Handlers, and nothing beneath " +
            "it — root included — may name the harness), " +
            "AccessibilityBoundaryTests.The_harness_drives_the_aggregates_through_their_public_seam_only " +
            "(M1-12 — this namespace is that rule's whole subject set, and it closes the half no " +
            "layering row can express: Testing -> Model is permitted, but only to PUBLIC members)"),
    };

    // ---------------------------------------------------------------- floors
    //
    // Derived from the tree as it stands on this commit, and stated as FLOORS rather than
    // equalities so that adding an adapter or a Core type is not a test edit. Lowering one
    // is a deliberate decision that belongs in the same commit as the deletion that forces
    // it, with the reason in the message.

    // 🔒 M5-01 raised both of these by exactly the two adapter projects it adds: the shared
    // Adapters.Ambient.System (the real system clock and id generator) and the client
    // Adapters.Cache.LocalFile that `23` §3 already catalogues and nothing had built. They move
    // together because every adapter is also a production project; if a later task raises one
    // without the other, one of the two sets has gained a member the other did not see.
    //
    // ⚠️ The project floor's trailing sum was ALREADY STALE and is corrected rather than shifted:
    // it read "26 under src/ + 3 under tools/", written before the four asset tools landed. The
    // tree holds 26 under src/ and 7 under tools/ today, and 28 + 7 once the two adapters above
    // exist — so 31 is a floor with genuine headroom rather than the count minus nothing, which is
    // what this block's preamble asks for. AdapterFloor stays tight against the tree because a
    // renamed adapter is the specific silence it watches for.
    // 🔒 M7-01b raised both by exactly the one adapter project it adds: the client
    // Adapters.Platform.Host, the real non-engine IPlatformInfoPort that `23` §5 A5 needs beside the
    // fake. Same pairing rule as M5-01's above, and ContractSuiteCoverageTests.AdapterAssemblyFloor
    // — which is stated over the same adapters from the other test assembly — moved with them.
    private const int ProductionProjectFloor = 32;   // src/ + tools/, floored below the 36 the tree holds
    private const int AdapterFloor = 24;
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
    // 🔒 M5-01 raised this from 1 to 5. At 1 the entire `23` §4 catalogue could be deleted down to
    // IContentSourcePort with DependencyRuleTests' two port rules still clearing the floor — and
    // those two rules are the only thing making a port more than a folder convention. The four this
    // task adds are IClockPort, IIdGeneratorPort, ILocalCachePort and IRewardedAdPort; every other
    // port `23` §4 declares is carried by PortCatalogue.Deferred with the task that builds it.
    // 🔒 M7-01b raised this from 5 to 6 for IPlatformInfoPort, the first of `23` §4.1's three
    // platform ports to become declarable — with PortCatalogueTests.DeclaredPortFloor and
    // ContractSuiteCoverageTests.PortFloor, the two other floors over this same set.
    private const int PortFloor = 6;                 // IContentSourcePort (M0-09) + M5-01's four + M7-01b's one
    private const int TypeConstantFloor = 10;        // Domain's *Type / *Event const fields

    // 🔒 M1-12. The constants whose register row carries a citation THIS assembly can resolve, and
    // therefore the rows whose citation the truth arm actually checks: one per register row, with
    // IClockPort excepted (30 §3 makes it the name that must never appear, so it is correctly in
    // neither register). Deliberately NOT restated as "N of M constants" — M1-12's own review caught
    // that transcription wrong by one on the commit that corrected two other stale counts, which is
    // the S4 known limit landing inside the sentence describing it. Floored so that a row rewritten
    // into prose this assembly cannot resolve is a build failure rather than a quiet exemption (S3).
    private const int CitedConstantRowFloor = 9;

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

    // EffectEvaluationPurityRuleTests (M2-06) is stated over the types under Rules/Effects/Values/,
    // Rules/Effects/Duration/ and Rules/Effects/Stacking/ — three namespace FILTERS, the same shape
    // this file exists to watch, and all three are one folder rename away from empty. There were 13
    // types on the commit the rules landed (2 evaluators + ValueModeSubjects; DurationEvaluator,
    // DurationScopes, EffectApplication, DurationProbe, DurationOutcome, WardPoolEvent,
    // DurationEndReason; EffectStackSet, StackApplication). The floor is 3 — one reachable type per
    // namespace — because the claim being made is that all three filters still reach something, not
    // that any of them holds a particular number of helpers. The four evaluator NAMES in Live above
    // are what pins which types those are.
    private const int EffectEvaluationTypeFloor = 3;

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

        // 🔒 M1 REVIEW. Contracts_never_redeclares_a_domain_type is the ONLY thing enforcing 30
        // §11.6's one-vocabulary rule, and it quantified over Contracts' single type with no floor:
        // emptying the project, or renaming its namespace, makes that rule green over zero.
        Floor(offenders, "types in SlayIdleRepeat.Contracts",
            Il.AllTypes(ProductionAssemblies.Module(ProductionAssemblies.ContractsName))
              .Count(t => !Domain.IsCompilerGenerated(t)),
            1,
            "Contracts_never_redeclares_a_domain_type is stated over this set. Empty, it is the one " +
            "guard against a parallel wire hierarchy passing over nothing (30 §11.6).");

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

        Floor(offenders, "types under the three 18 §1.1/§2.2/§6 evaluation namespaces",
            EffectEvaluationPurityRuleTests.SubjectCount, EffectEvaluationTypeFloor,
            "EffectEvaluationPurityRuleTests' two rules — An_effect_evaluator_never_draws_and_never_reads_a_clock " +
            "and An_effect_evaluator_holds_no_mutable_static_state — are stated over them. An empty set means " +
            "Values/, Duration/ or Stacking/ has been renamed or folded away, and nothing is stopping the next " +
            "edit from memoising a step count that 18 §1.1 requires re-read at every resolution pass.");

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
            // 🔒 A disclaimer withdraws every citation after it, from both arms. Reported rather
            // than applied silently: a row losing checked citations to a stray "NOT" is the same
            // silence this rule exists to break. Naming one rule as NOT keying on the subject is
            // legitimate — the GhostSnapshot row does exactly that — so this is an offender only
            // when the tail still carries a citation the arms would otherwise have checked.
            var discarded = AffirmativeAndDiscarded(subject.UsedBy).Discarded;

            offenders.AddRange(
                CitedRules(discarded)
                    .Skip(1)
                    .Select(lost =>
                        $"'{subject.Name}' names {lost} AFTER a 'NOT' disclaimer, so it is read as commentary " +
                        "and neither arm checks it. A disclaimer withdraws everything that follows — put the " +
                        "citations this row is CLAIMING before it, and keep the disclaimer last."));

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
        // ⚠️ Grouped, not ToDictionary. A subject COPIED into Live instead of MOVED out of Pending —
        // precisely the mistake this file polices — would otherwise kill this rule with
        // "An item with the same key has already been added" instead of reporting an offender, and a
        // rule that throws where it should fail is a rule whose message nobody reads.
        var rows = Pending.Concat(Live)
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        offenders.AddRange(
            rows.Where(r => r.Value.Length > 1)
                .Select(r => $"'{r.Key}' appears {r.Value.Length} times across Pending and Live. A subject that " +
                             "has arrived is MOVED, never copied — two rows mean one of them is describing a " +
                             "state the repository is not in."));

        var checkedRows = 0;

        foreach (var (constant, value) in constants)
        {
            // IClockPort is deliberately in neither register (30 §3: it must never exist), so the
            // lookup below already skips it. Not spelled as a second condition, because a reader
            // would go looking for the case it handles.
            if (!rows.TryGetValue(value, out var matches))
            {
                continue;
            }

            var row = matches[0];

            var citedTypes = CitedRules(row.UsedBy)
                .Select(c => c.Split('.')[0])
                .ToArray();

            if (citedTypes.Length == 0)
            {
                continue;
            }

            checkedRows++;

            var readerTypes = ReaderTypesOf(value);

            // 🔒 EVERY cited RULE class must read it, not merely one of them. An `Any` overlap lets a
            // row accumulate false citations indefinitely so long as one is right — which is the same
            // silence this rule exists to break, one indirection out. Non-`Tests` citations
            // (GapRegister) are held to the weaker bar in the check below, because a register is a
            // container of names rather than a rule with a subject set.
            var falseRuleCitations = citedTypes
                .Where(t => t.EndsWith("Tests", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Where(t => !readerTypes.Contains(t, StringComparer.Ordinal))
                .ToArray();

            offenders.AddRange(
                falseRuleCitations.Select(t =>
                    $"Domain.{constant} = '{value}' cites {t}, which does not read it — the name is read by " +
                    $"[{string.Join(", ", readerTypes.OrderBy(r => r, StringComparer.Ordinal))}]. Renaming the " +
                    "constant would leave that rule working exactly as before, so the row promises a " +
                    "consequence that would not happen. Cite the mechanism that reads it, or say plainly that " +
                    "the rule does NOT key on it (everything after the first 'NOT ' is read as commentary)."));

            if (!citedTypes.Any(t => readerTypes.Contains(t, StringComparer.Ordinal)))
            {
                offenders.Add(
                    $"Domain.{constant} = '{value}' is cited by [{string.Join(", ", citedTypes.Distinct(StringComparer.Ordinal))}] " +
                    $"but is READ by [{string.Join(", ", readerTypes.OrderBy(t => t, StringComparer.Ordinal))}] — no " +
                    "overlap at all. Nothing the row names would notice this constant being renamed.");
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
        Assert.True(
            ReadersOf(Domain.InMemoryGameType).Length > 0,
            "the ldstr scan finds nothing for a name the suite demonstrably reads — " +
            "The_whole_game_is_playable_from_Core_alone keys on it. If this is empty the reader arm " +
            "is reporting every constant unread, or (worse, once someone 'fixes' that) reporting " +
            "nothing at all.");

        // 🔒 The negatives are CASE VARIANTS of real names, not invented ones. An invented string can
        // only fail if the lookup returned a wildcard, which nobody will ever write; the actual
        // fragile assumption in both lookups is the StringComparer.Ordinal, and only a case variant
        // fails the moment either is loosened to IgnoreCase.
        Assert.False(
            ReadersOf(Domain.InMemoryGameType.ToLowerInvariant()).Length > 0,
            "the reader scan matched a lower-cased 'inmemorygame'. The comparison is Ordinal on " +
            "purpose: a case-insensitive one would let a renamed-only-in-case constant read as still " +
            "keyed on, which is the silence this rule exists to break.");

        var facts = SuiteFactNames();

        Assert.Contains(
            nameof(AccessibilityBoundaryTests) + "." + nameof(AccessibilityBoundaryTests.Core_internal_layering_holds),
            facts);

        Assert.DoesNotContain(
            "accessibilityboundarytests.core_internal_layering_holds",
            facts);

        // The citation PARSER, separately: a row naming no rule must yield nothing, or the arm above
        // quantifies over an empty set on every row and its failure mode is silence.
        Assert.Empty(CitedRules("the 14 §16.6 field-order pin in SlayIdleRepeat.Core.Tests"));

        Assert.Equal(
            new[] { "DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged" },
            CitedRules("DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged (LIVE since M1-04)"));

        // 🔒 And the disclaimer half, which is what the GhostSnapshot row needs and what a bare regex
        // over prose gets wrong: a name introduced as the rule that does NOT read the constant must
        // not come back as an affirmative citation.
        Assert.Equal(
            new[] { "GapRegisterTests.The_stale_check_is_silent_while_the_type_it_waits_for_is_absent" },
            CitedRules(
                "GapRegisterTests.The_stale_check_is_silent_while_the_type_it_waits_for_is_absent " +
                "⚠️ NOT IsolationTests.GuildView_is_a_read_only_projection, which selects by name"));
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
          // 🔒 StartsWith, not Equals — M1 REVIEW. A constant used in a compile-time concatenation is
          // FOLDED into the result: IsolationTests.IsGuildType writes
          // `Domain.GuildModelNamespace + "."`, and the compiler emits one ldstr of
          // "SlayIdleRepeat.Core.Model.Guild." — the constant's own value never appears in the IL at
          // all. Under Equals this reader was invisible and the rule reported a constant read by
          // nothing, which is the same const-inlining blind spot that let 62 of 63 rules pass while
          // Core referenced Contracts. A prefix match sees the folded form; the value is a
          // fully-qualified name, so a false positive would need a DIFFERENT name that starts with
          // this one, which the namespace tree makes a real reader anyway.
          .Where(m => Il.Instructions(m).Any(i =>
              i.OpCode == OpCodes.Ldstr &&
              (i.Operand as string)?.StartsWith(value, StringComparison.Ordinal) == true));

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

        foreach (Match match in Regex.Matches(Affirmative(usedBy), @"\b([A-Z]\w*)\.([A-Za-z_]\w*)\b"))
        {
            if (suiteTypes.Contains(match.Groups[1].Value))
            {
                yield return $"{match.Groups[1].Value}.{match.Groups[2].Value}";
            }
        }
    }

    /// <summary>
    /// 🔒 The part of a register row that <b>claims</b> something, with the disclaimer removed.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The GhostSnapshot row is why this exists, and it was found by review rather than by the
    /// rule.</b> M1-12 rewrote that row to record which rule does <em>not</em> read the constant —
    /// <c>"⚠️ NOT IsolationTests.Guild_state_is_unreachable_from_the_ghost_snapshot"</c> — and a bare
    /// regex over the prose then extracted that very name as an affirmative citation, which the truth
    /// arm accepted. So the one row in the repository documenting a false citation had reintroduced
    /// it in a form the mechanism reported as true. Everything from the first <c>NOT</c> onward is
    /// therefore commentary, not a claim.
    /// </remarks>
    private static string Affirmative(string usedBy) => AffirmativeAndDiscarded(usedBy).Affirmative;

    /// <summary>
    /// A row's prose split into the part that <b>claims</b> and the part a <c>NOT</c> disclaimer
    /// withdraws, so the discarded tail can be reported rather than silently dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <c>\bNOT\s</c>, not <c>IndexOf("NOT ")</c> — the substring also matches inside
    /// <c>CANNOT</c>, and in a file written like this one that word is likely.
    /// </para>
    /// <para>
    /// 🔴 <b>And the truncation is reported, because otherwise it is a silent kill-switch on the
    /// mechanism it serves.</b> Everything after the disclaimer is dropped from BOTH arms, so a row
    /// whose prose grew an emphatic <c>NOT</c> mid-sentence would quietly stop having its later
    /// citations checked — the exact S4 shape this rule exists to break, one indirection out. The
    /// <c>CitedConstantRowFloor</c> only notices a row falling to ZERO citations, not one falling
    /// from three to one. So a row that discards citations says so.
    /// </para>
    /// </remarks>
    private static (string Affirmative, string Discarded) AffirmativeAndDiscarded(string usedBy)
    {
        var disclaimer = Regex.Match(usedBy, @"\bNOT\s");

        return disclaimer.Success
            ? (usedBy[..disclaimer.Index], usedBy[disclaimer.Index..])
            : (usedBy, string.Empty);
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
            // 🔒 M1 REVIEW — "Namespace" joined "Type" and "Event". The filter covered only the two
            // type suffixes, so THREE namespace constants sat outside every inventory:
            // GuildModelNamespace, CombatRulesNamespace and StatsRulesNamespace. The last was read
            // by NOTHING AT ALL — the precise shape the read-by-a-rule arm below was written to
            // catch (Domain.GameContextType, deleted at wave 2 only because a human read the file),
            // and it could not catch it because it too was scoped to this filter.
            //
            // PermittedCoreNamespaces and PortsNamespace are excluded: the former is covered
            // wholesale by the untracked-namespace arm, and the latter names an Application
            // namespace no Core lookup can find.
            .Where(f => f.Name.EndsWith("Type", StringComparison.Ordinal) ||
                        f.Name.EndsWith("Event", StringComparison.Ordinal) ||
                        (f.Name.EndsWith("Namespace", StringComparison.Ordinal) &&
                         !Domain.PermittedCoreNamespaces.Contains((string)f.GetRawConstantValue()!) &&
                         f.Name != nameof(Domain.PortsNamespace)))
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
