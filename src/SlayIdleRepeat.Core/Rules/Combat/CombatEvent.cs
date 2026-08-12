namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` §7 — one entry of the combat log: <em>"CombatEvents are appended at the moment each
/// state change occurs, <b>not batched</b> (the log is the replay)"</em> (`05` §3.1 step 7).
/// </summary>
/// <param name="Tick">
/// The simulation tick the state change occurred on, <c>0..1799</c> (`05` §3: 20 ticks/second,
/// 90 s cap). Non-negative, and non-decreasing down the log — <see cref="CombatLog"/> enforces
/// both. The battle-start pre-tick's events carry tick <c>0</c>: `05` §3.1 runs it <em>before</em>
/// tick 0, and there is no tick <c>-1</c>.
/// </param>
/// <param name="Type">Which state change. See <see cref="CombatEventType"/>.</param>
/// <param name="SourceId">Who caused it, or <see cref="CombatActor.None"/>. See the table below.</param>
/// <param name="TargetId">Who it happened to, or <see cref="CombatActor.None"/>. See the table below.</param>
/// <param name="Value">The event's one number, rounded to 4 dp (`05` §1.1). See the table below.</param>
/// <param name="DataId">The content id the event names, or <c>0</c>. See the table below.</param>
/// <remarks>
/// <para>
/// 🔒 <b>This is a wire contract.</b> `05` §8 makes the visual battle a replay of this log, `11` §6
/// and `27` §11 have the backend recompute <c>LogHash</c> over it and compare, and `14` §8.2 runs
/// the same hash on Linux x64 and Android ARM64 as the determinism gate. Its shape is not an
/// implementation detail that a later milestone may adjust.
/// </para>
///
/// <para>
/// ═══ <b>WHAT EVERY MEMBER PUTS IN EVERY SLOT</b> 🔒 ═══
/// </para>
/// <para>
/// Stated exhaustively, member by member, because a slot left to each emitter's judgement is a slot
/// on which the client and the server can disagree — and `11` §6 reads that disagreement as
/// tampering. <c>—</c> means <see cref="CombatActor.None"/>; <c>0</c> means the literal zero.
/// </para>
/// <list type="table">
///   <listheader><term>Member</term><description>SourceId · TargetId · Value · DataId</description></listheader>
///   <item><term><see cref="CombatEventType.BattleStart"/></term>
///     <description>— · — · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Attack"/></term>
///     <description>attacker · defender · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Hit"/></term>
///     <description>whoever dealt it · <b>who lost the HP</b> · the HP actually lost, after ward
///       absorption (`05` §4 step 9) · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Crit"/></term>
///     <description>attacker · defender · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Miss"/></term>
///     <description>attacker · the dodger · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Block"/></term>
///     <description>attacker · the blocker · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Heal"/></term>
///     <description>the healer (the lifesteal attacker, or the target itself for a HoT) · <b>who
///       gained the HP</b> · the amount actually healed, overheal excluded (`05` §4.3) ·
///       <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Shield"/></term>
///     <description>the granter · the warded actor · the ward granted, after the pool cap
///       (`05` §4.1) · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.StatusApplied"/></term>
///     <description>the applier · the afflicted · <b>the resulting stack count</b> (≥ 1) ·
///       status id</description></item>
///   <item><term><see cref="CombatEventType.StatusExpired"/></term>
///     <description>the applier · the afflicted · <b>the stacks remaining</b> (<c>0</c> = gone
///       entirely) · status id</description></item>
///   <item><term><see cref="CombatEventType.StatusTick"/></term>
///     <description>the applier · the afflicted · <b>the HP delta, signed</b>: negative for a DoT,
///       positive for a HoT · status id</description></item>
///   <item><term><see cref="CombatEventType.PetAbility"/></term>
///     <description>the pet · the ability's target, or — for an untargeted or AoE ability ·
///       <c>0</c> · ability id</description></item>
///   <item><term><see cref="CombatEventType.WardBroken"/></term>
///     <description>whoever broke it · <b>whose pool emptied</b> · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.RunEffectQueued"/></term>
///     <description>the emitting actor · <b>— always</b> (`18` §5's <c>RUN</c> is not an actor) ·
///       the one runtime-resolved argument · battle-local effect index</description></item>
///   <item><term><see cref="CombatEventType.ActorDeath"/></term>
///     <description>the killer, or — on a non-combat death · <b>the actor that died</b> · <c>0</c> ·
///       <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.PhaseChange"/></term>
///     <description>the boss · the boss · the phase entered (<c>1</c>, <c>2</c> or <c>3</c> —
///       `05` §6.3) · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.BattleEnd"/></term>
///     <description>— · — · <c>0</c> · <c>0</c></description></item>
///   <item><term><see cref="CombatEventType.Telegraph"/></term>
///     <description>the winding-up actor · who it will hit, or — for an AoE · the wind-up in
///       <b>seconds</b>, <c>1.0..1.5</c> (`17` §1) · battle-local effect index</description></item>
/// </list>
/// <para>
/// 🔒 <b><see cref="DataId"/>'s namespace is a function of <see cref="Type"/> alone.</b> It is a
/// status id for the three <c>Status*</c> members, an ability id for
/// <see cref="CombatEventType.PetAbility"/>, and a battle-local effect index for
/// <see cref="CombatEventType.RunEffectQueued"/> and <see cref="CombatEventType.Telegraph"/>.
/// ⚠️ <b>The effect index is 0-based, so <c>DataId == 0</c> is a legitimate first effect</b> — a
/// consumer that treats <c>0</c> as "no content" for those two members silently drops it.
/// <see cref="CombatLog.NoDataId"/> means "names no content" only for the members whose row above
/// says <c>0</c>.
/// </para>
/// <para>
/// ⚠️ <b><see cref="CombatEventType.StatusApplied"/> carries the stack count, not the potency.</b>
/// `13` §5 requires <em>"status effect icons … with stack counts"</em> under every HP bar, and the
/// log is the only thing the replayer has. Potency is recoverable from the status content tables
/// and the stack count; a stack count is recoverable from nothing else — reapplication may add a
/// stack or merely refresh (`18` §6), and the two are indistinguishable by counting events. The
/// per-tick number the player actually sees is <see cref="CombatEventType.StatusTick"/>'s.
/// </para>
/// <para>
/// ⚠️ <b>`05` §7's third stated meaning of <see cref="Value"/>, "duration", is deliberately
/// unused.</b> The comment reads <c>// damage / heal / duration</c>, but no member above carries a
/// status duration and one cannot be added — the slot is spent, and the stack count is worth more.
/// Nothing is lost: <see cref="CombatEventType.StatusExpired"/> marks the end of a status exactly,
/// so a replayer knows how long every status lasted without being told in advance, and `13` §5 asks
/// for stack counts, not countdown timers. The one duration the log does carry is
/// <see cref="CombatEventType.Telegraph"/>'s wind-up, which must be known <i>ahead</i> of the event
/// it announces and so cannot be recovered from a later event. Recorded as errata.
/// </para>
///
/// <para>
/// ═══ <b>TWO DELIBERATE DEPARTURES FROM `05` §7's DECLARATION</b> ═══
/// </para>
/// <para>
/// <b>1. Properties, not public fields — because public fields hash as zero bytes.</b> `05` §7
/// writes this as a <c>readonly struct</c> whose six members are public <b>fields</b>.
/// <c>LogHash</c> goes through <c>CanonicalStateWriter</c> (`14` §16.6, the project's one
/// serialiser), whose field list <b>is</b> a record's primary-constructor parameter list. A public
/// field is not a parameter and not a property, so it contributes <b>no bytes at all</b>: a
/// <c>CombatEvent</c> declared `05` §7's way would hash only whatever <i>was</i> in its
/// constructor, every log would collide with every other log that shared those few fields, and
/// every test written over the hash would pass. A positional <c>record struct</c> has the same six
/// members in the same order, is equally immutable, and is the shape the writer can actually see.
/// The writer now <b>refuses</b> the other shape outright rather than hashing it to nothing — see
/// <c>CanonicalStateWriter.CanonicalProperties</c>.
/// </para>
/// <para>
/// (As written, `05` §7 does not compile either: C# forbids a mutable instance field on a
/// <c>readonly struct</c>. Recorded as errata.)
/// </para>
/// <para>
/// <b>2. <see cref="Value"/> is <see cref="double"/>, not <see cref="float"/>.</b> `05` §7 says
/// <c>float</c>. That is a defect in a determinism-critical structure, for three independent
/// reasons, any one of which is sufficient:
/// </para>
/// <list type="number">
///   <item>
///     <b>`14` §16.6 has no <c>float</c> row.</b> The canonical encoding covers integers,
///     booleans, enums, strings, <b>doubles</b>, timestamps, optionals, lists, maps and records —
///     and <c>CanonicalStateWriter</c> enforces that with a closed allowlist. <c>float</c> already
///     sits in the test suite's list of shapes the writer must <i>refuse</i>. A <c>float</c>
///     <see cref="Value"/> is therefore not merely lossy, it is <b>unhashable</b>, and
///     <c>LogHash</c> is defined as a hash over this list.
///   </item>
///   <item>
///     <b>It contradicts `05` §1.1 and breaks its rounding rule at the log boundary.</b> `05` §1.1
///     and `14` §8.2 both open with <em>"all combat math uses <c>double</c>"</em>, rounded to 4 dp.
///     Narrowing a rounded double to <c>float</c> un-rounds it: <c>1234.5678</c> becomes
///     <c>1234.5677490234375</c>, which fails <c>Math.Round(x, 4) == x</c> — the guard `14` §8.2
///     makes the writer apply to every double it sees. Every damage figure in the game would trip
///     it.
///   </item>
///   <item>
///     <b>It would make the determinism gate lie.</b> A <c>float</c> holds about seven significant
///     digits, so above <c>2^23</c> it has no fractional resolution at all:
///     <c>8388609.0001</c> and <c>8388609.0002</c> — two damage figures `05` §1.1 calls distinct —
///     are the <b>same</b> <c>float</c>. Narrowing at the log boundary would erase exactly the
///     low-order divergence `14` §8.2 runs on x64 and ARM64 to detect, and exactly the tampering
///     `11` §6 compares hashes to catch. The gate would go green <i>because</i> precision was
///     thrown away.
///   </item>
/// </list>
/// <para>
/// <b>The cost is nothing on the wire.</b> `14` §16.6 widens every scalar to 8 canonical bytes, so
/// a <c>float</c> would have occupied 8 bytes too: an event encodes to 48 bytes either way. The
/// whole cost of the widening is 4 bytes of this field in memory.
/// </para>
/// <para>
/// <see cref="SourceId"/> and <see cref="TargetId"/> stay <see cref="byte"/>: `05` §7's ceiling of
/// 256 actors is unreachable, and <see cref="CombatActor"/> shows the arithmetic rather than
/// asserting it.
/// </para>
/// </remarks>
public readonly record struct CombatEvent(
    int Tick,
    CombatEventType Type,
    byte SourceId,
    byte TargetId,
    double Value,
    ushort DataId);
