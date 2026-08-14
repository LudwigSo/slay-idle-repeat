using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Targeting;

/// <summary>
/// 🔒 `18` §5 — the eleven target tokens, resolved to the actors they name.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every token is relative to the effect's holder.</b> See
/// <see cref="EffectEvaluationContext.Holder"/> for `18` §7.10's Volatile worked example, which is
/// what settles it.
/// </para>
/// <para>
/// 🔒 <b>The liveness filter governs selection, not naming.</b> `05` §3.1 step 6 — <em>"An actor
/// whose HP reaches 0 stops acting and being <b>targetable</b> at that moment"</em> — is a rule about
/// who may be picked out of a set. The six <b>set</b> tokens — the five enemy tokens
/// (<c>ALL_ENEMIES</c>, <c>OTHER_ENEMIES</c>, <c>LOWEST_HP_ENEMY</c>, <c>HIGHEST_HP_ENEMY</c>,
/// <c>RANDOM_ENEMY</c>) and <c>ALL_PETS</c> — pick, and therefore filter. The four <b>naming</b> tokens
/// (<c>SELF</c>, <c>CURRENT_TARGET</c>, <c>ATTACKER</c>, <c>OWNER</c>) pick nothing: each names one
/// actor the caller already holds. Filtering those would break the cases that matter most — an
/// <c>ON_DEATH</c> effect targeting <c>SELF</c> (`18` §7.10's Volatile explodes <em>because</em> it
/// died), and thorns or an <c>ON_HIT_TAKEN</c> reaction against an attacker that fell in the same
/// tick.
/// </para>
/// <para>
/// 🔒 <b>Pets are never in an enemy set.</b> `05` §3.2: <em>"Pets cannot be targeted or killed."</em>
/// </para>
/// <para>
/// 🔒 <b>Nothing here caches.</b> A memoised candidate list is wrong on the very next death, and
/// wrong identically on client and server, so `14` §8.2's determinism job would not catch it either.
/// <c>ConditionPurityRuleTests.The_18_5_target_resolver_holds_no_writable_static_state</c> is what
/// stops it.
/// </para>
/// </remarks>
internal static class TargetResolver
{
    /// <summary>
    /// The actors a `18` §5 token names, in `05` §3.1's fixed actor-index order.
    /// </summary>
    /// <returns>
    /// The selected actors, possibly empty. An empty result means the set is genuinely empty — the
    /// last enemy died, the side holds no pet, or `18` §5's authored <c>OWNER</c> skip applied. It
    /// never means "the token could not be answered": that throws.
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

            // 🔒 Declared, not wired — and that is the correct end state, not an omission.
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

    /// <summary>
    /// 🔒 The living non-pets on the side opposite the holder's — `18` §7.10's R10 reading, read
    /// through the one predicate <see cref="BattleRoster"/> states for the whole DSL.
    /// </summary>
    private static IReadOnlyList<IEffectActorView> LivingEnemies(EffectEvaluationContext context) =>
        BattleRoster.LivingEnemies(context);

    /// <summary>
    /// 🔒 M2-R3 — <c>CURRENT_TARGET</c> on an <c>ENEMY</c> holder, when the context carries no
    /// current target at all (a boss <c>PERIODIC</c> mechanic, which has no attack context to carry
    /// one — see <c>BattleSimulation.ContextFor</c>'s slot-3 call, <c>target: null</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The ruling, and why it narrows `18` §5's uniform "absent subject throws" rule for
    /// exactly this one token on exactly this one holder kind.</b> `05` §3.2 states, as a rule and
    /// not merely an observation: <em>"Enemies always target the Hero."</em> A <c>HERO</c> holder's
    /// <c>CURRENT_TARGET</c> is genuinely ambiguous outside an attack context — `05` §3.2's own
    /// target-priority machinery lets the hero's basic attack (and therefore its primary target) vary
    /// hit to hit among several living enemies, so a <c>HERO</c>-holder <c>CURRENT_TARGET</c> with no
    /// attack in flight really does have no answer, and the general throw stands for it unchanged. An
    /// <c>ENEMY</c> holder has no such freedom: `05` §3's roster is one hero (plus pets, which `05`
    /// §3.2 makes untargetable), so an enemy's target is the same single actor whether or not an
    /// attack happens to be in flight at the moment its <c>PERIODIC</c> fires. Filling that one case
    /// is not steering S6's forbidden "plausible default" — the value is not a guess standing in for
    /// an unknown, it is the only value `05` §3.2 permits.
    /// </para>
    /// <para>
    /// 🔒 <b>The eight faulting boss effects this closes.</b> `17`'s Cindermaw Magma Vent/Vent
    /// Refresh (P2 and P3), Rimehold's Collapse, Cogitator Prime's Recalibrate and Piston Slam, and
    /// the Dicelord's All In are all <c>PERIODIC</c> effects on a boss holder targeting
    /// <c>CURRENT_TARGET</c> — no attack is in flight when a periodic timer fires, so
    /// <see cref="EffectEvaluationContext.CurrentTarget"/> is <c>null</c> and every one of them threw
    /// before this fix.
    /// </para>
    /// <para>
    /// ⚠️ <b>Returns exactly one actor, not a set — <c>STAT_COPY</c> depends on it.</b> R13 makes
    /// <c>STAT_COPY</c>'s <c>target</c> name the copy SOURCE, and the op reads it as a single actor.
    /// This helper is called from inside <see cref="Only"/>'s argument position exactly like every
    /// other naming token, so Cogitator's Recalibrate (a <c>STAT_COPY</c> targeting
    /// <c>CURRENT_TARGET</c>) still resolves to one actor — the hero — never a set containing it.
    /// </para>
    /// <para>
    /// <c>null</c> when the roster carries no hero on the opposite side — a synthetic or malformed
    /// context, not a real battle (`05` §3 always seats exactly one hero). Falling through to the
    /// ordinary throw below is the honest answer for that case, rather than a second, differently
    /// worded one.
    /// </para>
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
    /// `18` §5 — <em>"all enemies except the attack's primary target … Valid only inside an attack
    /// context; elsewhere it degrades to <c>ALL_ENEMIES</c>."</em>
    /// </summary>
    /// <remarks>
    /// <para>
    /// One of the two degradations the document actually authors, and the reason no primary target
    /// means <c>ALL_ENEMIES</c> rather than a failure: with no primary there is nothing to except.
    /// </para>
    /// <para>
    /// 🔒 <b>The primary is excluded by <see cref="IEffectActorView.Index"/>, not by
    /// <see cref="IEffectActorView.Id"/>.</b> `05` §3.1 authorises the index as the actor's position
    /// in a fixed per-battle order, so it is unique by construction; nothing in `05` or `18` promises
    /// the same of an id, and `05` §6.4 spawns several units from one archetype draw. If a roster
    /// ever mints ids from content ids, an id-based exclusion would silently drop <b>every</b> swarm
    /// unit from <c>PK_CLEAVE</c>'s splash instead of only the primary. (Reference equality is not an
    /// option either: an actor view may be a record, whose <c>==</c> is value equality, so two
    /// identical-looking enemies would compare equal.)
    /// </para>
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> OtherEnemies(EffectEvaluationContext context) =>
        BattleRoster.LivingEnemies(context, context.CurrentTarget?.Index);

    /// <summary>
    /// The living enemy with the lowest or highest <b>current HP</b> — `05` §3.2: <em>"a pet's
    /// targeted ability selects the enemy with the highest current HP"</em>.
    /// </summary>
    /// <remarks>
    /// 🔒 Current HP, never a fraction: a 400/2000 tank has more HP left and a smaller remaining
    /// fraction than a 40/40 runt, and the two readings answer this in opposite directions.
    /// <para>
    /// 🔒 Ties break by ascending `05` §3.1 actor index. `18` §5 authors no tie-break, and steering S6
    /// forbids inventing one — so the tie falls to the one actor ordering the documents do authorise,
    /// reused rather than invented. The strict comparison below is what implements it: the candidates
    /// arrive in index order and only a strict improvement displaces the incumbent.
    /// </para>
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
    /// One living enemy, drawn from the battle's combat stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The protocol:</b> <c>candidates[rng.Range(0, candidates.Count)]</c> over the living
    /// enemies in `05` §3.1 index order, consuming exactly one draw index. The candidate order is
    /// load-bearing — the same seed must pick the same actor on the client and the server, and index
    /// order is the only order both are guaranteed to build.
    /// </para>
    /// <para>
    /// 🔒 <b>The stream is required before the candidates are counted</b>, so a context assembled
    /// without one fails on the first effect that names this token rather than on the first one that
    /// happens to find an enemy alive. `14` §8.1's <c>battleSeed</c> is the caller's to supply, and a
    /// silent fallback to <c>candidates[0]</c> would be a "random" pick that is perfectly stable and
    /// wrong.
    /// </para>
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

        // 🔒 No candidate, no draw. DeterministicRng's own contract is that a rejected call is not a
        // call, because Position IS the persisted state of the stream: a draw spent here would shift
        // every later draw of the battle and desynchronise the client from the server.
        return candidates.Count == 0
            ? candidates
            : Only(candidates[rng.Range(0, candidates.Count)]);
    }

    /// <summary>
    /// The holder's own side's living pets, in `05` §3.1 slot order — <c>PK_PACK_LEADER</c> buffs its
    /// own pets, never the opposing hero's (`05` §3.3 puts pets on both sides of a duel).
    /// </summary>
    /// <remarks>
    /// ⚠️ An empty set on a side that holds no pet, never a failure. The tempting alternative — throw
    /// on a PvE enemy side, because a side with no hero cannot hold pets — rests on an inference no
    /// document makes: `18` §5 names <c>ALL_PETS</c> once, in its token list, with no semantics at
    /// all. Steering S6 forbids filling that hole, and the throw would crash a battle over any boss
    /// effect authored with this target.
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> Pets(EffectEvaluationContext context) =>
        BattleRoster.Pets(context);

    /// <summary>
    /// `18` §5 — <em>"the summoner of the source actor (a sporeling's owner is Sporequeen). On an
    /// actor that is not a summon, the effect is <b>skipped</b>."</em>
    /// </summary>
    /// <remarks>
    /// 🔒 The second of the document's two authored degradations, and deliberately a different failure
    /// mode from <c>OTHER_ENEMIES</c>'s: that one degrades to another token, this one yields nobody.
    /// A summon whose summoner has already left the roster is skipped the same way — Sporequeen dying
    /// must not turn every sporeling's effect into a crash.
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> Owner(EffectEvaluationContext context)
    {
        if (!context.Holder.IsSummon)
        {
            return Array.Empty<IEffectActorView>();
        }

        // 🔒 A summon that records no summoner is a malformed actor view, not `18` §5's authored
        // skip: IEffectActorView.OwnerId documents null as "an actor that was not summoned", which
        // this actor claims not to be. Folding the two into one empty set would spell a roster-
        // construction bug exactly like a documented no-op (steering S2/S6).
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
    /// ⚠️ <b><c>new[] { … }</c> rather than the collection expression <c>[actor]</c>, and it is not a
    /// style choice.</b> For a single element targeting <see cref="IReadOnlyList{T}"/>, Roslyn
    /// synthesises <c>&lt;&gt;z__ReadOnlySingleElementList</c> in the <b>global</b> namespace with no
    /// <c>CompilerGeneratedAttribute</c>, and
    /// <c>AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace</c> — which
    /// filters on that attribute — then reports it as an undocumented `30` §11.4 namespace. M2-01 hit
    /// this in <see cref="Content.Effects.EffectCondition"/> and recorded the same note there; this
    /// helper is why six call sites do not each have to remember it. An array allocation is the same
    /// cost and leaves no type behind.
    /// </remarks>
    private static IReadOnlyList<IEffectActorView> Only(IEffectActorView actor) => new[] { actor };
}
