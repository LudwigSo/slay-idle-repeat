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
/// <param name="SourceId">Who caused it, or <see cref="CombatActor.None"/>. See <see cref="CombatActor"/>.</param>
/// <param name="TargetId">Who it happened to, or <see cref="CombatActor.None"/>.</param>
/// <param name="Value">
/// The event's one number — <em>"damage / heal / duration"</em> (`05` §7), rounded to 4 decimal
/// places (`05` §1.1). Per member:
/// <list type="bullet">
///   <item><see cref="CombatEventType.Hit"/>: the HP actually lost, <b>after</b> ward absorption
///         (`05` §4 step 9) — the number the replayer draws as floating combat text.</item>
///   <item><see cref="CombatEventType.Heal"/>: the amount actually healed, overheal excluded
///         (`05` §4.3).</item>
///   <item><see cref="CombatEventType.Shield"/>: the ward amount granted, after the pool cap
///         (`05` §4.1).</item>
///   <item><see cref="CombatEventType.StatusApplied"/> / <see cref="CombatEventType.StatusTick"/>:
///         the potency applied on this application or tick.</item>
///   <item><see cref="CombatEventType.StatusExpired"/>: <c>0</c>.</item>
///   <item><see cref="CombatEventType.PhaseChange"/>: the phase entered (<c>1</c>, <c>2</c> or
///         <c>3</c> — `05` §6.3).</item>
///   <item><see cref="CombatEventType.Telegraph"/>: the wind-up in <b>seconds</b>, which `17` §1
///         bounds to <c>1.0..1.5</c>.</item>
///   <item><see cref="CombatEventType.RunEffectQueued"/>: the queued op's one runtime-resolved
///         argument — see <see cref="CombatLog.AppendRunEffectQueued"/>.</item>
///   <item><see cref="CombatEventType.Attack"/>, <see cref="CombatEventType.Crit"/>,
///         <see cref="CombatEventType.Miss"/>, <see cref="CombatEventType.Block"/>,
///         <see cref="CombatEventType.WardBroken"/>, <see cref="CombatEventType.ActorDeath"/>,
///         <see cref="CombatEventType.BattleStart"/>, <see cref="CombatEventType.BattleEnd"/>:
///         <c>0</c>.</item>
/// </list>
/// </param>
/// <param name="DataId">
/// The content id the event names, or <c>0</c> where it names none: a status id for the
/// <c>Status*</c> members, an ability id for <see cref="CombatEventType.PetAbility"/>, and the
/// battle-local effect index for <see cref="CombatEventType.RunEffectQueued"/> and
/// <see cref="CombatEventType.Telegraph"/>.
/// </param>
/// <remarks>
/// <para>
/// 🔒 <b>This is a wire contract.</b> `05` §8 makes the visual battle a replay of this log, `11` §6
/// has the PvP backend recompute <c>LogHash</c> over it and compare, and M5-12 runs the same hash
/// on x64 and two ARM64 devices as the determinism gate. Its shape is not an implementation detail
/// that a later milestone may adjust.
/// </para>
/// <para>
/// <b>⚠️ Two deliberate departures from `05` §7's declaration, both stated rather than made
/// quietly.</b>
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
///     <b>It breaks `05` §1.1's own rounding rule at the log boundary.</b> All combat math is
///     <c>double</c> rounded to 4 dp. Narrowing a rounded double to <c>float</c> un-rounds it:
///     <c>1234.5678</c> becomes <c>1234.5677490234375</c>, which fails
///     <c>Math.Round(x, 4) == x</c> — the guard `14` §8.2 makes the writer apply to every double
///     it sees. Every damage figure in the game would trip it.
///   </item>
///   <item>
///     <b>It would make the determinism gate lie.</b> A <c>float</c> holds about seven significant
///     digits, so above <c>2^23</c> it has no fractional resolution at all:
///     <c>8388609.0001</c> and <c>8388609.0002</c> — two damage figures `05` §1.1 calls distinct —
///     are the <b>same</b> <c>float</c>. Narrowing at the log boundary would erase exactly the
///     low-order divergence M5-12 runs on x64 and ARM64 to detect, and exactly the tampering
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
internal readonly record struct CombatEvent(
    int Tick,
    CombatEventType Type,
    byte SourceId,
    byte TargetId,
    double Value,
    ushort DataId);
