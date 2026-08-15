namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>The living, non-pet actors on the side opposite the holder's.</summary>
/// <remarks>
/// Shared here so the condition evaluator (counting) and the target resolver (selecting) can't
/// drift on the same predicate. "Hostile" is holder-relative, not literally "the monsters" — in a
/// duel the opposing hero and its pets occupy this set. Dead actors and pets are always excluded.
/// </remarks>
internal static class BattleRoster
{
    /// <summary>
    /// The living non-pet actors on the side opposite the holder's, in ascending actor index,
    /// optionally excluding one actor by index.
    /// </summary>
    /// <param name="context">The evaluation context. Its holder fixes which side is hostile.</param>
    /// <param name="excludeIndex">Index to leave out (e.g. OTHER_ENEMIES' primary target). Null excludes nobody.</param>
    /// <remarks>
    /// Order matters here: LOWEST_HP_ENEMY's tie-break and RANDOM_ENEMY's candidate order both key on
    /// this list's order, so it's built consistently rather than left to the caller. A battle caps at
    /// nine actors, so a manual insertion sort avoids the allocations of Where().OrderBy().ToList()
    /// running several times per tick.
    /// </remarks>
    internal static List<IEffectActorView> LivingEnemies(
        EffectEvaluationContext context,
        int? excludeIndex = null)
    {
        var side = context.Holder.Side;
        var actors = context.Actors;
        var enemies = new List<IEffectActorView>(actors.Count);

        for (var i = 0; i < actors.Count; i++)
        {
            var actor = actors[i];

            if (!actor.IsAlive || actor.Kind == EffectActorKind.PET || actor.Side == side)
            {
                continue;
            }

            if (excludeIndex is { } excluded && actor.Index == excluded)
            {
                continue;
            }

            Insert(enemies, actor);
        }

        return enemies;
    }

    /// <summary>The holder's own side's living pets, in ascending slot order — what <c>ALL_PETS</c> names.</summary>
    internal static List<IEffectActorView> Pets(EffectEvaluationContext context)
    {
        var side = context.Holder.Side;
        var actors = context.Actors;
        var pets = new List<IEffectActorView>(actors.Count);

        for (var i = 0; i < actors.Count; i++)
        {
            var actor = actors[i];

            if (actor.IsAlive && actor.Kind == EffectActorKind.PET && actor.Side == side)
            {
                Insert(pets, actor);
            }
        }

        return pets;
    }

    /// <summary>Places an actor at its position in ascending index order.</summary>
    private static void Insert(List<IEffectActorView> ordered, IEffectActorView actor)
    {
        var at = ordered.Count;

        while (at > 0 && ordered[at - 1].Index > actor.Index)
        {
            at--;
        }

        ordered.Insert(at, actor);
    }
}
