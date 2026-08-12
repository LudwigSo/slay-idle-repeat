using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 The three effects <b>every</b> boss carries and <b>no</b> boss script authors — `17` §1's
/// universal enrage and its phase-3 <c>STUN</c>/<c>FREEZE</c> immunity — plus the instance-id
/// spelling that makes a boss's phase map derivable and greppable.
/// </summary>
/// <remarks>
/// <para>
/// `17` §11 lists <em>"universal 70 s enrage implemented once, applied to all bosses"</em> as an
/// implementation deliverable, and `17` §1 states the immunity in the same register:
/// <em>"Bosses are immune to <c>STUN</c> and <c>FREEZE</c> in phase 3."</em> Both are therefore
/// <b>data attached by the encounter builder</b>, not per-boss authoring and not engine branches.
/// </para>
/// <para>
/// ⚠️ <b>The 3 s stun-immunity window in phases 1–2 is <em>not</em> here.</b> `17` §1's second
/// sentence — <em>"All bosses respect the 3 s stun-immunity window in phases 1–2"</em> — is `05`
/// §5's universal <c>STUN</c> rule (<em>"max 1.5 s per application, with a 3 s immunity window
/// after"</em>) restated, and it applies to every actor in the game, not only to bosses. It is
/// <b>M2-10's</b>, on <c>IStatusTimeline</c>. Recorded here rather than implemented, so that whoever
/// reads `17` §1 looking for it finds the pointer (steering S6).
/// </para>
///
/// <para>
/// ═══ 🔒 <b>THE INSTANCE-ID SPELLING, AND WHY THE BUILDER MINTS IT</b> ═══
/// </para>
/// <para>
/// <c>BattleSimulation.RegisterHoldings</c> mints <c>{actorId}#{effectId}#{n}</c> for a
/// <see cref="HeldEffect"/> that carries no id. A boss cannot take that: the phase controller has to
/// know <em>which phase</em> an instance belongs to in order to de-anchor phases 2 and 3 at pre-tick
/// 0c and to end the exiting phase's `18` §6 <c>PHASE</c> scope at each entry, and a minted ordinal
/// says nothing about phases. So the builder assigns every instance id itself:
/// </para>
/// <list type="bullet">
///   <item>a phase mechanic → <see cref="PhaseInstance"/>, <c>{bossId}#P{phase}#{effectId}</c></item>
///   <item>a built-in → <see cref="BuiltInInstance"/>, <c>{bossId}#{effectId}</c></item>
/// </list>
/// <para>
/// 🔒 The consequence is the point: anything <b>not</b> in the phase map is never touched by a phase
/// transition, and <c>SYS_ENRAGE</c> is the instance that must never be touched.
/// </para>
/// </remarks>
internal static class BossBuiltIns
{
    /// <summary>🔒 `05` §3.1 / `17` §1 — the universal 70 s enrage's effect id.</summary>
    internal const string EnrageId = "SYS_ENRAGE";

    /// <summary>🔒 `17` §1 — the phase-3 <c>STUN</c> immunity's effect id.</summary>
    internal const string Phase3StunImmunityId = "SYS_PHASE3_IMMUNE_STUN";

    /// <summary>🔒 `17` §1 — the phase-3 <c>FREEZE</c> immunity's effect id.</summary>
    internal const string Phase3FreezeImmunityId = "SYS_PHASE3_IMMUNE_FREEZE";

    /// <summary>`05` §5's <c>STUN</c>.</summary>
    internal const string StunStatusId = "STUN";

    /// <summary>`05` §5's <c>FREEZE</c>.</summary>
    internal const string FreezeStatusId = "FREEZE";

    /// <summary>🔒 `05` §3.1 — <c>SYS_ENRAGE</c>'s <c>interval</c>: <em>"per second"</em>.</summary>
    internal const double EnrageIntervalSeconds = 1.0;

    /// <summary>🔒 `17` §1 — <em>"a hard enrage at 70 s"</em>, as <c>startDelay</c> from battle start.</summary>
    internal const double EnrageStartDelaySeconds = 70.0;

    /// <summary>
    /// 🔒 `17` §1 — <em>"+8% ATK per second, compounding"</em>, as <c>STAT_MULT</c>'s value.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Under R1 the value <em>is</em> the multiplier</b>, so three seconds of enrage is
    /// <c>1.08³ = 1.259712</c> and not <c>2.08³ = 8.998912</c>. `05` §1.1's literal
    /// <c>Π(1 + value)</c> reading is an erratum; `18` §8 step 7's <em>"product"</em> is right.
    /// <c>SysEnrageStackingTests</c> and <c>StatAggregationTests</c> already pin the number.
    /// </remarks>
    internal const double EnrageAtkMultiplier = 1.08;

    /// <summary>
    /// 🔒 `05` §3.1 / `17` §1's built-in enrage: <c>STAT_MULT ATK ×1.08</c> on a
    /// <c>PERIODIC {interval: 1.0, startDelay: 70.0}</c>, <c>BATTLE</c> scope, <b>uncapped
    /// multiplicative</b> stacking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>It is not a phase and must never be reached by a phase transition.</b> R8 anchors a
    /// <c>PERIODIC</c> when its effect becomes active, and this one becomes active at pre-tick 0a
    /// like any other plan effect — which is what makes <c>startDelay: 70.0</c> mean <em>70 seconds
    /// of battle</em>. A phase entry that deactivated and reactivated it would re-anchor it, and the
    /// boss would enrage 70 s after reaching 66% HP.
    /// </para>
    /// <para>
    /// 🔒 <b>No Sporequeen exemption.</b> `17` §11 requires it <em>"implemented once, applied to all
    /// bosses"</em>, and `17` §8's note that Rot's ~66 s soft timer and the 70 s enrage
    /// <em>"must not interact confusingly"</em> asks for a <b>verification</b>, which is the balance
    /// harness's (M2-16a) — not an engine change. A per-boss opt-out used by one of eight is the
    /// bespoke boss code `18` exists to prevent.
    /// </para>
    /// <para>
    /// 🔴 <b>A STACK OF <c>SYS_ENRAGE</c> EMITS NO <c>CombatEvent</c>, AND THAT IS A DECISION.</b>
    /// The hole is left absent and greppable rather than filled (steering S6). What was rejected, and
    /// what reversing it would cost:
    /// </para>
    /// <list type="number">
    ///   <item><b>Rejected: express it as <c>APPLY_STATUS</c></b> so that <c>StatusApplied</c> carries
    ///   the stack count. That would contradict `05` §3.1's 🔒 <c>STAT_MULT</c> and would silently
    ///   swap `18` §6's effect stacking for `05` §5's status stacking — two different combiners, one
    ///   of which is already pinned at <c>1.259712</c>.</item>
    ///   <item><b>Rejected: a new <c>CombatEventType</c> member.</b> The ordinal is inside
    ///   <c>LogHash</c> (`14` §16.6's enum rule), so an eighteenth member would perturb every
    ///   committed reference vector, the `11` §6 tamper comparison and `14` §8.2's cross-platform
    ///   gate — for a requirement no document states. `17` §11 asks for <em>phase transition</em> and
    ///   <em>telegraph</em> events; `17` §1's telegraph rule reaches damaging <b>mechanics</b>, and a
    ///   stat buff is not one.</item>
    /// </list>
    /// <para>
    /// ⚠️ Volume was <b>not</b> the deciding argument and is recorded so nobody re-derives it: the
    /// worst case is 20 events (enrage at 70 s, PvE timeout at 90 s) and the authored 35–60 s band
    /// produces <b>zero</b>. The absence of a requirement decided it. <b>Cost of reversing:</b> one
    /// appended <c>CombatEventType</c> member, one emit in the enrage's firing path, and a
    /// regeneration of <c>CombatLogReferenceVectors.json</c> — no engine restructuring.
    /// </para>
    /// </remarks>
    internal static EffectDefinition Enrage { get; } = new()
    {
        Id = EnrageId,
        Op = EffectOp.STAT_MULT,
        Stat = StatSelector.Of(StatId.ATK),
        Value = EnrageAtkMultiplier,
        Target = EffectTarget.SELF,
        Trigger = new EffectTrigger
        {
            Kind = TriggerKind.PERIODIC,
            Interval = EnrageIntervalSeconds,
            StartDelay = EnrageStartDelaySeconds,
        },
        Duration = new EffectDuration { Scope = DurationScope.BATTLE },
        Stacking = new EffectStacking { Mode = StackingMode.MULTIPLICATIVE },
    };

    /// <summary>🔒 `17` §1 — <em>"Bosses are immune to <c>STUN</c> … in phase 3."</em></summary>
    /// <remarks>
    /// <para>
    /// Expressed as data on an <c>ON_PHASE_ENTER {phase: 3}</c> trigger, which is what makes it
    /// universal and implementable once: the phase controller already evaluates that occurrence, so
    /// no boss authors it and no engine branch tests for phase 3.
    /// </para>
    /// <para>
    /// ⚠️ <b>Assumption recorded: the scope is <c>PHASE</c> and no document states which.</b> `18`
    /// §2.3 gives <c>IMMUNE_STATUS</c> a duration and `17` §1 says only <em>"in phase 3"</em>.
    /// <c>PHASE</c> is the only one of `18` §6's six scopes that means <em>while in this phase</em>,
    /// and because phase 3 is never exited (phases never revert, `05` §3.1) it is equivalent to
    /// <c>BATTLE</c> in practice — so the choice is safe as well as literal.
    /// </para>
    /// <para>
    /// It routes through <c>IStatusEngine.GrantImmunity</c>, which is <b>M2-10's</b> and is
    /// <c>UnwiredStatusEngine</c> today.
    /// </para>
    /// </remarks>
    internal static EffectDefinition Phase3StunImmunity { get; } = Phase3Immunity(
        Phase3StunImmunityId, StunStatusId);

    /// <summary>🔒 `17` §1 — <em>"Bosses are immune to … <c>FREEZE</c> in phase 3."</em></summary>
    internal static EffectDefinition Phase3FreezeImmunity { get; } = Phase3Immunity(
        Phase3FreezeImmunityId, FreezeStatusId);

    /// <summary>
    /// The three built-ins, in the order <see cref="BossEncounterBuilder"/> attaches them.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <see cref="List{T}"/> initialiser rather than <c>[ … ]</c>, on
    /// <c>EnemyCatalogue.FixedStats</c>' precedent: a collection expression synthesises a helper in
    /// the <b>global</b> namespace, which <c>Every_Core_type_lives_under_a_documented_namespace</c>
    /// fails the build on.
    /// </remarks>
    internal static IReadOnlyList<EffectDefinition> All { get; } = new List<EffectDefinition>
    {
        Enrage, Phase3StunImmunity, Phase3FreezeImmunity,
    };

    /// <summary>
    /// 🔒 The instance id of a <b>phase mechanic</b>: <c>{bossId}#P{phase}#{effectId}</c>.
    /// </summary>
    /// <param name="bossId">The boss's actor id.</param>
    /// <param name="phase">The block the mechanic is in, <c>1..3</c>.</param>
    /// <param name="effectId">The mechanic's authored `18` §8 effect id.</param>
    /// <remarks>
    /// 🔒 The <c>P</c> is load-bearing, not decoration: <see cref="BuiltInInstance"/> spells
    /// <c>{bossId}#{effectId}</c>, so without it a boss whose effect id began with a digit would be
    /// ambiguous between the two shapes — and the whole point of the spelling is that a phase
    /// transition can tell a phase mechanic from a built-in by looking at the id.
    /// </remarks>
    internal static EffectInstanceId PhaseInstance(string bossId, int phase, string effectId) =>
        EffectInstanceId.Of(
            $"{bossId}#P{phase.ToString(CultureInfo.InvariantCulture)}#{effectId}");

    /// <summary>
    /// 🔒 The instance id of a <b>built-in</b>: <c>{bossId}#{effectId}</c> — deliberately <em>not</em>
    /// carrying a phase, because nothing outside the phase map is ever touched by a transition.
    /// </summary>
    /// <param name="bossId">The boss's actor id.</param>
    /// <param name="effectId">One of the three ids on this class.</param>
    internal static EffectInstanceId BuiltInInstance(string bossId, string effectId) =>
        EffectInstanceId.Of($"{bossId}#{effectId}");

    private static EffectDefinition Phase3Immunity(string effectId, string statusId) => new()
    {
        Id = effectId,
        Op = EffectOp.IMMUNE_STATUS,
        StatusId = statusId,
        Target = EffectTarget.SELF,
        Trigger = new EffectTrigger
        {
            Kind = TriggerKind.ON_PHASE_ENTER,
            Phase = BossPhaseRules.FinalPhase,
        },
        Duration = new EffectDuration { Scope = DurationScope.PHASE },
    };
}
