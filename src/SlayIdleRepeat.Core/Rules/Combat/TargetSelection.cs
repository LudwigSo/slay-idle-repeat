using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>Who a swing lands on. The three selections the tick loop makes, and nothing else.</summary>
/// <remarks>
/// <para>
/// A different mechanism from the DSL's target tokens, deliberately: those are how an
/// <em>effect</em> chooses a subject, this is how an <em>actor</em> chooses whom to swing at, and only
/// this one reads <c>targetPriority</c>.
/// </para>
/// <para>
/// The two selections that scan the enemy list use <c>BattleRoster</c>'s predicate, not a restatement
/// of it: it applies all three rules — holder-relative, living only, never a pet — and returns
/// candidates in ascending index order, which is what makes every tie-break below total.
/// </para>
/// <para>
/// <see cref="ForEnemyAttack"/> is the exception, deliberately: enemies always target the Hero, so it
/// is a search for a single role rather than a scan of a set. It applies the same three rules inline;
/// what it must never do is apply a different one, which is why the liveness and pet clauses are
/// spelled out there rather than assumed.
/// </para>
/// <para>
/// Every selection ends at the index: two enemies at equal priority and equal HP are ordinary, and
/// index is the one actor ordering that stays deterministic regardless of roster construction.
/// </para>
/// </remarks>
internal static class TargetSelection
{
    /// <summary>The hero targets the enemy with the highest <c>targetPriority</c>, breaking ties by lowest current HP, then by lowest index.</summary>
    /// <param name="context">
    /// The evaluation context whose <c>Holder</c> is the attacker. Its side fixes which actors are
    /// hostile, so the same method serves an enemy attacking into a duel's defending side.
    /// </param>
    /// <returns>The target, or <c>null</c> when nothing hostile is alive.</returns>
    /// <remarks><c>targetPriority</c> defaults to <c>0</c>; <c>-1</c> deprioritises and <c>+1</c> forces focus.</remarks>
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

    /// <summary>A pet's targeted ability selects the enemy with the highest current HP, unless the ability specifies its own target.</summary>
    /// <param name="context">The evaluation context whose <c>Holder</c> is the pet.</param>
    /// <returns>The target, or <c>null</c> when nothing hostile is alive.</returns>
    /// <remarks>
    /// It reads no <c>targetPriority</c> at all: that field belongs to the hero's selection only, and
    /// applying it here would make a deprioritised enemy invisible to pets too — defeating the
    /// mechanic's own purpose.
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

    /// <summary>Enemies always target the Hero.</summary>
    /// <param name="attacker">The enemy swinging.</param>
    /// <param name="actors">The whole roster, both sides.</param>
    /// <returns>The living hero on the opposing side, or <c>null</c> when it is dead.</returns>
    /// <remarks>
    /// Stated as a search for the opposing hero, not as "index 0": a duel has a hero on each side and
    /// the defending one is not at index 0, so an enemy-side actor reading index 0 would swing at its
    /// own ally. Pets are excluded by the same clause that excludes them everywhere.
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

    /// <summary>The roster's own actor type, which is the only implementation a battle ever holds — forwarded to <see cref="BattleActor.Of"/>.</summary>
    private static BattleActor AsBattleActor(IEffectActorView view) => BattleActor.Of(view);
}
