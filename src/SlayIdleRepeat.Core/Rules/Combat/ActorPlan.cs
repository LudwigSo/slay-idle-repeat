using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// One effect an actor brings into a battle, and the <c>EffectInstanceId</c> it holds it under.
/// </summary>
/// <param name="Effect">The authored effect.</param>
/// <param name="InstanceId">
/// The instance id, or <c>null</c> to have the simulator mint a battle-local one. Every live copy of
/// an effect needs a distinct id, and an <c>ON_KILL</c> id must stay stable across battles because its
/// counter belongs to the run — so a hero-side holding (a perk, a gear affix) passes the id the run
/// layer owns, while a boss, elite or summon effect passes <c>null</c>. A <c>null</c> on an
/// <c>ON_KILL</c> effect is refused (<see cref="BattlePlan"/>): a minted id is battle-local, so
/// accepting one would reset an on-kill counter every fight while still producing a legal-looking log.
/// </param>
internal readonly record struct HeldEffect(EffectDefinition Effect, EffectInstanceId? InstanceId = null);

/// <summary>
/// One actor as it enters a fight: its stat block, level, effect holdings and the facts combat
/// conditions read about it.
/// </summary>
/// <remarks>
/// <para>
/// Not named <c>*Snapshot</c>: that suffix belongs to the persistence contract's committed field
/// order, and nothing here is persisted state.
/// </para>
/// <para>
/// It is a plan, not the actor. Nothing here is mutable and nothing changes during a fight;
/// <see cref="BattleActor"/> is the live state and holds a reference back to this. That split is what
/// makes a visual battle a replay of a pre-computed log checkable — the inputs to a fight are a value,
/// so re-running one is re-running the same value.
/// </para>
/// </remarks>
internal sealed record ActorPlan
{
    /// <summary>The actor's stable identity — what <c>OWNER</c> matches on. Unique within one roster.</summary>
    public required string Id { get; init; }

    /// <summary>The fixed actor index: hero, pets in slot order, enemies by index. The loop's initiative order.</summary>
    public required int Index { get; init; }

    /// <summary>The combat-log id — <c>CombatActor.Hero</c>, <c>Pet(slot)</c> or <c>Enemy(index)</c>.</summary>
    public required byte LogId { get; init; }

    /// <summary>Which side.</summary>
    public required BattleSide Side { get; init; }

    /// <summary>Hero, pet or enemy.</summary>
    public required EffectActorKind Kind { get; init; }

    /// <summary>The fourteen combat stats, before effect aggregation — the hero curve or the enemy derivation.</summary>
    /// <remarks>
    /// A pet has one and it is never read for a basic attack: pets have no ATK/ASPD of their own, but a
    /// pet can still carry aura effects a <c>STAT_COPY</c> may read, and there is no partial stat form.
    /// </remarks>
    public required ActorStats BaseStats { get; init; }

    /// <summary>
    /// The actor's level — the attacker-level term of the mitigation curve. The hero's Legend Level, or
    /// the shared enemy level for a (chapter, tier).
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// The effects this actor holds — gear, affixes, talents, auras, perks, boss mechanics. Order is
    /// immaterial; every step that reads them imposes the ordinal effect-id order.
    /// </summary>
    public IReadOnlyList<HeldEffect> Effects { get; init; } = Array.Empty<HeldEffect>();

    /// <summary>
    /// The hero's target priority: the hero targets the enemy with the highest one, ties broken by
    /// lowest current HP. Default <c>0</c>; <c>-1</c> deprioritises and <c>+1</c> forces focus.
    /// </summary>
    /// <remarks><see cref="BattleActor.TargetPriority"/> is the live one; this is its opening value.</remarks>
    public double TargetPriority { get; init; }

    /// <summary>The elite modifier. Read by <c>TARGET_IS_ELITE</c>/<c>ATTACKER_IS_ELITE</c>.</summary>
    public bool IsElite { get; init; }

    /// <summary>Whether this actor is the boss the phase check watches.</summary>
    public bool IsBoss { get; init; }

    /// <summary>Whether a <c>SUMMON</c> op spawned this actor.</summary>
    public bool IsSummon { get; init; }

    /// <summary>The <see cref="Id"/> of this actor's summoner — <c>OWNER</c>'s subject.</summary>
    public string? OwnerId { get; init; }

    /// <summary>
    /// The health this actor enters the fight on, or <see langword="null"/> for its full aggregated
    /// Max HP.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Full is the aggregated maximum, not <see cref="BaseStats"/>'s.</b> Those were the same
    /// number for as long as nothing standing modified Max HP, so an actor opening on its base block
    /// was invisible — and the moment a loadout's Max HP reached the aggregation, a fully-equipped
    /// hero began every fight on the fraction of a bar its base curve alone describes, and lost
    /// timeouts decided on HP fraction that its build wins.
    /// <para>
    /// 🔒 <b>A run's own persisted current HP DOES arrive here now, and the precondition this paragraph
    /// used to state is what made it possible.</b> It read: <em>"a fight's remaining health is not
    /// written back to the run, so opening at it would mean a hero who was wounded once stayed wounded
    /// for the rest of the run with no way to be hurt further. Both halves belong to whichever handler
    /// closes the loop."</em> M7-06d wrote the first half — <c>ConfirmBattleResult</c> stores the
    /// simulation's ending HP — and M7-06e wires this one, so the loop is closed and a run is an
    /// attrition rather than a series of independent encounters.
    /// </para>
    /// <para>
    /// ⚠️ Which means the hero is the one actor whose starting HP is routinely NOT full. Enemies still
    /// pass <see langword="null"/>: they are composed fresh per fight and have no persisted health to
    /// carry, so full is not a default for them so much as the only answer.
    /// </para>
    /// </remarks>
    public double? StartingHp { get; init; }
}
