namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` §7 — the closed vocabulary of the combat log.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The numeric values are a wire contract, not an implementation detail.</b> A
/// <see cref="CombatEvent"/> hashes its <see cref="CombatEvent.Type"/> as the underlying
/// <see cref="int"/> (`14` §16.6's enum rule), and that hash is <c>LogHash</c> — the cross-platform
/// determinism gate of M5-12 and the anti-tamper comparison of `11` §6. <b>Inserting a member
/// anywhere but the end silently changes every <c>LogHash</c> in existence</b>, on both sides of a
/// duel and on both architectures of the determinism job. The values are therefore written out
/// explicitly rather than left to the compiler's implicit numbering, and new members are
/// <b>appended</b>.
/// </para>
/// <para>
/// The first seventeen members are `05` §7's list, in `05` §7's order.
/// <see cref="Telegraph"/> is the eighteenth and is an <b>addition</b> — see its own remarks.
/// </para>
/// <para>
/// <b>What each member means is settled here so the four tasks that emit them cannot disagree.</b>
/// M2-08 (tick loop), M2-09 (damage resolution), M2-10 (statuses) and M2-12 (boss phases) all
/// append through <see cref="CombatLog"/>; the meaning of a member, the actor it names and what it
/// puts in <see cref="CombatEvent.Value"/> and <see cref="CombatEvent.DataId"/> are stated on
/// <see cref="CombatEvent"/>, member by member.
/// </para>
/// </remarks>
internal enum CombatEventType
{
    /// <summary>The pre-tick has completed and tick 0 is about to run (`05` §3.1 step 0d).</summary>
    BattleStart = 0,

    /// <summary>An actor's basic attack fires (`05` §3.1 step 4a), before the outcome is known.</summary>
    Attack = 1,

    /// <summary>An attack landed. <see cref="CombatEvent.Value"/> is the HP actually lost (`05` §4 step 9).</summary>
    Hit = 2,

    /// <summary>The landed attack was a critical (`05` §4 step 4). Accompanies its <see cref="Hit"/>.</summary>
    Crit = 3,

    /// <summary>The attack was dodged (`05` §4 step 1). No damage follows.</summary>
    Miss = 4,

    /// <summary>The landed attack was blocked and halved (`05` §4 step 5). Accompanies its <see cref="Hit"/>.</summary>
    Block = 5,

    /// <summary>HP was restored. <see cref="CombatEvent.Value"/> is the amount actually healed (`05` §4.3).</summary>
    Heal = 6,

    /// <summary>A ward segment was granted (`05` §4.1). Fires on <b>every</b> grant.</summary>
    Shield = 7,

    /// <summary>A status was applied or refreshed (`05` §5).</summary>
    StatusApplied = 8,

    /// <summary>
    /// A status ended — by its timer, by an <c>until</c> terminator, or by a ward segment's
    /// remainder being dropped at expiry (`05` §4.1, `18` §6).
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

    /// <summary>The fight is over. The last event of every log.</summary>
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
