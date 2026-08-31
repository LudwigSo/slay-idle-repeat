using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The state one condition evaluation or target resolution reads — assembled by the caller, never
/// gathered by the evaluator.
/// </summary>
/// <remarks>
/// Everything is relative to <see cref="Holder"/>: <c>SELF</c> is the holder, the enemy tokens are
/// the side opposite the holder's, <c>ALL_PETS</c> is the holder's side's pets, <c>OWNER</c> is the
/// holder's summoner. Nothing here is read from ambient state — no clock, no unseeded RNG — which is
/// what makes condition evaluation a pure function of passed-in state.
/// </remarks>
internal sealed record EffectEvaluationContext
{
    /// <summary>The actor holding the effect — the origin of every target token.</summary>
    public required IEffectActorView Holder { get; init; }

    /// <summary>The holder's current attack target. In a duel this is the opposing hero.</summary>
    /// <remarks>
    /// Its presence also marks an attack context, which gates <c>OTHER_ENEMIES</c>: outside an
    /// attack it degrades to <c>ALL_ENEMIES</c> rather than needing a separate flag that could
    /// disagree with this one.
    /// </remarks>
    public IEffectActorView? CurrentTarget { get; init; }

    /// <summary>The actor that dealt the hit being reacted to.</summary>
    /// <remarks>
    /// Null outside a hit-reaction context, where the <c>ATTACKER_IS_*</c> conditions read false by
    /// design. The <c>ATTACKER</c> target itself has no such default and throws instead.
    /// </remarks>
    public IEffectActorView? Attacker { get; init; }

    /// <summary>Every actor in the battle, both sides, living and dead.</summary>
    /// <remarks>
    /// Dead actors are included rather than pre-filtered so the "out of play at 0 HP" rule is applied
    /// once, here, rather than trusting every caller to filter consistently. Order is not guaranteed —
    /// each actor carries its own index, and <see cref="BattleRoster"/> imposes the order that
    /// <c>LOWEST_HP_ENEMY</c>'s tie-break and <c>RANDOM_ENEMY</c>'s candidate order depend on.
    /// </remarks>
    public required IReadOnlyList<IEffectActorView> Actors { get; init; }

    /// <summary><c>BATTLE_TIME</c> — seconds of battle time elapsed, advancing in 0.05s steps at 20 Hz.</summary>
    public double BattleTimeSeconds { get; init; }

    /// <summary>When the enrage begins, in battle-time seconds. Null in a fight with no enrage (only bosses have one).</summary>
    public double? EnrageAtSeconds { get; init; }

    /// <summary>When this fight is forced to end, in battle-time seconds (the fight timeout, or a duel's shorter cap).</summary>
    public required double FightHorizonSeconds { get; init; }

    /// <summary>
    /// <c>IS_PVP</c> — lets a perk behave differently in a duel; also the switch behind the duel's
    /// fixed target-elite/boss/count rules.
    /// </summary>
    public bool IsPvp { get; init; }

    /// <summary>The run's state, for the conditions that read it. Null where there is no run (a duel, or the balance harness).</summary>
    /// <remarks>
    /// A run condition evaluated against a null view throws rather than reading zero, so content that
    /// should have been skipped via <c>IS_PVP</c> but wasn't doesn't fail silently.
    /// </remarks>
    public IRunStateView? Run { get; init; }

    /// <summary>
    /// Whether this context supplies every subject the classified tree reads — the activation half
    /// of the conditional standing-effect bucket's subject-presence rule.
    /// </summary>
    /// <param name="subjects">The tree's classification, from <see cref="ConditionSubjects.Of"/>.</param>
    internal bool Carries(ConditionSubjects subjects) =>
        (!subjects.ReadsTarget || CurrentTarget is not null) &&
        (!subjects.ReadsAttacker || Attacker is not null);

    /// <summary><c>RANDOM_ENEMY</c>'s draw stream — the only target token that draws.</summary>
    /// <remarks>
    /// Handed in already seeded, never derived here — the run seed never leaves the server. Null
    /// outside a battle, where <c>RANDOM_ENEMY</c> throws rather than silently picking the first
    /// candidate.
    /// </remarks>
    public DeterministicRng? Rng { get; init; }
}
