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

        // The other member of Domain.PublicRuleTypes. Tracked for the same reason its sibling is:
        // the list is a transcription of a 🔒 section, and a rule keyed on it must not be able to
        // go quiet through a rename nobody notices. M2-07 is landing Rules/Stats/ and this is the
        // type that directory exists for (`29` §1, `30` §11.2).
        new("PowerCalculator", SubjectKind.CoreType, "M2-07",
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal — the exemption arm of the rule; " +
            "Domain.PublicRuleTypes names it and nothing else pins that name"),

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

        // 🔒 A DEFERRAL, not a rule subject — recorded here so it expires by itself (steering S4).
        //
        // `18` §3: "ON_KILL counters PERSIST ACROSS BATTLES FOR THE RUN (PK_MIDAS's 'every 6th enemy
        // killed')." M2-04 owns the counter and its read/write seam
        // (Rules.Effects.Triggers.IRunTriggerCounters, with RunTriggerCounters as the in-Core
        // implementation and RunTriggerCountersContract as the shared suite, per steering S7). It
        // does NOT own the persistence: the state has to live on the Run aggregate, which is M1-05
        // in the other, unfinished milestone, and the controller that carries it between battles is
        // M3. M2-04 deliberately built no placeholder run controller.
        //
        // ⚠️ Whoever lands `Run` therefore inherits an obligation: a run must hold ONE
        // RunTriggerCounters for its whole length and hand the same instance to every battle's
        // TriggerRegistry, and its snapshot must carry the pairs. Rebuilding it per battle resets
        // PK_MIDAS every fight — a defect that produces a legal-looking log and is wrong in the only
        // number that matters.
        //
        // This entry is the expiry: Every_rule_subject_is_present_or_declared_pending fails the
        // moment a type named Run exists, which is exactly when the obligation lands.
        new("Run", SubjectKind.CoreType, "M1-05",
            "SlayIdleRepeat.Core.Rules.Effects.Triggers.IRunTriggerCounters — see the note above " +
            "this entry: the ON_KILL counter is run-scoped state and the Run aggregate has to carry " +
            "it across battle boundaries"),

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
        new("LuckService", SubjectKind.CoreType, "M4-01",
            "SlayIdleRepeat.Core.Rules.Combat.Enemies.IEliteModifierHistory — see the note above this " +
            "entry: 05 §6.2's no-repeat rule is run-scoped state that 24 §4.10 B2 owns, the seam only " +
            "works if ONE history instance spans the whole run, and the implementation must live on " +
            "the run-controller side rather than on LuckService itself, or Rules.Luck ends up naming " +
            "Rules.Combat and R17 has no edge for it"),

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
        new("EliteModifierEffects", SubjectKind.CoreType, "M2-08 / M2-13 — the first consumer",
            "05 §6.2's modifier parameters, which have no consumer yet; 18's headnote forbids the " +
            "per-modifier branching that is otherwise the path of least resistance, and the elite-id " +
            "rule in EnemyDerivationRuleTests is blind to an enum switch"),

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

        new("GearItem", SubjectKind.CoreType, "M4-03",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 1 of 10, " +
            "'gear'. Its IEffectSource has no data model until M4-03's gear instance schema lands"),
        new("GearAffix", SubjectKind.CoreType, "M4-03",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 2 of 10, " +
            "'affixes'. 08 §3's 14 affixes are M4-03's"),
        new("SetBonus", SubjectKind.CoreType, "M4-03",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 3 of 10, " +
            "'set bonuses'. 08 §3's 4 SS set-bonus engines are M4-03's"),
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
        new("PerkDefinition", SubjectKind.CoreType, "M3-07",
            "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue — 18 §8 step 1's source 10 of 10, " +
            "'perks (in draft order)'. 06's 98 perks are M3-07's. ⚠️ '(in draft order)' is R5's " +
            "COLLECTION order; the application order is EffectResolutionOrder's, and is total"),

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
            "AccessibilityBoundaryTests.Handlers_and_Rules_are_internal, AccessibilityBoundaryTests." +
            "Core_internal_layering_holds, IsolationTests.Entitlements_are_" +
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
        new("IEliteModifierHistory", SubjectKind.CoreType, "M2-11",
            "EliteModifierHistoryContract — the shared contract suite every implementation of 05 §6.2's " +
            "run-scoped no-repeat memory is run through (steering S7); paired with the LuckService " +
            "entry in Pending, which is where its persistence lands"),
        new("EnemyCatalogue", SubjectKind.CoreType, "M2-11",
            "the single reader of content/enemies/enemies.json — 05 §6's derivation constants, level " +
            "table, archetype rows, on-hit tables, elite modifiers and identities and chapter pools " +
            "all enter Core through it, and EnemiesDataTests asserts the document it names"),

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
        //                         The_05_4_mitigation_quotient_is_computed_in_exactly_one_place
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
            "DamageResolutionRuleTests.The_05_4_mitigation_quotient_is_computed_in_exactly_one_place " +
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

        // Live only because Model.Snapshots sits beneath it — Domain.CoreTypesUnder matches
        // by namespace PREFIX. The aggregates themselves are M1-04's, so the aggregate half
        // of Apply_is_the_only_public_mutation is still quantifying over nothing; what is
        // asserted here is that the prefix reaches something, not that M1 has arrived.
        new(Domain.ModelNamespace, SubjectKind.CoreNamespace, "M0-07",
            "AccessibilityBoundaryTests.Core_internal_layering_holds"),
        new("CanonicalStateWriter", SubjectKind.CoreType, "M0-07",
            "the 14 §16.6 field-order pin in SlayIdleRepeat.Core.Tests"),

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
