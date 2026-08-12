using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` §3.2 — who a swing lands on. The three selections the tick loop makes, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>A different mechanism from `18` §5's target tokens, deliberately.</b>
/// <see cref="IEffectActorView"/> is explicit: <em>"`05` §3.2's hero target selection is M2-08's, and
/// is a different mechanism from <c>LOWEST_HP_ENEMY</c>/<c>HIGHEST_HP_ENEMY</c>"</em>. `18` §5's
/// tokens are how an <em>effect</em> chooses a subject; this is how an <em>actor</em> chooses whom to
/// swing at, and only this one reads <c>targetPriority</c>.
/// </para>
/// <para>
/// 🔒 <b>The roster predicate is <c>BattleRoster</c>'s, not restated here.</b> That type exists
/// because "the living actors hostile to the holder" had already been written twice, and its remarks
/// name this task as the fourth caller. It applies all three rules — holder-relative, living only,
/// never a pet — and returns candidates in ascending `05` §3.1 index order, which is what makes every
/// tie-break below total.
/// </para>
/// <para>
/// 🔒 <b>Every selection ends at the index, and that is not an invention.</b> `05` §3.2 gives one
/// tie-break for the hero (lowest current HP) and none for a pet ability, and two enemies at equal
/// priority and equal HP are ordinary — a <c>SWARM</c> draw spawns three identical units (`05` §6.4).
/// <see cref="IEffectActorView.Index"/> is the one actor ordering the design documents authorise, and
/// `18` §5 already reuses it as its tie-break for the same reason: a tie-break that varied with
/// roster construction would be a `14` §8.2 determinism break.
/// </para>
/// </remarks>
internal static class TargetSelection
{
    /// <summary>
    /// 🔒 `05` §3.2 — <em>"the hero targets the enemy with the highest <c>targetPriority</c>, breaking
    /// ties by <b>lowest current HP</b>"</em>, and then by lowest index.
    /// </summary>
    /// <param name="context">
    /// The evaluation context whose <c>Holder</c> is the attacker. Its side fixes which actors are
    /// hostile, so the same method serves an enemy attacking into a duel's defending side.
    /// </param>
    /// <returns>The target, or <c>null</c> when nothing hostile is alive.</returns>
    /// <remarks>
    /// <c>targetPriority</c> defaults to <c>0</c>; <c>-1</c> deprioritises — Sporequeen Vell's
    /// sporelings (`17` §8), so the hero keeps hitting the queen while they are up — and <c>+1</c>
    /// forces focus.
    /// </remarks>
    internal static BattleActor? ForBasicAttack(EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        BattleActor? best = null;

        foreach (var candidate in BattleRoster.LivingEnemies(context))
        {
            var actor = AsBattleActor(candidate);

            if (best is null || Beats(actor, best))
            {
                best = actor;
            }
        }

        return best;

        // Higher priority wins; then lower current HP; then lower index. The candidates arrive in
        // ascending index order, so a strict comparison on the first two keys leaves the earliest
        // index in place and the third key needs no clause of its own.
        static bool Beats(BattleActor candidate, BattleActor incumbent) =>
            candidate.TargetPriority > incumbent.TargetPriority ||
            (candidate.TargetPriority == incumbent.TargetPriority &&
             candidate.CurrentHp < incumbent.CurrentHp);
    }

    /// <summary>
    /// 🔒 `05` §3.2 — a pet's <em>targeted ability</em> selects <em>"the enemy with the <b>highest
    /// current HP</b> (so pet abilities chip the tanky one while the hero cleans up)"</em>, unless the
    /// ability specifies its own target (`18` §5).
    /// </summary>
    /// <param name="context">The evaluation context whose <c>Holder</c> is the pet.</param>
    /// <returns>The target, or <c>null</c> when nothing hostile is alive.</returns>
    /// <remarks>
    /// 🔒 It reads no <c>targetPriority</c> at all. `05` §3.2 gives that field to the hero's selection
    /// only, and applying it here would make Sporequeen's <c>-1</c> sporelings invisible to the pets
    /// as well — which would defeat the mechanic's own purpose, since the sporelings exist to be
    /// cleared by something other than the hero's swing.
    /// </remarks>
    internal static BattleActor? ForPetAbility(EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        BattleActor? best = null;

        foreach (var candidate in BattleRoster.LivingEnemies(context))
        {
            var actor = AsBattleActor(candidate);

            if (best is null || actor.CurrentHp > best.CurrentHp)
            {
                best = actor;
            }
        }

        return best;
    }

    /// <summary>
    /// 🔒 `05` §3.2 — <em>"enemies always target the Hero."</em>
    /// </summary>
    /// <param name="attacker">The enemy swinging.</param>
    /// <param name="actors">The whole roster, both sides.</param>
    /// <returns>The living hero on the opposing side, or <c>null</c> when it is dead.</returns>
    /// <remarks>
    /// 🔒 <b>Stated as a search for the opposing <c>HERO</c>, not as "index 0".</b> `05` §3.3's duel
    /// has a hero on each side and the defending one is at <c>CombatActor.FirstEnemy</c>, so an
    /// enemy-side actor reading index 0 would swing at its own ally. Pets are excluded by the same
    /// clause that excludes them everywhere: <em>"pets cannot be targeted or killed"</em>.
    /// </remarks>
    internal static BattleActor? ForEnemyAttack(BattleActor attacker, IReadOnlyList<BattleActor> actors)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(actors);

        foreach (var candidate in actors)
        {
            if (candidate.Side != attacker.Side &&
                candidate.Kind == EffectActorKind.HERO &&
                candidate.IsAlive)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The roster's own actor type, which is the only implementation a battle ever holds.
    /// </summary>
    /// <remarks>
    /// 🔒 A cast with a message rather than a silent one. <c>BattleRoster</c> is typed over
    /// <see cref="IEffectActorView"/> because `18` §4 and §5 are, but a battle's roster is built by
    /// <see cref="BattleSimulation"/> out of <see cref="BattleActor"/>s exclusively — see
    /// <see cref="IEffectActorView"/>'s <em>"two views of one battle are two chances to disagree"</em>.
    /// A foreign view reaching here is a second roster, which is the defect that remark is about.
    /// </remarks>
    private static BattleActor AsBattleActor(IEffectActorView view) =>
        view as BattleActor ??
        throw new InvalidOperationException(
            $"`05` §3.2's target selection was handed a {view.GetType().Name} rather than a " +
            $"{nameof(BattleActor)}. A battle has one roster and one view of it; a second implementation " +
            "of IEffectActorView in the candidate list means two rosters exist, which is exactly how " +
            "ENEMY_COUNT and ALL_ENEMIES come to disagree about the same fight.");
}
