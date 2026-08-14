namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// 🔒 <b>"The living actors hostile to the holder"</b>, stated once — the predicate behind `18` §4's
/// <c>ENEMY_COUNT</c> and every one of `18` §5's five enemy tokens.
/// </summary>
/// <remarks>
/// <para>
/// It is a type rather than a convention because the same sentence was written twice before it was
/// written here — once in the condition evaluator to count, once in the target resolver to select —
/// and nothing compared the two. If they ever drifted, <c>ENEMY_COUNT</c> and <c>ALL_ENEMIES</c>
/// would disagree about the same battle, which is the worst possible split: `18` §7.10-style content
/// gates one on the other, so the disagreement would surface as a perk that fires against a count of
/// enemies it then fails to hit. M2-03's ops and M2-08's tick loop need the same predicate a third
/// and fourth time.
/// </para>
/// <para>
/// 🔒 <b>Three rules, all cited, all in one place:</b>
/// </para>
/// <list type="bullet">
///   <item><b>Holder-relative.</b> "Hostile" means <em>the side opposite the holder's</em>. `18` §5
///   never says whose enemies its tokens mean; §7.10 settles it by worked example, authoring the
///   Volatile elite's death explosion — <em>"explodes on death for 15% of <b>hero</b> Max HP"</em> —
///   as <c>target: "ALL_ENEMIES"</c> on an <b>enemy</b> actor.</item>
///   <item><b>Living only.</b> `05` §3.1 step 6: <em>"An actor whose HP reaches 0 stops acting and
///   being targetable at that moment."</em></item>
///   <item><b>Never a pet.</b> `05` §3.2: <em>"Pets cannot be targeted or killed."</em></item>
/// </list>
/// <para>
/// 🔒 <b>Pure, and held to a condition's standard of purity.</b> It draws nothing and caches nothing.
/// <c>ConditionPurityRuleTests</c> governs it by name alongside <c>Rules/Effects/Conditions/</c>,
/// because <c>ConditionEvaluator</c> reaches it — and a rule that scanned only the condition namespace
/// would be defeated by one hop through anything the evaluator calls.
/// </para>
/// </remarks>
internal static class BattleRoster
{
    /// <summary>
    /// The living non-pet actors on the side opposite the holder's, in ascending `05` §3.1 actor
    /// index, optionally excluding one actor by index.
    /// </summary>
    /// <param name="context">The evaluation context. Its holder fixes which side is hostile.</param>
    /// <param name="excludeIndex">
    /// The `05` §3.1 index to leave out — <c>OTHER_ENEMIES</c>' primary target (`18` §5). <c>null</c>
    /// excludes nobody.
    /// </param>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The result is ordered here, not assumed to arrive ordered.</b> A caller cannot change a
    /// target selection by reordering the roster it hands in: `18` §5's <c>LOWEST_HP_ENEMY</c>
    /// tie-break and <c>RANDOM_ENEMY</c>'s candidate order both key on this order, so leaving it to
    /// the caller would make a battle's outcome depend on how the simulator happened to build a list.
    /// </para>
    /// <para>
    /// A stable insertion rather than a sort: `05` §3 caps a battle at one hero, three pets and five
    /// enemies, so this runs over at most nine actors and allocates one list, where
    /// <c>Where().OrderBy().ToList()</c> allocates seven objects per call — several times per actor
    /// per tick at 20 Hz.
    /// </para>
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

    /// <summary>
    /// The holder's own side's living pets, in ascending `05` §3.1 slot order — what <c>ALL_PETS</c>
    /// names.
    /// </summary>
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

    /// <summary>Places an actor at its position in ascending `05` §3.1 index order.</summary>
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
