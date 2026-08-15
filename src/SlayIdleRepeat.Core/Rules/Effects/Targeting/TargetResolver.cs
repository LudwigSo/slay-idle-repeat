using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Targeting;

/// <summary>
/// The eleven target tokens, resolved to the actors they name.
/// </summary>
/// <remarks>
/// Every token is relative to the effect's holder. The liveness filter governs selection, not
/// naming: the six set tokens (the five enemy tokens and <c>ALL_PETS</c>) filter to living actors,
/// but the four naming tokens (<c>SELF</c>, <c>CURRENT_TARGET</c>, <c>ATTACKER</c>, <c>OWNER</c>)
/// each name one actor the caller already holds and are never filtered — an <c>ON_DEATH</c> effect
/// targeting <c>SELF</c>, or a reaction against an attacker that fell in the same tick, depends on
/// that. Pets are never in an enemy set. Nothing here caches: a memoised candidate list goes stale on
/// the very next death.
/// </remarks>
internal static class TargetResolver
{
    /// <summary>
    /// The actors a token names, in fixed actor-index order.
    /// </summary>
    /// <returns>
    /// The selected actors, possibly empty. An empty result means the set is genuinely empty — the
    /// last enemy died, the side holds no pet, or the <c>OWNER</c> skip applied. It never means "the
    /// token could not be answered": that throws.
    /// </returns>
    /// <exception cref="EffectContextException">
    /// The context does not carry the token's subject. See that type for the uniform rule.
    /// </exception>
    internal static IReadOnlyList<IEffectActorView> Resolve(
        EffectTarget target,
        EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return target switch
        {
            EffectTarget.SELF => Only(context.Holder),

            EffectTarget.CURRENT_TARGET => Only(
                context.CurrentTarget ?? EnemyHolderHero(context) ?? throw new EffectContextException(
                    nameof(EffectTarget.CURRENT_TARGET),
                    "the context carries no current target",
                    "18 §5 authorises a degradation for OTHER_ENEMIES and a skip for OWNER, and " +
                    "nothing for this token on a HERO holder. Steering S6: an empty set here would be " +
                    "a hit that landed on nobody, indistinguishable from a battle whose enemies are " +
                    "all dead. (An ENEMY holder never reaches this throw — see EnemyHolderHero.)")),

            EffectTarget.ATTACKER => Only(
                context.Attacker ?? throw new EffectContextException(
                    nameof(EffectTarget.ATTACKER),
                    "the context carries no attacker",
                    "18 §5 authorises no default for this token. (18 §4's ATTACKER_IS_* trio is a " +
                    "different clause with a different answer — it authors 'false elsewhere', and " +
                    "the evaluator honours that.)")),

            EffectTarget.OWNER => Owner(context),

            EffectTarget.ALL_ENEMIES => LivingEnemies(context),
            EffectTarget.OTHER_ENEMIES => OtherEnemies(context),
            EffectTarget.LOWEST_HP_ENEMY => ByCurrentHp(context, lowest: true),
            EffectTarget.HIGHEST_HP_ENEMY => ByCurrentHp(context, lowest: false),
            EffectTarget.RANDOM_ENEMY => RandomEnemy(context),

            EffectTarget.ALL_PETS => Pets(context),

            // Declared, not wired: run/board ops are resolved by a run controller, never by the
            // combat simulator, so a placeholder resolver here would be a guess.
            EffectTarget.RUN => throw new EffectContextException(
                nameof(EffectTarget.RUN),
                "the combat simulator resolves no run or board op",
                "18 §2.5: run and board ops 'are resolved by the run controller, never by the combat " +
                "simulator' — the simulator appends a RunEffectQueued event to the combat log (05 §7) " +
                "and the controller applies the queue when the battle resolves. That controller is " +
                "M3's, over M1-05's Run aggregate. A placeholder resolver here would be a guess at " +
                "both, and an empty set would read as 'this resolved to nobody'."),

            _ => throw new EffectContextException(
                target.ToString(),
                "it is not one of 18 §5's eleven tokens",
                "18 §11: '11 targets = 9 + OTHER_ENEMIES + OWNER'."),
        };
    }

    /// <summary>The living non-pets on the side opposite the holder's.</summary>
    private static IReadOnlyList<IEffectActorView> LivingEnemies(EffectEvaluationContext context) =>
        BattleRoster.LivingEnemies(context);

    /// <summary>
    /// The hero opposite an <c>ENEMY</c> holder, used as a <c>CURRENT_TARGET</c> fallback when the
    /// context carries none (e.g. a boss <c>PERIODIC</c> mechanic with no attack in flight).
    /// </summary>
    /// <remarks>
    /// Enemies always target the hero, and a battle seats exactly one, so an <c>ENEMY</c> holder's
    /// target is unambiguous even with no attack in flight — unlike a <c>HERO</c> holder, whose
    /// target-priority machinery can vary hit to hit, so that case still falls through to the general
    /// throw unchanged. Returns exactly one actor, never a set, since <c>STAT_COPY</c> reads its
    /// target as a single actor. Returns <c>null</c> when the roster carries no opposite-side hero at
    /// all — a malformed context rather than a real battle.
    /// </remarks>
    private static IEffectActorView? EnemyHolderHero(EffectEvaluationContext context)
    {
        if (context.Holder.Kind != EffectActorKind.ENEMY)
        {
            return null;
        }

        var side = context.Holder.Side;

        foreach (var actor in context.Actors)
        {
            if (actor.Kind == EffectActorKind.HERO && actor.Side != side)
            {
                return actor;
            }
        }

        return null;
    }

    /// <summary>
    /// All enemies except the attack's primary target. Degrades to <c>ALL_ENEMIES</c> outside an
    /// attack context, since with no primary there's nothing to except.
    /// </summary>
    /// <remarks>
    /// Excludes by <see cref="IEffectActorView.Index"/>, not <see cref="IEffectActorView.Id"/> or
    /// reference equality: index is unique per battle by construction, several units can share one id
    /// when spawned from the same archetype draw, and an actor view may use value equality.
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> OtherEnemies(EffectEvaluationContext context) =>
        BattleRoster.LivingEnemies(context, context.CurrentTarget?.Index);

    /// <summary>
    /// The living enemy with the lowest or highest current HP.
    /// </summary>
    /// <remarks>
    /// Current HP, never a fraction — a 400/2000 tank and a 40/40 runt rank oppositely under the two
    /// readings. Ties break by ascending actor index: candidates arrive in that order, and only a
    /// strict improvement displaces the incumbent.
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> ByCurrentHp(EffectEvaluationContext context, bool lowest)
    {
        var candidates = LivingEnemies(context);

        if (candidates.Count == 0)
        {
            return candidates;
        }

        var chosen = candidates[0];

        for (var i = 1; i < candidates.Count; i++)
        {
            var challenger = candidates[i];
            var better = lowest
                ? challenger.CurrentHp < chosen.CurrentHp
                : challenger.CurrentHp > chosen.CurrentHp;

            if (better)
            {
                chosen = challenger;
            }
        }

        return Only(chosen);
    }

    /// <summary>
    /// One living enemy, drawn from the battle's deterministic RNG stream.
    /// </summary>
    /// <remarks>
    /// Draws over the living enemies in actor-index order, consuming exactly one draw — the same
    /// seed must pick the same actor on client and server, and index order is the only order both are
    /// guaranteed to build the same way. The stream is required before candidates are counted, so a
    /// context built without one fails on the first use of this token rather than only when an enemy
    /// happens to be alive.
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> RandomEnemy(EffectEvaluationContext context)
    {
        var rng = context.Rng ?? throw new EffectContextException(
            nameof(EffectTarget.RANDOM_ENEMY),
            "the context carries no draw stream",
            "14 §8.1: a combat draw is new DeterministicRng(battleSeed, RngStreams.Combat), and the " +
            "battleSeed is handed in by the simulator (SeedDerivation.BattleSeed) rather than derived " +
            "here. Falling back to the first candidate would be a stable, reproducible, wrong 'random'.");

        var candidates = LivingEnemies(context);

        // No candidate, no draw: a rejected call must not consume RNG position, or every later draw
        // in the battle would desync between client and server.
        return candidates.Count == 0
            ? candidates
            : Only(candidates[rng.Range(0, candidates.Count)]);
    }

    /// <summary>
    /// The holder's own side's living pets, in slot order — never the opposing side's.
    /// </summary>
    /// <remarks>
    /// An empty set on a side that holds no pet, never a failure — the target has no authored
    /// semantics for a side that can't hold pets, and throwing would crash any boss effect using it.
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> Pets(EffectEvaluationContext context) =>
        BattleRoster.Pets(context);

    /// <summary>
    /// The summoner of the source actor. On an actor that is not a summon, the effect is skipped
    /// (an empty set).
    /// </summary>
    /// <remarks>
    /// A summon whose summoner has already left the roster is skipped the same way — a summoner
    /// dying must not turn every summon's effect into a crash.
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> Owner(EffectEvaluationContext context)
    {
        if (!context.Holder.IsSummon)
        {
            return Array.Empty<IEffectActorView>();
        }

        // A summon that records no summoner is a malformed actor view, not the authored skip:
        // OwnerId documents null as "not a summon", which this actor claims not to be.
        var ownerId = context.Holder.OwnerId ?? throw new EffectContextException(
            nameof(EffectTarget.OWNER),
            $"'{context.Holder.Id}' is flagged a summon and records no summoner",
            "18 §5's skip is authored for an actor that is NOT a summon.");

        foreach (var actor in context.Actors)
        {
            if (string.Equals(actor.Id, ownerId, StringComparison.Ordinal))
            {
                return Only(actor);
            }
        }

        return Array.Empty<IEffectActorView>();
    }

    /// <summary>The one actor a naming token resolved to.</summary>
    /// <remarks>
    /// Built as <c>new[] { actor }</c> rather than the <c>[actor]</c> collection expression: for a
    /// single-element <see cref="IReadOnlyList{T}"/>, the compiler synthesizes an undocumented
    /// global-namespace type that trips this project's namespace-boundary test. An array allocation
    /// is the same cost and leaves no type behind.
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> Only(IEffectActorView actor) => new[] { actor };
}
