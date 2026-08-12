using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// One effect an actor brings into a battle, and the <c>EffectInstanceId</c> it holds it under.
/// </summary>
/// <param name="Effect">The authored effect.</param>
/// <param name="InstanceId">
/// 🔒 The instance id, or <c>null</c> to have the simulator mint a <b>battle-local</b> one.
/// <para>
/// <c>TriggerRegistry</c> states the minting rule and cannot check it: every live copy of an effect
/// needs a <b>distinct</b> id, and an <c>ON_KILL</c> id must be <b>stable across battles</b> because
/// its counter is the run's. So a hero-side holding — a perk in a draft slot, an affix on a gear item
/// — passes an id the run layer owns and repeats every fight, and a boss, elite or summon effect
/// passes <c>null</c>.
/// </para>
/// <para>
/// 🔒 <b>A <c>null</c> on an <c>ON_KILL</c> effect is refused</b> (<see cref="BattlePlan"/>). A minted
/// id is battle-local by construction, so accepting one would reset <c>PK_MIDAS</c>'s "every 6th
/// enemy killed" every fight — a defect that produces an entirely legal-looking log and is wrong in
/// the only number that matters.
/// </para>
/// </param>
internal readonly record struct HeldEffect(EffectDefinition Effect, EffectInstanceId? InstanceId = null);

/// <summary>
/// 🔒 One actor as it enters a fight — `05` §1's stat block, `05` §6.0's level, its `18` §8 effect
/// holdings and the four facts `18` §4's conditions read about it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Not named <c>*Snapshot</c>, deliberately</b>, for <c>ActorStats</c>' reason: `05` §1 calls
/// the simulator's inputs <c>heroSnapshot</c>/<c>enemySnapshot</c>, but the <c>*Snapshot</c> suffix
/// belongs to `14` §16.6's persistence contract and its committed field-order pin. Nothing in a
/// battle is persisted state.
/// </para>
/// <para>
/// 🔒 <b>It is a plan, not the actor.</b> Nothing here is mutable and nothing here changes during a
/// fight; <see cref="BattleActor"/> is the live state and holds a reference back to this. That split
/// is what makes `05` §7's <em>"the visual battle is a replay of a pre-computed log"</em> checkable —
/// the inputs to a fight are a value, so re-running one is re-running the same value.
/// </para>
/// </remarks>
internal sealed record ActorPlan
{
    /// <summary>
    /// The actor's stable identity — <c>IEffectActorView.Id</c>, which <c>OWNER</c> matches on.
    /// Unique within one roster.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 🔒 `05` §3.1's fixed actor index — <em>"hero, pets in slot order, enemies by index"</em>. It is
    /// the loop's initiative order, `18` §5's tie-break, and the order slot 6 resolves deaths in.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>The `05` §7 log id — <c>CombatActor.Hero</c>, <c>Pet(slot)</c> or <c>Enemy(index)</c>.</summary>
    public required byte LogId { get; init; }

    /// <summary>Which side. `18` §5's enemy tokens are relative to this.</summary>
    public required BattleSide Side { get; init; }

    /// <summary>Hero, pet or enemy.</summary>
    public required EffectActorKind Kind { get; init; }

    /// <summary>
    /// 🔒 `05` §1's fourteen combat stats, <b>before</b> `18` §8 — <c>Base(stat)</c>, which is `05`
    /// §2's hero curve or `05` §6's enemy derivation.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A pet has one and it is never read for a basic attack.</b> `05` §3.2 is 🔒 that pets
    /// <em>"have no ATK/ASPD stats of their own"</em>; the block exists because a pet still carries
    /// `18` §8 aura effects that a <c>STAT_COPY</c> may read, and <c>ActorStats</c> has no partial
    /// form — <em>"an unstated stat is a bug, not a zero"</em>. <see cref="BattleSimulation"/> never
    /// reaches a pet's ASPD, because slot 4 never considers a pet.
    /// </remarks>
    public required ActorStats BaseStats { get; init; }

    /// <summary>
    /// 🔒 `05` §6.0's level — the <c>20 * attackerLevel</c> term of `05` §4's mitigation curve. For
    /// the hero it is the Legend Level (`05` §2); for every enemy in a <c>(chapter, tier)</c> it is
    /// the one shared <c>EnemyLevel</c>.
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// The `18` §8 effects this actor holds — gear, affixes, talents, auras, perks, boss mechanics.
    /// Order is immaterial; every step that reads them imposes `18` §8's ordinal effect-id order.
    /// </summary>
    public IReadOnlyList<HeldEffect> Effects { get; init; } = Array.Empty<HeldEffect>();

    /// <summary>
    /// 🔒 `05` §3.2's <c>targetPriority</c>: the hero targets the enemy with the <b>highest</b> one,
    /// ties broken by <b>lowest current HP</b>. Default <c>0</c>; <c>-1</c> deprioritises (Sporequeen
    /// Vell's sporelings, `17` §8) and <c>+1</c> forces focus.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>M2-11 owns the authored field on enemy definitions; this task owns the selection
    /// algorithm.</b> The value arrives here as a number because that is all the algorithm needs, and
    /// `18` §2.4's <c>SET_TARGET_PRIORITY</c> types it <see cref="double"/> — so a mid-fight write and
    /// an authored value are the same quantity. <see cref="BattleActor.TargetPriority"/> is the live
    /// one; this is its opening value.
    /// </remarks>
    public double TargetPriority { get; init; }

    /// <summary>`05` §6.2's elite modifier. Read by <c>TARGET_IS_ELITE</c>/<c>ATTACKER_IS_ELITE</c>.</summary>
    public bool IsElite { get; init; }

    /// <summary>`05` §6.3's boss — the actor the phase check watches.</summary>
    public bool IsBoss { get; init; }

    /// <summary>Whether a <c>SUMMON</c> op (`18` §2.4) spawned this actor.</summary>
    public bool IsSummon { get; init; }

    /// <summary>The <see cref="Id"/> of this actor's summoner — <c>OWNER</c>'s subject.</summary>
    public string? OwnerId { get; init; }
}
