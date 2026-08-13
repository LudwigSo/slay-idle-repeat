using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// 🔒 `05` §5's twelve statuses and `05` §3.1's DoT/HoT cadence, for one fight.
/// </summary>
/// <remarks>
/// <para>
/// It is both seams at once — <see cref="IStatusEngine"/> (`18` §2.3's six ops) and
/// <see cref="IStatusTimeline"/> (`05` §3.1's slots 1, 2 and 4a) — because they are one store read
/// two ways. Splitting them would be two views of one fight's statuses, which is the failure
/// <c>BattleActor</c>'s own remarks record about <c>IEffectActorView</c>.
/// </para>
/// <para>
/// ═══ 🔒 <b>THE THREE OBLIGATIONS <see cref="IStatusTimeline"/> HANDED OVER, AND HOW EACH IS MET</b> ═══
/// </para>
/// <list type="number">
///   <item>
///     <b>Every DoT HP change routes the phase check</b> — and it does so <em>through the damage
///     pipeline</em> rather than beside it. `05` §3.1 requires a DoT tick to run <em>"ward absorption
///     (§4 step 9)"</em>, and this type has no ward pool and must not build a second one: the pool is
///     `05` §4.1's, one per actor, and <b>M2-09</b>'s. So a tick is handed to
///     <see cref="IAttackPipeline.DealMaxHpPctDamage"/>, which owns step 9 — the absorption, the HP
///     write and therefore the phase check that `05` §4 step 9 puts immediately after it. Calling
///     <c>AfterHpDecrease</c> here <em>as well</em> would fire <c>ON_LOW_HP</c> twice for one HP
///     change, and <c>ON_LOW_HP</c> is a <b>crossing</b>: a doubled observation is a doubled firing.
///     <c>StatusTimelineTests.Each_DoT_HP_change_runs_the_phase_check_and_ON_LOW_HP_exactly_once</c> pins it at exactly one.
///   </item>
///   <item>
///     <b><c>ON_LOW_HP</c> after every HP change</b> — same call, same reason.
///     <c>BattleServices.AfterHpDecrease</c> is both the phase check and the <c>ON_LOW_HP</c> sweep,
///     so routing the HP change through the one member that owns it satisfies both obligations
///     exactly once.
///   </item>
///   <item>
///     <b>HoT ticks go through <see cref="IAttackPipeline.Heal"/></b> — `05` §3.1: <em>"HoT ticks
///     route through <c>Heal()</c> (§4.3), not §4"</em>, so <c>HEAL%</c> applies and <c>ON_HEAL</c>
///     fires. <see cref="Tick"/> branches on <see cref="StatusKind"/> and there is no other path.
///   </item>
/// </list>
/// <para>
/// 🔴 <b>WHY <c>DealMaxHpPctDamage</c>, AND THE ONE THING IT COSTS.</b> `05` §3.1's exemption list
/// for a DoT tick is <em>"no dodge, crit or block; DEF mitigation does not apply; <c>DR%</c> and
/// <c>DAMAGE_TAKEN_MULT</c> apply; wards absorb them; the §4 damage floor does not apply; they
/// trigger no lifesteal and no thorns"</em>. `05` §4.2's row for <c>DAMAGE_MAXHP_PCT</c> is
/// <em>"no dodge, crit, block, mitigation or floor; <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c> do
/// apply; wards absorb; no lifesteal or thorns"</em>. They are the same list, item for item, so the
/// routing is `05`'s own and not a convenience — the member's <em>name</em> is about the op that
/// motivated it, and its <em>contract</em> is the routing class a DoT tick belongs to.
/// <br/>
/// ⚠️ What it costs is one log event. <c>CombatEventType.Hit</c>'s own remark is that <em>"the only
/// HP decrease that is not a <c>Hit</c> is a DoT tick, which has <c>StatusTick</c> so the replayer
/// can colour it purple"</em>, and <see cref="IAttackPipeline"/> has no way for a caller to say
/// <em>"this HP decrease is a DoT tick"</em>. This type therefore emits its own
/// <c>StatusTick</c> carrying the amount and the status, and M2-09's <c>Hit</c> — if it emits one —
/// sits beside it. <b>The signature was not changed</b>: M2-09 is in flight on it, and a second
/// milestone widening an interface underneath a running one is how a merge stops being reviewable.
/// Recorded here and in M2-10's report so M2-09 can suppress the <c>Hit</c> on the day it can see
/// the caller.
/// </para>
/// <para>
/// ⚠️ <b>Four stateful types under <c>Rules/</c></b>, which `30` §11.4 annotates as <em>"internal,
/// static, stateless calculators"</em> — recorded here once for all four, on
/// <c>BattleSimulation</c>'s, <c>BattleActor</c>'s and <c>CombatFlowState</c>'s precedent and for
/// their reason: a status is written by one tick and read by a later one, so it has to live
/// somewhere across ticks. They are <b>this type</b>, <see cref="ActorStatuses"/> (one per actor),
/// <see cref="StunWindow"/> (one per actor) and <see cref="StatusInstance"/> (one per status per
/// actor). This type is the sole owner of the other three: nothing else constructs them, they are
/// per-battle, never shared and never static. A grep for the departure finds all four from here.
/// </para>
/// </remarks>
internal sealed class StatusTimeline : IStatusTimeline, IStatusEngine
{
    /// <summary>
    /// 🔒 The prefix of the synthetic ids this type puts into `18` §8's aggregation and into failure
    /// messages. It cannot collide with an authored effect id: `18` §8 makes one an identifier and no
    /// identifier starts with <c>(</c> — the same guarantee <c>BattleSimulation.BasicAttackSourceId</c>
    /// and <c>RefreshStats</c>' <c>(stat-copy:…)</c> buckets rely on.
    /// </summary>
    private const string SyntheticIdPrefix = "(status:";

    private readonly BattleServices _services;
    private readonly StatusCatalogue _catalogue;

    private readonly Dictionary<string, ActorStatuses> _byActor = new(StringComparer.Ordinal);

    /// <summary>Builds the timeline for one fight.</summary>
    /// <param name="services">The battle — its log, its clock and its roster.</param>
    /// <param name="attack">
    /// `05` §4's pipeline. Every HP change a status causes goes through it (see the type remarks);
    /// nothing here writes HP directly.
    /// </param>
    /// <param name="catalogue"><c>content/statuses.json</c>, read — the whole of `05` §5.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal StatusTimeline(BattleServices services, IAttackPipeline attack, StatusCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(catalogue);

        _services = services;
        Attack = attack;
        _catalogue = catalogue;
    }

    /// <summary>`05` §4's pipeline, as this fight wired it.</summary>
    internal IAttackPipeline Attack { get; }

    // ══════════════════════════════════════════════════════════════════ IStatusTimeline

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 `05` §3.1 slot 1 — <em>"status timers advance by <c>TICK</c>. Every DoT/HoT instance whose
    /// cadence boundary falls on this tick applies its tick."</em>
    /// <para>
    /// There is no timer to advance: `05` §3.1's own ruling is that battle time is a function of the
    /// tick index and never accumulated (<c>BattleClock</c>), so an instance's remaining duration is
    /// <c>now − appliedAt</c> and its cadence is integer arithmetic on the anchor. What this slot does
    /// is therefore exactly the second sentence, and nothing else.
    /// </para>
    /// <para>
    /// The walk is over a snapshot in ascending effect-id order. A tick can kill the actor and a
    /// death can fire <c>ON_DEATH</c> effects that apply or remove statuses, and a collection mutated
    /// under a <c>foreach</c> throws — the same reason `05` §3.1's own slots are indexed loops.
    /// </para>
    /// </remarks>
    public void AdvanceTimers(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // 🔒 The early-out is on a counter, for StatModifiers' measured reason: this runs for every
        // actor on every one of 1800 ticks, and an actor carrying only a FREEZE has nothing here.
        if (!_byActor.TryGetValue(actor.Id, out var statuses) || statuses.TickingCount == 0)
        {
            return;
        }

        foreach (var instance in statuses.Ordered())
        {
            if (!instance.Definition.Ticks || !StatusCadence.LandsOn(instance.AnchorTick, tick))
            {
                continue;
            }

            // A DoT that killed the actor earlier in this same slot must not keep ticking a corpse:
            // `05` §3.1 step 6 puts an actor out of play "at that moment".
            if (!actor.IsAlive)
            {
                return;
            }

            // 🔴 THE SNAPSHOT CAN GO STALE UNDER THIS WALK, and liveness is not the only way — found
            // by review. A tick reaches BattleSimulation.AfterHpDecrease through the pipeline, which
            // fires the phase check AND ON_LOW_HP, and either can resolve an `18` §2.3
            // REMOVE_STATUS on an actor that is still alive. The instance is then gone from the
            // store but still in this materialised list, so without this it would tick AFTER its own
            // StatusExpired was logged. Remove-and-reapply in one handler is worse: the snapshot's
            // old instance would tick beside the new one, on the old stacks.
            if (!ReferenceEquals(statuses.Find(instance.Definition.Id), instance))
            {
                continue;
            }

            Tick(actor, instance, tick);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 `05` §3.1 slot 2 — <em>"statuses whose duration reached 0 expire, in ascending effect-id
    /// order. <b>A DoT expiring exactly on a cadence boundary deals that tick first, then
    /// expires.</b> Emit <c>StatusExpired</c>."</em>
    /// <para>
    /// 🔒 <b>The "deals that tick first" half is bought by the slot order and by nothing else.</b>
    /// Slot 1 runs the cadence for every actor before slot 2 runs any expiry, so an instance whose
    /// timer elapses on the same tick as a cadence boundary has already ticked by the time this runs.
    /// That means the rule is only true while <see cref="AdvanceTimers"/> refuses to expire anything
    /// and this refuses to tick anything — which is why neither does the other's work, and why
    /// <c>StatusTimelineTests.A_DoT_expiring_on_a_cadence_boundary_deals_that_tick_first_then_expires</c>
    /// pins a DoT whose duration lands exactly on its boundary at one tick of damage rather than zero.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ward segments are not this method's, though `05` §5 lists <c>WARD</c> among the
    /// statuses.</b> A <c>05</c> §4.2 <c>SHIELD</c> grants a segment in a fight that wires
    /// <c>NoStatusTimeline</c> and holds no status at all, so the sweep cannot live behind this
    /// seam — it is in the tick loop's own slot 2, beside the call to this. See
    /// <c>BattleSimulation</c> slot 2 and <c>BattleServices.ExpireWards</c>.
    /// </para>
    /// </remarks>
    public void ExpireDue(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!_byActor.TryGetValue(actor.Id, out var statuses))
        {
            return;
        }

        // 🔴 R3 — CurrentBossPhase is what makes `18` §6's PHASE scope mean anything. M2-10 left this
        //    probe without a phase because nothing in the battle could answer the question, and the
        //    consequence was silent: DurationEvaluator took §6's "outside a boss fight it behaves as
        //    BATTLE" fallback INSIDE boss fights, so every boss AURA outlived the phase that granted
        //    it. M2-12 added IBossPhases.CurrentPhase and routed it here.
        var probe = new DurationProbe
        {
            BattleTimeSeconds = BattleClock.SecondsAt(tick),
            CurrentPhase = _services.CurrentBossPhase,
        };

        foreach (var instance in statuses.Ordered())
        {
            if (!DurationEvaluator.Evaluate(instance.Application, probe).HasEnded)
            {
                continue;
            }

            statuses.Remove(instance.Definition.Id);
            Expired(actor, instance, tick);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 `05` §3.1 slot 4a's <em>"and not stunned"</em>, which is the whole of what the loop asks.
    /// `05` §5's <c>STUN</c> cap and its mandatory immunity window are <see cref="StunWindow"/>'s.
    /// </remarks>
    public bool CanAct(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return !_byActor.TryGetValue(actor.Id, out var statuses) ||
               statuses.Stun.CanAct(_services.Tick);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 `05` §3.1's <em>"current stack count, read at the moment the tick lands"</em>, and `18` §4's
    /// <c>HAS_STATUS</c>/<c>STATUS_STACKS</c>, are the same reading — this one. It is computed from
    /// <c>EffectStackSet.Count</c> on every call and never cached: M2-06 made the set immutable
    /// precisely so that a count belongs to a moment.
    /// <para>
    /// ⚠️ <b><c>WARD</c> answers <c>0</c>, and the reason is a real gap rather than an oversight.</b>
    /// `05` §5 defers <c>WARD</c> entirely — <em>"full semantics — stacking, bypass, ordering,
    /// <c>WardBroken</c> — in §4.1"</em> — and §4.1's pool is one absorb pool of segments per actor,
    /// owned by M2-09. This timeline keeps no second copy of it, because two stores of one pool is
    /// how a ward broken by damage stays "applied" in a condition. Until <c>IAttackPipeline</c>
    /// exposes the pool, <c>HAS_STATUS(WARD)</c> is false while a ward is up. No authored content
    /// reads it today. Registered in <c>SubjectSetFloorTests.Pending</c> under <c>WardPool</c>.
    /// </para>
    /// </remarks>
    public int StacksOn(BattleActor actor, string statusId)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return _byActor.TryGetValue(actor.Id, out var statuses) ? statuses.Stacks(statusId) : 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 `18` §8 step 1 — <em>"collect all <b>active</b> effects"</em>. A live `05` §5 debuff or
    /// buff is one, and this is how it reaches the aggregation.
    /// <para>
    /// Each live <see cref="StatusPotencyBasis.TargetStatPct"/> instance becomes one synthetic
    /// <c>STAT_ADD_PCT</c> carrying <c>EffectStackSet.CombinedValue</c> — the sum of its applications
    /// under <c>ADDITIVE</c>, which is exactly what <em>"−X% DEF, stacks to 5"</em> means at five
    /// stacks. The id is synthetic (see <see cref="SyntheticIdPrefix"/>), on the precedent
    /// <c>BattleSimulation.RefreshStats</c> set for `18` §2.4's <c>STAT_COPY</c> buckets: they are
    /// not authored effects, so they are stated as synthetic ops under an id no authored effect can
    /// take.
    /// </para>
    /// <para>
    /// 🔒 <b>Percent adds, not multipliers.</b> `05` §5 writes every one of the six as a signed
    /// percentage of a stat (<em>"−50% ASPD"</em>, <em>"−X% DEF"</em>, <em>"+X% ASPD"</em>), which is
    /// `18` §2.1's <c>STAT_ADD_PCT</c> bucket at §8 step 5. R1's <em>the <c>STAT_MULT</c> value IS the
    /// multiplier</em> is about the other op, and reading a −0.5 as a ×(−0.5) would invert
    /// <c>FREEZE</c> into a negative ASPD.
    /// </para>
    /// </remarks>
    public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // 🔒 The early-out is on a counter, not on a walk. This runs for every state-dependent actor
        // on every one of 1800 ticks and `05` budgets a whole fight at < 5 ms; the commonest answer
        // is "none", because a BURN and a REGEN feed no stat. See ActorStatuses.StatModifierCount.
        if (!_byActor.TryGetValue(actor.Id, out var statuses) || statuses.StatModifierCount == 0)
        {
            return [];
        }

        List<EffectDefinition>? modifiers = null;

        foreach (var instance in statuses.Ordered())
        {
            if (instance.Definition.Basis != StatusPotencyBasis.TargetStatPct)
            {
                continue;
            }

            modifiers ??= new List<EffectDefinition>(1);
            modifiers.Add(new EffectDefinition
            {
                Id = SyntheticIdPrefix + instance.Definition.Id + ")",
                Op = EffectOp.STAT_ADD_PCT,
                Stat = StatSelector.Of(instance.Definition.RequireStat()),
                Value = instance.Stacks.CombinedValue,
            });
        }

        return modifiers ?? (IReadOnlyList<EffectDefinition>)[];
    }

    // ══════════════════════════════════════════════════════════════════ IStatusEngine

    /// <inheritdoc />
    public void Apply(
        IEffectActorView applier, IEffectActorView target, string statusId, double potency,
        EffectDuration? duration, EffectStacking? stacking, string sourceEffectId)
    {
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(target);

        var definition = _catalogue.Of(statusId);
        var receiver = Actor(target, sourceEffectId);
        var statuses = For(receiver);

        if (statuses.IsImmuneTo(statusId, _services.Tick))
        {
            return;
        }

        // 🔒 STUN first, because it is the one row with no magnitude at all — `05` §5 gives it a
        // duration and nothing else. Computing a potency for it and discarding it (which the first
        // version of this method did) runs the STATUS_POWER_PCT multiply for nothing.
        if (definition.Basis == StatusPotencyBasis.None)
        {
            ApplyStun(receiver, statuses, duration, sourceEffectId);
            return;
        }

        // 🔒 `05` §5's FREEZE is the one row stated as a literal rather than as X — "−50% ASPD" — so
        // the number is the status's and not the applying effect's. Everything else takes the
        // effect's value, scaled by whatever STATUS_POWER_PCT the APPLIER carries (§2.3: "the potency
        // of statuses THIS ACTOR APPLIES" — outgoing, read off the applier, never the target).
        //
        // ⚠️ FREEZE is therefore the one status a STATUS_POWER_PCT build cannot amplify, and that is
        // a ruling rather than an oversight: `05` §5 states the −50% as the status's own constant,
        // not as an X the applier supplies, so there is no authored X for §2.3 to scale. If a later
        // design wants FREEZE amplifiable, `05` §5 is what has to restate it as an X.
        var x = definition.FixedPotency ?? StatRounding.Round(
            potency * OutgoingPowerScale(applier, sourceEffectId));

        if (definition.Basis == StatusPotencyBasis.FlatHp)
        {
            // 🔒 `05` §5 defers WARD wholly to §4.1, whose pool, segments, cap, ordering and
            // WardBroken are M2-09's. Granting a segment IS applying the status; a second record
            // here would be a second pool.
            //
            // ⚠️ TWO THINGS ARE LOST HERE AND BOTH ARE M2-09'S TO GIVE BACK, recorded rather than
            // hidden. (1) The application's `duration` is dropped: `05` §4.1 types a segment as
            // {amount, expiresAt?, sourceEffectId} but IAttackPipeline.GrantWard carries no duration
            // parameter, so an APPLY_STATUS WARD with a D produces a PERMANENT segment. (2) No
            // StatusApplied event is emitted from here, so `05` §7's replay sees a ward grant only
            // if GrantWard emits its own §4.1 Shield event. M2-09 is in flight on that interface and
            // widening it underneath a running task is how a merge stops being reviewable, so this
            // is reported rather than fixed.
            Attack.GrantWard(target, x, sourceCapPct: null, sourceEffectId);

            return;
        }

        var contribution = definition.Basis == StatusPotencyBasis.ApplierAtkPctPerSecond

            // 🔒 The applier-side half of the basis is folded in HERE and never again. `05` §5 says
            // BLEED is "set at application", `05` §3.1 says "potency was fixed at application", and
            // the applier may be dead when the tick lands.
            ? StatRounding.Round(x * Actor(applier, sourceEffectId).Stats[StatId.ATK])
            : x;

        var application = new EffectApplication
        {
            EffectId = sourceEffectId,
            Duration = ScaledDuration(receiver, duration),
            AppliedAtSeconds = BattleClock.SecondsAt(_services.Tick),

            // 🔴 R3 — the other half of `18` §6's PHASE scope: §6 ends the effect when the boss exits
            //    "the phase in which the effect WAS APPLIED", so the phase has to be stamped here, at
            //    application, and not re-read at expiry. null outside a boss fight, which is §6's own
            //    BATTLE fallback. See BattleServices.CurrentBossPhase.
            AppliedInPhase = _services.CurrentBossPhase,
        };

        var instance = statuses.Find(statusId);

        if (instance is null)
        {
            statuses.Add(new StatusInstance
            {
                Definition = definition,

                // 🔒 THE ANCHOR, set once. `05` §3.1: the instance ticks "on the 20th simulation tick
                // after FIRST application", and reapplication "never re-anchors the cadence".
                AnchorTick = _services.Tick,
                Stacks = EffectStackSet
                    .Empty(sourceEffectId, _catalogue.StackingFor(statusId, stacking))
                    .Apply(contribution)
                    .Stacks,
                Application = application,
                SourceEffectId = sourceEffectId,
            });
        }
        else
        {
            // 🔒 Reapplication: stacks and duration, per `18` §6. StatusInstance.AnchorTick is
            // init-only, so the cadence cannot be moved from here even by mistake.
            instance.Reapply(contribution, application, sourceEffectId);
        }

        Log(receiver, CombatEventType.StatusApplied, definition, x);
        Restat(receiver, definition);
    }

    /// <inheritdoc />
    public void Remove(IEffectActorView target, string statusId, string sourceEffectId)
    {
        var receiver = Actor(target, sourceEffectId);

        // The catalogue call is not decoration: it refuses an id outside `05` §5's twelve, so a
        // REMOVE_STATUS naming a status that does not exist fails rather than silently removing
        // nothing, which is indistinguishable from succeeding.
        var definition = _catalogue.Of(statusId);

        if (!_byActor.TryGetValue(receiver.Id, out var statuses))
        {
            return;
        }

        if (definition.Basis == StatusPotencyBasis.None)
        {
            // 🔒 The stun goes; the immunity window stays. A cleanse that also cleared the window
            // would make a cleansed hero a BETTER stun-lock target than an uncleansed one, which
            // inverts `05` §5's "stun immunity is mandatory".
            statuses.Stun.Clear();
        }

        var instance = statuses.Find(statusId);
        if (instance is null)
        {
            return;
        }

        statuses.Remove(statusId);
        Expired(receiver, instance, _services.Tick);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// 🔴 <b>A REFUSAL, not a no-op — the first version of this method was the silent kind and
    /// review caught it.</b> That version argued that <see cref="StatusTag"/> is <em>"deliberately
    /// empty of members"</em> so no tag could ever match. The premise is false:
    /// <c>StatusTag</c> is a <b>string-valued</b> record struct, so every label is expressible and
    /// <c>game-data/schema/effect.schema.json</c> already accepts any <c>^[a-z][a-z0-9_]*$</c> for
    /// <c>statusTag</c>. What is actually missing is the <em>vocabulary</em>: `05` §5 tags none of
    /// its twelve, <c>content/statuses.json</c> authors no <c>tags</c> key, and
    /// <c>StatusTag</c>'s own remarks hand that over — <em>"which labels exist is M2-10's status
    /// catalogue to author"</em> — which this task did not do, because inventing a taxonomy nobody
    /// agreed is exactly steering S6.
    /// </para>
    /// <para>
    /// 🔒 So an authored <c>REMOVE_STATUS {statusTag}</c> reaches a feature that does not exist, and
    /// that is <c>EffectOpSeams</c>' stated shape for exactly this case: <em>"a silent no-op turns
    /// 'M2-09 has not landed' into 'this perk does nothing', which is a balance bug rather than an
    /// error, and the balance harness would attribute it to the content."</em> It fails loudly
    /// instead. No authored content in the repository uses the tag form today, so nothing regresses.
    /// </para>
    /// </remarks>
    /// <exception cref="EffectContextException">Always — no status carries a tag to match.</exception>
    public void RemoveByTag(IEffectActorView target, StatusTag tag, string sourceEffectId)
    {
        ArgumentNullException.ThrowIfNull(target);

        _ = Actor(target, sourceEffectId);

        throw new EffectContextException(
            sourceEffectId,
            $"it clears the status tag group '{tag.Value}' and no status carries a tag",
            "18 §2.3's REMOVE_STATUS takes a statusId OR a statusTag, and the tag form needs a " +
            "vocabulary. 05 §5 tags none of its twelve statuses, content/statuses.json authors no " +
            "tags key, and StatusTag's own remarks assign that vocabulary to the status catalogue — " +
            "M2-10 declined to invent one (16 R6). Removing nothing and reporting success would be " +
            "indistinguishable from removing the right thing. Author the tags, or clear by statusId.");
    }

    /// <inheritdoc />
    public void Extend(IEffectActorView target, string statusId, double seconds, string sourceEffectId)
    {
        var receiver = Actor(target, sourceEffectId);
        _ = _catalogue.Of(statusId);

        if (!_byActor.TryGetValue(receiver.Id, out var statuses) ||
            statuses.Find(statusId) is not { } instance)
        {
            return;
        }

        var duration = instance.Application.Duration;
        if (duration?.Seconds is not { } current)
        {
            // An instance with no timer has nothing to extend, and inventing one would give a
            // permanent status an end. `18` §2.3 adds duration to an existing status; it does not
            // convert an untimed one.
            return;
        }

        // 🔒 The timer moves and the anchor does not — the same separation `05` §3.1 draws for a
        // reapplication. An EXTEND_STATUS that re-anchored would reset a DoT's cadence, which is the
        // one thing the section says reapplication may never do.
        instance.Application = instance.Application with
        {
            Duration = duration with { Seconds = StatRounding.Round(current + seconds) },
        };
    }

    /// <inheritdoc />
    public void GrantImmunity(
        IEffectActorView target, string statusId, EffectDuration? duration, string sourceEffectId)
    {
        var receiver = Actor(target, sourceEffectId);
        _ = _catalogue.Of(statusId);

        For(receiver).GrantImmunity(statusId, _services.Tick, duration);
    }

    /// <inheritdoc />
    /// <remarks>
    /// `18` §2.3 — <em>"scale the potency of statuses <b>this actor applies</b>"</em>. Held on the
    /// actor and read at the moment that actor applies a status, which is what makes it outgoing.
    /// </remarks>
    public void ScaleOutgoingPower(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        For(Actor(target, sourceEffectId)).AddOutgoingPower(fraction, Seconds(duration), _services.Tick);

    /// <inheritdoc />
    /// <remarks>
    /// `18` §2.3 — <em>"scale duration of statuses <b>applied to</b> this actor"</em>. The opposite
    /// direction from <see cref="ScaleOutgoingPower"/>, and read at the moment a status lands on this
    /// actor.
    /// </remarks>
    public void ScaleIncomingDuration(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        For(Actor(target, sourceEffectId)).AddIncomingDuration(fraction, Seconds(duration), _services.Tick);

    // ══════════════════════════════════════════════════════════════════ the cadence tick

    /// <summary>
    /// 🔒 One cadence boundary, for one instance — `05` §3.1's <em>"per-tick amount = per-second
    /// potency × <b>current stack count, read at the moment the tick lands</b>"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The stack count is read here, inside the tick, and nowhere earlier.</b> Not at
    /// application, not at the top of the tick, not from a field written when the stack changed —
    /// <c>EffectStackSet</c> is immutable so that <c>Count</c> belongs to a moment, and this is the
    /// moment. A cached count is invisible in every fight where the stacks do not change between
    /// application and boundary, which is most of them.
    /// </para>
    /// <para>
    /// ⚠️ <b>No draw is taken.</b> `05` §3.1 is explicit that a DoT tick is <em>"a damage event, not
    /// an attack: no dodge, crit or block"</em>, and all three of those are the only things in `05`
    /// §4 that draw. Nothing on this path touches <c>BattleServices.Rng</c>, and
    /// <c>StatusDeterminismTests.A_fight_full_of_status_ticks_takes_exactly_the_draws_a_fight_with_none_takes</c>
    /// pins the draw count across a fight full of DoTs at exactly the count of a fight with none.
    /// </para>
    /// </remarks>
    private void Tick(BattleActor actor, StatusInstance instance, int tick)
    {
        var definition = instance.Definition;

        // ── `05` §3.1's per-tick amount, in the order the sentence writes it.
        var perSecond = instance.Stacks.CombinedValue;

        var amount = definition.Basis == StatusPotencyBasis.TargetMaxHpPctPerSecond
            ? perSecond * actor.MaxHp
            : perSecond;

        if (definition.ScalesWithTargetMissingHp)
        {
            // 📐 `05` §5 — BLEED: "each tick deals that amount × (1 + target's missing-HP fraction)".
            // The coefficient is content/statuses.json's, which is what closes the section's 📐.
            var missing = actor.MaxHp <= 0.0
                ? 0.0
                : StatRounding.Round((actor.MaxHp - actor.CurrentHp) / actor.MaxHp);

            amount *= 1.0 + (_catalogue.BleedMissingHpScaling * missing);
        }

        amount = StatRounding.Round(amount);

        // `05` §7 step 7 — appended at the moment of the state change, and BEFORE the HP change, so
        // a replayer knows which status the decrease that follows belongs to. CombatEventType.Hit:
        // "the only HP decrease that is not a Hit is a DoT tick, which has StatusTick".
        Log(actor, CombatEventType.StatusTick, definition, amount);

        if (definition.Kind == StatusKind.HoT)
        {
            // 🔒 `05` §3.1: "HoT ticks route through Heal() (§4.3), not §4" — so HEAL% applies and
            // ON_HEAL fires. Obligation 3.
            Attack.Heal(actor, amount, instance.SourceEffectId);

            return;
        }

        // 🔒 Obligations 1 and 2, met through the pipeline that owns `05` §4 step 9 — ward
        // absorption, the HP write, and the phase check immediately after it. See the type remarks
        // for why this member and why not a second call to AfterHpDecrease.
        Attack.DealMaxHpPctDamage(actor, amount, bypassesWards: false, instance.SourceEffectId);
    }

    // ══════════════════════════════════════════════════════════════════ helpers

    /// <summary>`05` §5's <c>STUN</c>: the cap, the mandatory immunity window, and the instance.</summary>
    private void ApplyStun(
        BattleActor receiver, ActorStatuses statuses, EffectDuration? duration, string sourceEffectId)
    {
        // 🔴 A STUN WITH NO D IS REFUSED, not defaulted. The first version substituted `05` §5's
        // 1.5 s cap, which invents a number in the direction of the longest stun the game allows —
        // steering S6, and a balance decision made in an engine helper.
        if (Seconds(duration) is not { } authored)
        {
            throw new EffectContextException(
                sourceEffectId,
                "it applies STUN with no duration",
                "05 §5 states STUN as 'cannot act for D s' and D is the applying effect's own " +
                "18 §6 duration. Substituting the 1.5 s per-application cap would silently grant " +
                "the longest legal stun to every effect that forgot to author one.");
        }

        // 🔒 `18` §2.3's STATUS_DURATION_PCT — "scale duration of statuses APPLIED TO this actor" —
        // with no carve-out for the one status that is nothing but a duration. Scaled BEFORE the cap
        // so that `05` §5's 1.5 s ceiling still binds on the scaled value rather than being applied
        // to a number the debuff then inflates past it.
        var requested = Seconds(ScaledDuration(receiver, duration)) ?? authored;
        var until = statuses.Stun.Apply(_services.Tick, requested);

        if (until is null)
        {
            // Refused by `05` §5's immunity window. No instance, no event: nothing happened.
            return;
        }

        var granted = StatRounding.Round(
            BattleClock.SecondsAt(until.Value + 1) - BattleClock.SecondsAt(_services.Tick));

        var definition = _catalogue.Of(StunId);
        var application = new EffectApplication
        {
            EffectId = sourceEffectId,
            Duration = new EffectDuration { Scope = DurationScope.BATTLE, Seconds = granted },
            AppliedAtSeconds = BattleClock.SecondsAt(_services.Tick),
        };

        var instance = statuses.Find(StunId);
        if (instance is null)
        {
            statuses.Add(new StatusInstance
            {
                Definition = definition,
                AnchorTick = _services.Tick,
                Stacks = EffectStackSet
                    .Empty(sourceEffectId, _catalogue.StackingFor(StunId, authored: null))
                    .Apply(0.0)
                    .Stacks,
                Application = application,
                SourceEffectId = sourceEffectId,
            });
        }
        else
        {
            instance.Application = application;
            instance.SourceEffectId = sourceEffectId;
        }

        Log(receiver, CombatEventType.StatusApplied, definition, granted);
    }

    /// <summary>
    /// `05` §5's <c>STUN</c> id — an alias of <see cref="StatusIds.Stun"/>, which is where it is
    /// spelled.
    /// </summary>
    /// <remarks>
    /// 🔴 This constant used to hold the literal and claim to be the single naming, while
    /// <c>BossBuiltIns</c> held its own <c>StunStatusId</c> two directories away with the same claim.
    /// The word now lives in one place.
    /// </remarks>
    private const string StunId = StatusIds.Stun;

    private ActorStatuses For(BattleActor actor)
    {
        if (!_byActor.TryGetValue(actor.Id, out var statuses))
        {
            statuses = new ActorStatuses(
                new StunWindow(
                    _catalogue.StunMaxSecondsPerApplication, _catalogue.StunImmunityWindowSeconds));

            _byActor[actor.Id] = statuses;
        }

        return statuses;
    }

    /// <summary>
    /// `18` §2.3's <c>STATUS_DURATION_PCT</c>, applied to the incoming application's <c>D</c>.
    /// </summary>
    private EffectDuration? ScaledDuration(BattleActor receiver, EffectDuration? duration)
    {
        if (duration?.Seconds is not { } seconds)
        {
            return duration;
        }

        // For() has already run for every caller, so the actor always has a store by now.
        var scale = For(receiver).IncomingDurationScale(_services.Tick);

        return scale == 1.0
            ? duration
            : duration with { Seconds = StatRounding.Round(seconds * scale) };
    }

    private double OutgoingPowerScale(IEffectActorView applier, string sourceEffectId) =>
        _byActor.TryGetValue(Actor(applier, sourceEffectId).Id, out var statuses)
            ? statuses.OutgoingPowerScale(_services.Tick)
            : 1.0;

    private void Log(BattleActor actor, CombatEventType type, StatusDefinition definition, double value) =>
        _services.Log.Append(
            _services.Tick, type, actor.LogId, actor.LogId, value, StatusLogId.Of(definition.Id));

    private void Expired(BattleActor actor, StatusInstance instance, int tick)
    {
        _services.Log.Append(
            tick, CombatEventType.StatusExpired, actor.LogId, actor.LogId,
            0.0, StatusLogId.Of(instance.Definition.Id));

        Restat(actor, instance.Definition);
    }

    /// <summary>
    /// 🔒 Every change to a status that feeds `18` §8 invalidates the actor's aggregation.
    /// </summary>
    /// <remarks>
    /// Only the six <see cref="StatusPotencyBasis.TargetStatPct"/> rows feed it, and marking the
    /// other six stale would re-aggregate nine actors on every DoT application for no change —
    /// <c>BattleSimulation.RefreshStats</c> is the whole of `05`'s &lt; 5 ms budget. It is stated as a
    /// test of the <b>basis</b> rather than of the id, so a status added to that basis is covered
    /// without a second edit here.
    /// </remarks>
    private static void Restat(BattleActor actor, StatusDefinition definition)
    {
        if (definition.Basis == StatusPotencyBasis.TargetStatPct)
        {
            actor.InvalidateStats();
        }
    }

    private static double? Seconds(EffectDuration? duration) => duration?.Seconds;

    /// <summary>
    /// The <see cref="BattleActor"/> behind an <see cref="IEffectActorView"/>.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>BattleActor</c>'s own remarks make it <em>"one view of one battle, not two"</em>: it is
    /// the only implementation of <see cref="IEffectActorView"/> a fight produces, and the cast is
    /// that fact rather than an assumption. A different implementation reaching here is a second
    /// roster, which is exactly what the single-view rule forbids, so it fails by name.
    /// </remarks>
    private static BattleActor Actor(IEffectActorView view, string sourceEffectId)
    {
        // 🔒 The guard lives HERE and not on each of the seven seam members, which is what review
        // found: five of them had none, so a null target produced an NRE on view.GetType() below
        // rather than naming the parameter. One funnel, one guard.
        ArgumentNullException.ThrowIfNull(view);

        return view as BattleActor ?? throw new EffectContextException(
            sourceEffectId,
            $"its target is a {view.GetType().Name}, not a BattleActor",
            "05 §5's statuses live on the fight's own actors. BattleActor is the single " +
            "IEffectActorView a battle produces — its own remarks are that two views of one battle " +
            "are two chances to disagree about who is alive — so a second implementation reaching " +
            "the status engine is a second roster, not a substitutable view.");
    }
}
