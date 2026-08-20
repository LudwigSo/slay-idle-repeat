namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>The closed vocabulary of the combat log.</summary>
/// <remarks>
/// <para>
/// The numeric values are a wire contract, not an implementation detail. A <see cref="CombatEvent"/>
/// hashes its <see cref="CombatEvent.Type"/> as the underlying <see cref="int"/>, and that hash is
/// inside <c>LogHash</c> — the cross-platform determinism gate and the anti-tamper comparison.
/// Inserting a member anywhere but the end silently changes every <c>LogHash</c> in existence, on both
/// sides of a duel and on both architectures of the determinism job. The values are therefore written
/// out explicitly, and new members are appended.
/// </para>
/// <para><see cref="Telegraph"/> and <see cref="ActorSpawned"/> are additions beyond the original seventeen-member set — see their own remarks.</para>
/// <para>An undefined value is refused by <see cref="CombatLog.Append"/>: this is a closed vocabulary, and an out-of-range value would otherwise hash as its ordinal and replay as nothing at all.</para>
///
/// <para><b>The per-attack emission sequence.</b> Events are appended at the moment each state change occurs, which is step order rather than the damage formula's own line order — the HP decrease happens before the lifesteal and thorns that follow it. Two implementations that each read the formula honestly could otherwise produce different <c>LogHash</c>es for the same fight:</para>
/// <list type="number">
///   <item><see cref="Attack"/> — the swing starts, before any outcome is known.</item>
///   <item><see cref="Miss"/> if the defender dodged. <b>The attack ends here.</b></item>
///   <item><see cref="Crit"/> if it critted.</item>
///   <item><see cref="Block"/> if it was blocked.</item>
///   <item><see cref="WardBroken"/> if absorption emptied the pool.</item>
///   <item><see cref="Hit"/> carrying the HP actually lost.</item>
///   <item><see cref="PhaseChange"/> per phase entered by the phase check — more than one when a burst crosses two thresholds.</item>
///   <item><see cref="Heal"/> for the attacker's lifesteal.</item>
///   <item>the thorns reflect, which is itself a <see cref="Hit"/> on the attacker and carries its own <see cref="WardBroken"/> and <see cref="PhaseChange"/> in the same order.</item>
/// </list>
/// <para>
/// <see cref="Attack"/> marks a basic attack only. Every swing gets a four-frame attack animation which
/// the replayer must play even on a miss, so the member exists purely as that cue — a DSL damage op
/// routed through the same pipeline emits the outcome events above but not <see cref="Attack"/>, since
/// it is not a swing, and a pet ability already has <see cref="PetAbility"/> as its cue.
/// </para>
/// <para><see cref="ActorDeath"/> is not part of this sequence — death resolution is deferred to a later slot, so it lands after every attack in the tick.</para>
/// </remarks>
public enum CombatEventType
{
    /// <summary>The pre-tick has completed and tick 0 is about to run.</summary>
    /// <remarks>
    /// Exactly one per log, at tick 0, with both actor slots <see cref="CombatActor.None"/> — all three
    /// enforced, since a disagreement about this event's actor ids between client and server would read
    /// as tampering. It is not the first event of the log: it is emitted after the roster's
    /// <see cref="ActorSpawned"/> entries, and after the ward grants and opening buffs, of the
    /// battle-start pre-tick.
    /// </remarks>
    BattleStart = 0,

    /// <summary>A basic attack's swing begins, before the outcome is known.</summary>
    Attack = 1,

    /// <summary>An actor lost HP. <see cref="CombatEvent.Value"/> is the amount actually lost, after ward absorption.</summary>
    /// <remarks>
    /// Every HP decrease, not only an attack's — a thorns reflect, a true-damage or max-HP-percent op,
    /// or a self-inflicted cost that emitted nothing would otherwise be damage the player watches land
    /// with no number attached. The only HP decrease that is not a <see cref="Hit"/> is a DoT tick,
    /// which has <see cref="StatusTick"/> so the replayer can colour it distinctly.
    /// </remarks>
    Hit = 2,

    /// <summary>The landed attack was a critical. Precedes its <see cref="Hit"/>.</summary>
    Crit = 3,

    /// <summary>The attack was dodged. Ends the attack; no <see cref="Hit"/> follows.</summary>
    Miss = 4,

    /// <summary>The landed attack was blocked and halved. Precedes its <see cref="Hit"/>.</summary>
    Block = 5,

    /// <summary>HP was restored. <see cref="CombatEvent.Value"/> is the amount actually healed.</summary>
    Heal = 6,

    /// <summary>A ward segment was granted. Fires on every grant.</summary>
    Shield = 7,

    /// <summary>A status was applied or refreshed.</summary>
    StatusApplied = 8,

    /// <summary>A status lost a stack or ended — by its timer, by an <c>until</c> terminator, or by a ward segment's remainder being dropped at expiry.</summary>
    StatusExpired = 9,

    /// <summary>A DoT or HoT instance's cadence landed.</summary>
    StatusTick = 10,

    /// <summary>A pet's active ability fired.</summary>
    PetAbility = 11,

    /// <summary>The ward pool reached 0 through damage — the event a "until ward broken" duration terminator watches for. Segment expiry emits <see cref="StatusExpired"/> instead and never this.</summary>
    WardBroken = 12,

    /// <summary>A combat trigger emitted a run/board op. The simulator never resolves it — see <see cref="CombatLog.AppendRunEffectQueued"/> for the encoding and the handover.</summary>
    RunEffectQueued = 13,

    /// <summary>An actor's death resolved, after its <c>ON_DEATH</c> effects.</summary>
    ActorDeath = 14,

    /// <summary>A boss entered a phase.</summary>
    PhaseChange = 15,

    /// <summary>The fight is over. The last event of every log, emitted by <see cref="CombatLog.Complete"/>.</summary>
    BattleEnd = 16,

    /// <summary>A damaging boss mechanic's wind-up has begun. Not in the original event vocabulary — added deliberately.</summary>
    /// <remarks>
    /// <para>
    /// Every damaging boss mechanic requires a visible 1.0–1.5 s wind-up, telegraphed 1.0–1.5 s ahead of
    /// the mechanic it announces. The original seventeen-member vocabulary had no member that could
    /// express one, and since the visual battle is a replay of the log, a telegraph that is not in the
    /// log cannot be drawn at all. This is the smallest addition that closes that gap.
    /// </para>
    /// <para>
    /// Appended, never inserted, for the reason in the type remarks: the ordinal is hashed. Boss
    /// mechanics emit these through <see cref="CombatLog.AppendTelegraph"/>, which enforces the 1.0–1.5 s band.
    /// </para>
    /// </remarks>
    Telegraph = 17,

    /// <summary>
    /// An actor has entered the fight. <see cref="CombatEvent.Value"/> is its Max HP — the
    /// denominator of its health bar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Added because a health bar needs a denominator and no other member carries one.</b> Every
    /// other event is a delta, so an actor's maximum was recoverable from the log for exactly two
    /// actors: the hero, whose remaining HP <see cref="SimulationResult.HeroHpRemaining"/> reports, and
    /// anyone the log records an <see cref="ActorDeath"/> for, who ended at zero. An enemy that
    /// survived anchored neither equation, so the battle replay drew <em>no enemy health bar at all</em>
    /// — and since a losing hero never kills anything, that was every enemy of every lost fight. The
    /// screen looked like a hero being beaten by something invulnerable. It was not: the enemy was
    /// taking damage the whole time and the log said so, one <see cref="Hit"/> at a time, with nothing
    /// to draw them against.
    /// </para>
    /// <para>
    /// 🔒 <b>Appended, and it changes every <c>LogHash</c> in existence.</b> That is the cost of putting
    /// anything new in the log and it is paid once: the ordinal is 18 rather than an insertion, so no
    /// <em>existing</em> member's hashed value moves, and client and server reach the same new hash
    /// because both compute the fight through one composition. No stored hash outlives a battle — the
    /// confirming command carries the hash of the fight it is confirming — so there is nothing
    /// persisted for the change to invalidate.
    /// </para>
    /// <para>
    /// 🔒 <b>One per actor, at the moment it enters, rather than one roster event for the fight.</b> A
    /// summon enters mid-fight and its bar needs a denominator from the tick it appears on, and
    /// <see cref="CombatEvent"/> has one actor slot pair and one number — a roster of nine actors
    /// cannot be spelled in either.
    /// </para>
    /// <para>
    /// ⚠️ <b>The maximum, not the opening current HP.</b> They differ for exactly one actor — the hero,
    /// who opens on the health its run persisted — and that one is already recoverable by undoing every
    /// change the log records against
    /// <see cref="SimulationResult.HeroHpRemaining"/>. Everything else opens full, because nothing else
    /// carries health between fights. A consumer that cannot anchor an opening value therefore reads
    /// this one, and is right about every actor the game composes.
    /// </para>
    /// </remarks>
    ActorSpawned = 18,
}
