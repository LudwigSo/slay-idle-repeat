namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>One entry of the combat log: appended at the moment each state change occurs, never batched — the log is the replay.</summary>
/// <param name="Tick">
/// The simulation tick the state change occurred on, <c>0..1799</c>. Non-negative, and non-decreasing
/// down the log — <see cref="CombatLog"/> enforces both. The battle-start pre-tick's events carry tick
/// <c>0</c>: it runs before tick 0, and there is no tick <c>-1</c>.
/// </param>
/// <param name="Type">Which state change. See <see cref="CombatEventType"/>.</param>
/// <param name="SourceId">Who caused it, or <see cref="CombatActor.None"/>. See the table below.</param>
/// <param name="TargetId">Who it happened to, or <see cref="CombatActor.None"/>. See the table below.</param>
/// <param name="Value">The event's one number, rounded to 4 dp. See the table below.</param>
/// <param name="DataId">The content id the event names, or <c>0</c>. See the table below.</param>
/// <remarks>
/// <para>
/// This is a wire contract: the server recomputes <c>LogHash</c> over it and compares against the
/// client, and a determinism gate compares it across x64 and ARM64. Its shape is not an implementation
/// detail that a later milestone may adjust.
/// </para>
///
/// <para><b>What every member puts in every slot.</b> Stated exhaustively, because a slot left to each emitter's judgement is a slot on which the client and the server can disagree, which reads as tampering. <c>—</c> means <see cref="CombatActor.None"/>; <c>0</c> means the literal zero.</para>
/// <list type="table">
///   <listheader><term>Member</term><description>SourceId · TargetId · Value · DataId</description></listheader>
///   <item><term><see cref="CombatEventType.BattleStart"/></term>
///     <description>— · — · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Attack"/></term>
///     <description>attacker · defender · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Hit"/></term>
///     <description>whoever dealt it · who lost the HP · the HP actually lost, after ward
///       absorption · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Crit"/></term>
///     <description>attacker · defender · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Miss"/></term>
///     <description>attacker · the dodger · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Block"/></term>
///     <description>attacker · the blocker · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Heal"/></term>
///     <description>the healer (the lifesteal attacker, or the target itself for a HoT) · who gained
///       the HP · the amount actually healed, overheal excluded · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Shield"/></term>
///     <description>the granter · the warded actor · the ward granted, after the pool cap ·
///       <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.StatusApplied"/></term>
///     <description>the applier · the afflicted · the resulting stack count (≥ 1) · status id</description></item>
///   <item><term><see cref="CombatEventType.StatusExpired"/></term>
///     <description>the applier · the afflicted · the stacks remaining (<c>0</c> = gone entirely) ·
///       status id</description></item>
///   <item><term><see cref="CombatEventType.StatusTick"/></term>
///     <description>the applier · the afflicted · the HP delta, signed: negative for a DoT, positive
///       for a HoT · status id</description></item>
///   <item><term><see cref="CombatEventType.PetAbility"/></term>
///     <description>the pet · the ability's target, or — for an untargeted or AoE ability · <c>0</c> ·
///       ability id</description></item>
///   <item><term><see cref="CombatEventType.WardBroken"/></term>
///     <description>whoever broke it · whose pool emptied · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.RunEffectQueued"/></term>
///     <description>the emitting actor · — always (the run is not an actor) · the one
///       runtime-resolved argument · battle-local effect index</description></item>
///   <item><term><see cref="CombatEventType.ActorDeath"/></term>
///     <description>the killer, or — on a non-combat death · the actor that died · <c>0</c> ·
///       <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.PhaseChange"/></term>
///     <description>the boss · the boss · the phase entered (<c>1</c>, <c>2</c> or <c>3</c>) · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.BattleEnd"/></term>
///     <description>— · — · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Telegraph"/></term>
///     <description>the winding-up actor · who it will hit, or — for an AoE · the wind-up in seconds,
///       <c>1.0..1.5</c> · battle-local effect index</description></item>
///   <item><term><see cref="CombatEventType.ActorSpawned"/></term>
///     <description>the summoner, or — for an actor of the opening roster · the actor that entered ·
///       its Max HP, positive · <c>0</c></description></item>
/// </list>
/// <para>
/// <see cref="DataId"/>'s namespace is a function of <see cref="Type"/> alone: a status id for the
/// three <c>Status*</c> members, an ability id for <c>PetAbility</c>, and a battle-local effect index
/// for <c>RunEffectQueued</c> and <c>Telegraph</c>. The effect index is 0-based, so <c>DataId == 0</c>
/// is a legitimate first effect — a consumer that treats <c>0</c> as "no content" for those two members
/// silently drops it. <see cref="CombatLog.NoDataId"/> means "names no content" only for the members
/// whose row above says <c>0</c>.
/// </para>
/// <para>
/// <see cref="CombatEventType.StatusApplied"/> carries the stack count, not the potency: potency is
/// recoverable from the status content tables and the stack count, but the stack count itself is not —
/// reapplication may add a stack or merely refresh, and the two are indistinguishable by counting
/// events. The per-tick number the player sees is <see cref="CombatEventType.StatusTick"/>'s.
/// </para>
/// <para>
/// The one duration the log carries is <see cref="CombatEventType.Telegraph"/>'s wind-up, since it must
/// be known ahead of the event it announces and so cannot be recovered from a later event.
/// </para>
///
/// <para><b>Two deliberate departures from the original spec's declared shape.</b></para>
/// <para>
/// <b>1. Properties, not public fields — public fields hash as zero bytes.</b> The canonical
/// serialiser's field list is a record's primary-constructor parameter list; a public field is not a
/// parameter and not a property, so it contributes no bytes at all. Every log would collide with every
/// other log that shared the few fields that were parameters, and every test written over the hash
/// would pass. A positional <c>record struct</c> has the same members in the same order, is equally
/// immutable, and is the shape the writer can actually see; the writer now refuses the other shape
/// outright rather than hashing it to nothing.
/// </para>
/// <para>
/// <b>2. <see cref="Value"/> is <see cref="double"/>, not <see cref="float"/>.</b> <c>float</c> is not
/// in the canonical encoding's closed allowlist and is unhashable there. It also un-rounds an
/// already-rounded double at the log boundary (<c>1234.5678</c> becomes <c>1234.5677490234375</c>),
/// breaking the "already rounded to 4 dp" invariant every accumulation point relies on. And above
/// <c>2^23</c> a <c>float</c> has no fractional resolution at all, so it would erase exactly the
/// low-order divergence the cross-platform determinism gate exists to detect and the tamper check
/// compares hashes to catch — the gate would go green because precision was thrown away. The cost is
/// nothing on the wire: every scalar is widened to 8 canonical bytes regardless, so a <c>float</c> would
/// have occupied 8 bytes too.
/// </para>
/// <para>
/// <see cref="SourceId"/> and <see cref="TargetId"/> stay <see cref="byte"/>: the 256-actor ceiling is
/// unreachable, and <see cref="CombatActor"/> shows the arithmetic rather than asserting it.
/// </para>
/// </remarks>
public readonly record struct CombatEvent(
    int Tick,
    CombatEventType Type,
    byte SourceId,
    byte TargetId,
    double Value,
    ushort DataId);
