using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// The three effects every boss carries and no boss script authors: the universal enrage and its
/// phase-3 STUN/FREEZE immunity, attached as data by the encounter builder rather than per-boss
/// content or engine branches.
/// </summary>
/// <remarks>
/// <para>
/// The 3 s stun-immunity window in phases 1-2 is <em>not</em> here — it is a universal STUN rule
/// that applies to every actor, not just bosses, and lives on <c>IStatusTimeline</c>.
/// </para>
/// <para>
/// The builder mints every instance id itself rather than letting <c>RegisterHoldings</c> mint an
/// ordinal, because the phase controller needs to know which phase an instance belongs to in order
/// to de-anchor phases 2 and 3 on entry:
/// </para>
/// <list type="bullet">
///   <item>a phase mechanic → <see cref="PhaseInstance"/>, <c>{bossId}#P{phase}#{effectId}</c></item>
///   <item>a built-in → <see cref="BuiltInInstance"/>, <c>{bossId}#{effectId}</c></item>
/// </list>
/// <para>
/// Anything not in the phase map is never touched by a phase transition — <c>SYS_ENRAGE</c> must
/// never be touched.
/// </para>
/// </remarks>
internal static class BossBuiltIns
{
    /// <summary>The universal 70 s enrage's effect id.</summary>
    internal const string EnrageId = "SYS_ENRAGE";

    /// <summary>The phase-3 STUN immunity's effect id.</summary>
    internal const string Phase3StunImmunityId = "SYS_PHASE3_IMMUNE_STUN";

    /// <summary>The phase-3 FREEZE immunity's effect id.</summary>
    internal const string Phase3FreezeImmunityId = "SYS_PHASE3_IMMUNE_FREEZE";

    /// <summary>An alias of <see cref="StatusIds.Stun"/>.</summary>
    internal const string StunStatusId = StatusIds.Stun;

    /// <summary>An alias of <see cref="StatusIds.Freeze"/>.</summary>
    internal const string FreezeStatusId = StatusIds.Freeze;

    /// <summary><c>SYS_ENRAGE</c>'s tick interval.</summary>
    internal const double EnrageIntervalSeconds = 1.0;

    /// <summary>Delay from battle start before the hard enrage kicks in.</summary>
    internal const double EnrageStartDelaySeconds = 70.0;

    /// <summary>
    /// +8% ATK per second, compounding, as <c>STAT_MULT</c>'s value.
    /// </summary>
    /// <remarks>
    /// The value <em>is</em> the multiplier, so three seconds of enrage is <c>1.08^3 = 1.259712</c>,
    /// not <c>2.08^3</c>. <c>SysEnrageStackingTests</c> and <c>StatAggregationTests</c> pin the number.
    /// </remarks>
    internal const double EnrageAtkMultiplier = 1.08;

    /// <summary>
    /// Built-in enrage: <c>STAT_MULT ATK x1.08</c> on a <c>PERIODIC {interval: 1.0, startDelay: 70.0}</c>,
    /// <c>BATTLE</c> scope, uncapped multiplicative stacking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a phase, and must never be reached by a phase transition: a <c>PERIODIC</c> anchors when
    /// its effect becomes active, and this one becomes active at battle start — a phase entry that
    /// deactivated and reactivated it would re-anchor it, so the boss would enrage 70 s after
    /// reaching 66% HP instead of 70 s into the fight.
    /// </para>
    /// <para>
    /// A stack of <c>SYS_ENRAGE</c> deliberately emits no <c>CombatEvent</c> — not modeled as
    /// <c>APPLY_STATUS</c> (would swap effect stacking for status stacking, two different combiners),
    /// and no new <c>CombatEventType</c> member was added for it (would perturb every committed log
    /// reference vector for a requirement nothing states).
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

    /// <summary>Bosses are immune to STUN in phase 3.</summary>
    /// <remarks>
    /// Scoped to <c>PHASE</c> rather than <c>BATTLE</c>: since phase 3 is never exited the two are
    /// equivalent in practice, but <c>PHASE</c> is the literal reading of "in phase 3".
    /// </remarks>
    internal static EffectDefinition Phase3StunImmunity { get; } = Phase3Immunity(
        Phase3StunImmunityId, StunStatusId);

    /// <summary>Bosses are immune to FREEZE in phase 3.</summary>
    internal static EffectDefinition Phase3FreezeImmunity { get; } = Phase3Immunity(
        Phase3FreezeImmunityId, FreezeStatusId);

    /// <summary>
    /// The three built-ins, in the order <see cref="BossEncounterBuilder"/> attaches them.
    /// </summary>
    /// <remarks>
    /// A <see cref="List{T}"/> initialiser rather than <c>[ … ]</c>: a collection expression
    /// synthesises a helper in the global namespace, which fails the namespace-boundary build check.
    /// </remarks>
    internal static IReadOnlyList<EffectDefinition> All { get; } = new List<EffectDefinition>
    {
        Enrage, Phase3StunImmunity, Phase3FreezeImmunity,
    };

    /// <summary>
    /// The instance id of a phase mechanic: <c>{bossId}#P{phase}#{effectId}</c>.
    /// </summary>
    /// <param name="bossId">The boss's actor id.</param>
    /// <param name="phase">The block the mechanic is in, <c>1..3</c>.</param>
    /// <param name="effectId">The mechanic's authored effect id.</param>
    /// <remarks>
    /// The <c>P</c> is load-bearing: without it, an id could be ambiguous between this shape and
    /// <see cref="BuiltInInstance"/>'s, and a phase transition would no longer be able to tell a
    /// phase mechanic from a built-in by looking at the id.
    /// </remarks>
    internal static EffectInstanceId PhaseInstance(string bossId, int phase, string effectId) =>
        EffectInstanceId.Of(
            $"{bossId}#P{phase.ToString(CultureInfo.InvariantCulture)}#{effectId}");

    /// <summary>
    /// The instance id of a built-in: <c>{bossId}#{effectId}</c> — deliberately not carrying a
    /// phase, since nothing outside the phase map is ever touched by a transition.
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
