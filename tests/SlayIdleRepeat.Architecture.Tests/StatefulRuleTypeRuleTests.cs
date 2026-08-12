using System.Globalization;
using Mono.Cecil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `30` §11.4 — <c>Rules/</c> is <em>"internal, static, stateless calculators"</em>, and the
/// exceptions are an <b>enumerated</b> list rather than a precedent anyone may extend.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this rule exists, stated plainly.</b> The annotation was already false five times over
/// under `Rules/Combat/` alone when M2-09 wrote this rule, and the list has since grown to
/// <see cref="Stateful"/>'s current length as M2-10 and M2-12 landed. `05` §3
/// is a fixed-tick simulation — a 1800-tick loop is an accumulator by construction, HP and a cooldown
/// have to live somewhere, and a ward outlives the hit that failed to break it. Every entry
/// is justified in its own <c>⚠️</c> paragraph, and every one of those paragraphs argued from the
/// previous one's precedent. That is <b>accretion, not a record</b>: nothing in the suite quantified
/// over statelessness at all, so the next one would cost nothing and `30` §11.4 would go
/// on saying the opposite of the code.
/// </para>
/// <para>
/// 🔒 <b>This file is the one statement of the list, and no count is written down anywhere.</b> `30`
/// §11.4's erratum used to restate the five names and went stale twice — M2-10 added three entries
/// and M2-12 a fourth without it moving. A closed list with two copies is not closed, so the document
/// now points here and the numbers below are read off <see cref="Stateful"/> rather than asserted in
/// prose.
/// </para>
/// <para>
/// 🔒 <b>Enumerated, on <c>Domain.PublicRuleTypes</c>' precedent and for its reason.</b> A blanket
/// <em>"per-battle state is fine"</em> rule would let the next one appear without a decision;
/// enumerated, it takes a diff, and the diff is where the question <em>"does this really need to hold
/// state, or is it a calculator that grew a field?"</em> gets asked. Adding a name here is allowed —
/// it is a deliberate edit with a reason in the commit message, which is the whole mechanism.
/// </para>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to M0-08's rule files or to <c>Infrastructure/Domain.cs</c>:
/// M1-12 is in flight on exactly those (steering S12).
/// </para>
/// </remarks>
public sealed class StatefulRuleTypeRuleTests
{
    /// <summary>
    /// 🔒 The <b>closed</b> list of <c>Rules/</c> types that may hold instance state, with what each
    /// one accumulates and why a stateless function could not.
    /// </summary>
    /// <remarks>
    /// Every one of them is per-battle or per-actor, owned by exactly one caller, never shared and
    /// never static — which is what makes them carry none of the properties `30` §11.4's annotation
    /// exists to protect. That sentence, and not a count, is the entry criterion.
    /// </remarks>
    private static readonly IReadOnlyList<string> Stateful = new List<string>
    {
        // M2-15 — `05` §3.1 step 7 appends events "at the moment each state change occurs", across
        // every step of an 1800-tick loop. A stateless builder would take and return the whole log.
        "SlayIdleRepeat.Core.Rules.Combat.CombatLog",

        // M2-08 — the loop itself. `05` §3.1 is a fixed-tick simulation.
        "SlayIdleRepeat.Core.Rules.Combat.BattleSimulation",

        // M2-08 — one actor's HP, cooldown, target priority and aggregated block for the fight.
        "SlayIdleRepeat.Core.Rules.Combat.BattleActor",

        // M2-08 — `18` §2.4's per-actor charges, saves, buckets and multipliers: written by one op
        // and read by a later attack, so they outlive both calls.
        "SlayIdleRepeat.Core.Rules.Combat.CombatFlowState",

        // M2-09 — `05` §4.1's absorb pool. Its segments outlive the hit that failed to break them,
        // and the four rules stated over them (cap, order, per-source cap, WardBroken-versus-expiry)
        // are only checkable because one object owns all four.
        "SlayIdleRepeat.Core.Rules.Combat.WardPool",

        // M2-10 — `05` §5's twelve statuses per actor. Added by the conductor at merge: M2-09 wrote
        // this rule and M2-10 wrote this type in the same wave, so neither could enumerate the other.
        //
        // It is a genuine exception, not a calculator that grew a field. `05` §3.1's cadence rule is
        // stated over accumulated state that no stateless function can hold: one instance per
        // statusId per target, ticking "on the 20th simulation tick after FIRST APPLICATION and every
        // 20 ticks thereafter", where reapplication "never re-anchors the cadence" — so the anchor
        // must outlive every later application. `_immuneUntilSeconds` is the same shape for `05` §5's
        // mandatory 3 s stun-immunity window, which is defined by what happened before it.
        "SlayIdleRepeat.Core.Rules.Combat.Status.ActorStatuses",

        // M2-10 — the per-fight index from actor to its ActorStatuses. Same wave, same reason: the
        // cadence anchors it holds are per actor and per fight, and `05` §3.1 runs slots 1 and 2 over
        // every actor on every tick, so the loop needs one object to ask rather than a lookup it
        // rebuilds 1800 times.
        "SlayIdleRepeat.Core.Rules.Combat.Status.StatusTimeline",

        // M2-10 — `05` §5's STUN rule is *"Max 1.5 s per application, with a 3 s immunity window
        // AFTER"*, and "after" is the whole difficulty: whether an actor may be stunned now is a
        // function of when it was last stunned, which no stateless call can see. `05` §5 calls the
        // immunity mandatory — *"without it, stun-locking becomes the only viable build"* — and
        // M2-10's proof of it was a mutation that took the fight from 1200 actionable ticks to 0.
        "SlayIdleRepeat.Core.Rules.Combat.Status.StunWindow",

        // M2-12 — `05` §3.1's phase check is stated over the phase the boss is CURRENTLY in, not over
        // its HP: *"while currentPhase < PhaseFor(hp) … enter the next phase"*, and *"phases never
        // revert — healing back above a threshold does not re-enter an earlier phase."* Both
        // sentences are about what already happened, so `_phase` accumulates exactly the fact no
        // function of present state can recover: a boss at 70% HP that has been to phase 3 is in
        // phase 3, and one that has not is in phase 1. BossPhaseRules.PhaseFor is the stateless half
        // and is a static calculator; this is the half that remembers, and `18` §6's PHASE scope
        // then reads it through IBossPhases.CurrentPhase on every tick of slot 2.
        //
        // It is also per-fight and owned by one caller: BattleSeams builds one per BattleServices,
        // nothing shares it and nothing static holds it — the same shape as the eight above.
        "SlayIdleRepeat.Core.Rules.Combat.Bosses.BossPhaseController",
    };

    // ⚠️ The other three types under Rules/Combat/Bosses/ that hold instance fields are deliberately
    //    NOT here, and the rule proves each one does not need to be. BossOutcomes holds a `readonly
    //    BattleServices` and BossSummonSource holds a readonly catalogue plus three readonly numbers
    //    — all of them the fight or the encounter the object was BUILT with, which `30` §11.4's own
    //    definition excludes. BossTelegraphs, BossPhaseRules, BossBuiltIns and BossAdds are static.
    //    The telegraph pass in particular was checked rather than assumed: it emits on the tick
    //    `NextFiringTick − leadTicks`, which is a function of the registry reading at that tick, so
    //    "once per firing" needs no remembered set. If that ever stops being true, the next entry
    //    is a diff and this comment is where the question gets asked again.

    // ⚠️ AttackPipeline is deliberately NOT here, and the rule proves it does not need to be: its one
    //    field is a `readonly BattleServices`, which is the fight it belongs to rather than anything
    //    it accumulates. `05` §4's pipeline really is a calculator; it just needs a log and a draw
    //    stream to be a calculator OVER. Every_enumerated_exception_resolves_and_still_holds_state
    //    fails on a name added here that does not hold state, which is what caught the first draft.

    /// <summary>
    /// A floor under the scanned set (steering S3). Every <c>Rules/</c> type is a subject; if the
    /// namespace stops resolving, the rule reports success over nothing.
    /// </summary>
    private const int CombatRulesTypeFloor = 15;

    /// <summary>
    /// 🔒 `30` §11.4 — a type under <c>Rules/Combat/</c> with instance state is on
    /// <see cref="Stateful"/>, or it is a calculator that grew a field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>What counts as state, and what deliberately does not.</b> A non-static, non-<c>readonly</c>
    /// field, or a <c>readonly</c> field of a mutable collection type — the two shapes an accumulator
    /// actually takes. A <c>readonly</c> field holding an immutable value (the context a per-pass
    /// reader is bound to, the services a seam belongs to) is <b>not</b> state: it is a constructor
    /// argument, and `30` §11.4's concern is objects that <em>change</em>, not objects that
    /// <em>remember what they were built with</em>. Records and their positional members are exempt
    /// for the same reason — <c>BattlePlan</c>, <c>ActorPlan</c> and <c>AttackResolution</c> are
    /// values.
    /// </para>
    /// <para>
    /// ⚠️ <b>What it cannot see:</b> a mutable value hidden behind an immutable-looking field type,
    /// and a static mutable field — which is a worse problem and is
    /// <c>EffectEvaluationPurityRuleTests</c>' and <c>ConditionPurityRuleTests</c>' subject, not
    /// this one's.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_stateful_type_under_Rules_Combat_is_on_the_enumerated_list()
    {
        var subjects = Subjects();

        var offenders = new List<string>();

        // S3 — the floor, in the same case as the rule.
        if (subjects.Length < CombatRulesTypeFloor)
        {
            offenders.Add(
                $"only {subjects.Length.ToString(CultureInfo.InvariantCulture)} types were found under " +
                $"{Domain.CombatRulesNamespace}; the floor is " +
                $"{CombatRulesTypeFloor.ToString(CultureInfo.InvariantCulture)}. This rule is quantifying " +
                "over almost nothing.");
        }

        foreach (var type in subjects)
        {
            if (Stateful.Contains(type.FullName, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var field in type.Fields.Where(f => !f.IsStatic && !Domain.IsCompilerGenerated(f)))
            {
                if (!IsAccumulator(field))
                {
                    continue;
                }

                offenders.Add(
                    $"{Il.Describe(field)} is instance state on a Rules/Combat/ type that is not on " +
                    $"{nameof(StatefulRuleTypeRuleTests)}.{nameof(Stateful)}. `30` §11.4 makes " +
                    "Rules/ 'internal, static, stateless calculators'; the exceptions are " +
                    "enumerated so that the next one is a decision in a diff rather than a precedent " +
                    "somebody followed. If this one is right, add it to the list with what it " +
                    "accumulates and why a stateless function could not.");
            }
        }

        ArchRule.Empty(
            offenders,
            "Every stateful type under Rules/Combat/ is one of 30 §11.4's enumerated exceptions.");
    }

    /// <summary>
    /// 🔒 `23` §6 — the floor under the <b>exemption</b> arm (steering S3/S4). Every listed name
    /// resolves, and every listed type still holds the state it was listed for.
    /// </summary>
    /// <remarks>
    /// A vacuous exemption makes the rule above <em>stricter</em> rather than silent, which is loud —
    /// but a listed name that no longer holds state is a <b>satisfied exception that has not
    /// expired</b>, and steering S4 is explicit that those must go. The day one of these is
    /// refactored back into a calculator, this fails and the entry has to be deleted.
    /// </remarks>
    [Fact]
    public void Every_enumerated_exception_resolves_and_still_holds_state()
    {
        var offenders = new List<string>();

        foreach (var name in Stateful)
        {
            var type = Domain.CoreTypes.FirstOrDefault(
                t => t.FullName.Equals(name, StringComparison.Ordinal));

            if (type is null)
            {
                offenders.Add(
                    $"'{name}' is enumerated as a stateful Rules/ type but resolves to nothing. It has " +
                    "been renamed, moved or deleted; the entry now exempts a type that does not exist " +
                    "while the real one is governed by the rule above.");

                continue;
            }

            if (!type.Fields.Any(f => !f.IsStatic && !Domain.IsCompilerGenerated(f) && IsAccumulator(f)))
            {
                offenders.Add(
                    $"'{name}' is enumerated as a stateful Rules/ type but holds no instance state. " +
                    "The exception is satisfied and a satisfied exception that stays is a stale one " +
                    "(steering S4) — delete the entry.");
            }
        }

        ArchRule.Empty(
            offenders,
            "The enumerated exceptions to 30 §11.4 all resolve and all still need to be exceptions (23 §6).");
    }

    /// <summary>
    /// Every type this rule governs — the top-level, non-enum, non-record types under
    /// <c>Rules/Combat/</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Scoped to <c>Rules.Combat</c>, deliberately, and the narrowing is the honest one.</b>
    /// `30` §11.4's annotation covers the whole of <c>Rules/</c>, and there are stateful types outside
    /// this namespace too — <c>TriggerInstance</c>'s R8 schedule, <c>RunTriggerCounters</c>' run-scoped
    /// counts, <c>EffectStackSet</c>, <c>EffectSourceSet</c>, <c>EliteModifierHistory</c> — each with
    /// its own justification written by the task that landed it. Enumerating those here would be
    /// M2-09 taking an inventory of four other tasks' decisions, which is not a review it is in a
    /// position to do. What M2-09 <em>is</em> in a position to say is that the accretion the code
    /// review found is in <b>this</b> namespace: five of the six ⚠️ paragraphs are here, each citing
    /// the previous one. Widening the scope is a milestone-review job and is recorded as such.
    /// </para>
    /// <para>
    /// ⚠️ <b>Enums are excluded</b> — every enum carries an instance field named <c>value__</c>, which
    /// is the storage for its own value and not state anybody wrote. <b>Records are excluded</b>
    /// because their positional members are values: <c>BattlePlan</c>, <c>ActorPlan</c>,
    /// <c>CombatRules</c> and <c>BattleSeams</c> are inputs to a fight, not accumulators.
    /// </para>
    /// </remarks>
    private static Mono.Cecil.TypeDefinition[] Subjects() =>
        Domain.CoreTypesUnder(Domain.CombatRulesNamespace)
            .Where(t => !Domain.IsCompilerGenerated(t))
            .Where(t => t.DeclaringType is null)
            .Where(t => !t.IsEnum && !IsRecord(t))
            .ToArray();

    /// <summary>A C# <c>record</c> or <c>record struct</c> — Roslyn marks both with a clone member.</summary>
    private static bool IsRecord(Mono.Cecil.TypeDefinition type) =>
        type.Methods.Any(m => m.Name.Equals("<Clone>$", StringComparison.Ordinal)) ||
        type.Methods.Any(m =>
            m.Name.Equals("op_Equality", StringComparison.Ordinal) &&
            type.Methods.Any(p => p.Name.Equals("PrintMembers", StringComparison.Ordinal)));

    /// <summary>
    /// The field is an accumulator: mutable, or a <c>readonly</c> handle on a mutable collection.
    /// </summary>
    private static bool IsAccumulator(FieldDefinition field) =>
        !field.IsInitOnly || IsMutableCollection(field.FieldType);

    /// <summary>A BCL collection type whose contents can change behind a <c>readonly</c> field.</summary>
    private static bool IsMutableCollection(TypeReference type)
    {
        var name = type.FullName;

        return name.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal) ||
               name.StartsWith("System.Collections.Generic.Dictionary`2", StringComparison.Ordinal) ||
               name.StartsWith("System.Collections.Generic.HashSet`1", StringComparison.Ordinal) ||
               name.StartsWith("System.Collections.Generic.Queue`1", StringComparison.Ordinal) ||
               name.StartsWith("System.Collections.Generic.Stack`1", StringComparison.Ordinal);
    }
}
