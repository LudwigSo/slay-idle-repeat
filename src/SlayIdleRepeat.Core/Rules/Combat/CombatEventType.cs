namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` §7 — the closed vocabulary of the combat log.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The numeric values are a wire contract, not an implementation detail.</b> A
/// <see cref="CombatEvent"/> hashes its <see cref="CombatEvent.Type"/> as the underlying
/// <see cref="int"/> (`14` §16.6's enum rule), and that hash is <c>LogHash</c> — the cross-platform
/// determinism gate (`14` §8.2) and the anti-tamper comparison of `11` §6 and `27` §11.
/// <b>Inserting a member anywhere but the end silently changes every <c>LogHash</c> in
/// existence</b>, on both sides of a duel and on both architectures of the determinism job. The
/// values are therefore written out explicitly rather than left to the compiler's implicit
/// numbering, and new members are <b>appended</b>.
/// </para>
/// <para>
/// The first seventeen members are `05` §7's list, in `05` §7's order.
/// <see cref="Telegraph"/> is the eighteenth and is an <b>addition</b> — see its own remarks.
/// </para>
/// <para>
/// 🔒 <b>An undefined value is refused</b> by <see cref="CombatLog.Append"/>: this is a closed
/// vocabulary, and <c>(CombatEventType)42</c> would otherwise hash as ordinal 42 and replay as
/// nothing at all.
/// </para>
///
/// <para>
/// ═══ <b>THE PER-ATTACK EMISSION SEQUENCE</b> 🔒 ═══
/// </para>
/// <para>
/// `05` §4's pseudocode writes <c>log(HIT, …)</c> as its last line, but `05` §3.1 step 7 governs:
/// events are appended <em>"at the moment each state change occurs"</em>. The HP decrease happens
/// at §4 step 9, before the lifesteal and thorns of step 10 — so the order below is §4's <b>step
/// order</b>, not its line order. Both are stated because they differ, and because two
/// implementations that each read §4 honestly would otherwise produce different <c>LogHash</c>es
/// for the same fight. <b>M2-09 owns this pipeline and must emit exactly this:</b>
/// </para>
/// <list type="number">
///   <item><see cref="Attack"/> — the swing starts (tick slot 4a), before any outcome is known.</item>
///   <item><see cref="Miss"/> if the defender dodged (§4 step 1). <b>The attack ends here.</b></item>
///   <item><see cref="Crit"/> if it critted (§4 step 4).</item>
///   <item><see cref="Block"/> if it was blocked (§4 step 5).</item>
///   <item><see cref="WardBroken"/> if absorption emptied the pool (§4 step 9, §4.1).</item>
///   <item><see cref="Hit"/> carrying the HP actually lost (§4 step 9).</item>
///   <item><see cref="PhaseChange"/> per phase entered by the phase check (§4 step 9, §3.1) — more
///         than one when a burst crosses two thresholds.</item>
///   <item><see cref="Heal"/> for the attacker's lifesteal (§4 step 10).</item>
///   <item>the thorns reflect (§4 step 10), which is itself a <see cref="Hit"/> on the attacker and
///         carries its own <see cref="WardBroken"/> and <see cref="PhaseChange"/> in the same
///         order.</item>
/// </list>
/// <para>
/// 🔒 <b><see cref="Attack"/> marks a <i>basic</i> attack only.</b> `05` §4's pseudocode has no
/// <c>log(ATTACK)</c> at all — the member exists because `05` §8 gives every swing a four-frame
/// attack animation, which the replayer must play even when the swing misses. A DSL <c>DAMAGE</c>
/// op routed through the same pipeline (`05` §4.2) emits the <b>outcome</b> events above but
/// <b>not</b> <see cref="Attack"/>: it is not a swing, and a pet ability already has
/// <see cref="PetAbility"/> as its cue.
/// </para>
/// <para>
/// <see cref="ActorDeath"/> is not part of this sequence — `05` §3.1 defers death <i>resolution</i>
/// to tick slot 6, so it lands after every attack in the tick.
/// </para>
/// </remarks>
public enum CombatEventType
{
    /// <summary>The pre-tick has completed and tick 0 is about to run (`05` §3.1 step 0d).</summary>
    /// <remarks>
    /// 🔒 Exactly one per log, at tick 0, with both actor slots <see cref="CombatActor.None"/> —
    /// all three enforced, because `11` §6 compares a client-computed <c>LogHash</c> against a
    /// server-computed one and a disagreement about this event's actor ids would read as tampering.
    /// It is <b>not</b> the first event of the log: `05` §3.1 emits it at step 0d, <i>after</i> the
    /// ward grants and opening buffs of step 0b.
    /// </remarks>
    BattleStart = 0,

    /// <summary>A basic attack's swing begins (`05` §3.1 step 4a), before the outcome is known.</summary>
    Attack = 1,

    /// <summary>
    /// An actor lost HP. <see cref="CombatEvent.Value"/> is the amount actually lost, after ward
    /// absorption (`05` §4 step 9).
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Every HP decrease, not only an attack's.</b> `05` §7 offers no other member for one,
    /// and `05` §8 makes the log the replay — so a thorns reflect (`05` §4's <c>ReflectDamage</c>),
    /// a <c>DAMAGE_TRUE</c>, a <c>DAMAGE_MAXHP_PCT</c> (`18` §2.2, routed by `05` §4.2) or a
    /// self-inflicted cursed-perk cost that emitted nothing would be damage the player watches land
    /// with no number attached. The only HP decrease that is <b>not</b> a <see cref="Hit"/> is a
    /// DoT tick, which has <see cref="StatusTick"/> so the replayer can colour it purple (`05` §8).
    /// </remarks>
    Hit = 2,

    /// <summary>The landed attack was a critical (`05` §4 step 4). Precedes its <see cref="Hit"/>.</summary>
    Crit = 3,

    /// <summary>The attack was dodged (`05` §4 step 1). Ends the attack; no <see cref="Hit"/> follows.</summary>
    Miss = 4,

    /// <summary>The landed attack was blocked and halved (`05` §4 step 5). Precedes its <see cref="Hit"/>.</summary>
    Block = 5,

    /// <summary>HP was restored. <see cref="CombatEvent.Value"/> is the amount actually healed (`05` §4.3).</summary>
    Heal = 6,

    /// <summary>A ward segment was granted (`05` §4.1). Fires on <b>every</b> grant.</summary>
    Shield = 7,

    /// <summary>A status was applied or refreshed (`05` §5).</summary>
    StatusApplied = 8,

    /// <summary>
    /// A status lost a stack or ended — by its timer, by an <c>until</c> terminator, or by a ward
    /// segment's remainder being dropped at expiry (`05` §4.1, `18` §6).
    /// </summary>
    StatusExpired = 9,

    /// <summary>A DoT or HoT instance's one-second cadence landed (`05` §3.1).</summary>
    StatusTick = 10,

    /// <summary>A pet's active ability fired (`05` §3.1 step 5).</summary>
    PetAbility = 11,

    /// <summary>
    /// The ward pool reached 0 <b>through damage</b> (`05` §4.1) — the event Ossify's DR buff
    /// terminates on. Segment expiry emits <see cref="StatusExpired"/> instead and never this.
    /// </summary>
    WardBroken = 12,

    /// <summary>
    /// A combat trigger emitted a run/board op (`18` §2.5). The simulator never resolves it —
    /// see <see cref="CombatLog.AppendRunEffectQueued"/> for the encoding and the handover.
    /// </summary>
    RunEffectQueued = 13,

    /// <summary>An actor's death resolved (`05` §3.1 step 6), after its <c>ON_DEATH</c> effects.</summary>
    ActorDeath = 14,

    /// <summary>A boss entered a phase (`05` §3.1's phase check, `17` §11's "UI band").</summary>
    PhaseChange = 15,

    /// <summary>The fight is over. The last event of every log, emitted by <see cref="CombatLog.Complete"/>.</summary>
    BattleEnd = 16,

    /// <summary>
    /// ⚠️ <b>Not in `05` §7 — added here, and deliberately.</b> A damaging boss mechanic's
    /// wind-up has begun.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `17` §1 requires that <em>"every damaging mechanic has a visible 1.0–1.5 s wind-up"</em> and
    /// `17` §11 lists <em>"telegraph events emitted 1.0–1.5 s ahead of every damaging mechanic"</em>
    /// as an implementation deliverable. `05` §7's enum has no member that can express one, and
    /// `05` §8 makes the visual battle a <b>replay of the log</b> — so a telegraph that is not in
    /// the log cannot be drawn at all. The two documents cannot both be satisfied by `05` §7's
    /// seventeen members; this is the smallest addition that satisfies both.
    /// </para>
    /// <para>
    /// 🔒 <b>Appended, never inserted</b>, for the reason in the type remarks: the ordinal is
    /// hashed. Recorded as errata against `05` §7 for the milestone conductor. M2-12 owns
    /// telegraphs and emits these through <see cref="CombatLog.AppendTelegraph"/>, which enforces
    /// the 1.0–1.5 s band `17` §1 states.
    /// </para>
    /// </remarks>
    Telegraph = 17,
}
